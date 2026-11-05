using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : prépare et envoie un e-mail de récupération ou de déblocage, en arrière-plan (GR Auth, SS §2).
public class BackgroundEmailRequestProcessingService
{
    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomTokens;
    private readonly IEmailSender senderOfTheEmailsToTheVisitors;
    private readonly PublicWebsiteSettings settingsOfThePublicWalletFlowWebsite;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont ces e-mails ont besoin.
    public BackgroundEmailRequestProcessingService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase,
        ISecureTokenGenerator generatorOfTheSecureRandomTokens,
        IEmailSender senderOfTheEmailsToTheVisitors,
        PublicWebsiteSettings settingsOfThePublicWalletFlowWebsite,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.repositoryOfTheSecurityEventsInDatabase = repositoryOfTheSecurityEventsInDatabase;
        this.generatorOfTheSecureRandomTokens = generatorOfTheSecureRandomTokens;
        this.senderOfTheEmailsToTheVisitors = senderOfTheEmailsToTheVisitors;
        this.settingsOfThePublicWalletFlowWebsite = settingsOfThePublicWalletFlowWebsite;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Traite une demande de la file.
    public async Task<BackgroundEmailRequestResult> ProcessOneBackgroundEmailRequestAsync(BackgroundEmailRequest requestToProcessInTheBackground)
    {
        if (requestToProcessInTheBackground.TypeOfTheRequestedBackgroundEmail == BackgroundEmailRequestType.PasswordRecovery)
        {
            return await ProcessOnePasswordRecoveryRequestAsync(requestToProcessInTheBackground);
        }

        return await ProcessOneAccountUnlockRequestAsync(requestToProcessInTheBackground);
    }

    // Choisit l'e-mail selon l'état du compte. Une adresse sans compte ne reçoit rien, et laisse une trace sans identifiant.
    private async Task<BackgroundEmailRequestResult> ProcessOnePasswordRecoveryRequestAsync(BackgroundEmailRequest requestToProcessInTheBackground)
    {
        var accountUsingTheRequestedEmailAddress = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByEmailAddressAsync(requestToProcessInTheBackground.EmailAddressOfTheRecoveryRequest!);

        if (accountUsingTheRequestedEmailAddress == null)
        {
            await SavePasswordRecoveryRequestInTheJournalAsync(SecurityEventResult.Failure, null, requestToProcessInTheBackground);
            return new BackgroundEmailRequestResult();
        }

        if (!accountUsingTheRequestedEmailAddress.IsEmailConfirmed)
        {
            var invitationEmailForTheAddressOwner = AccountRecoveryEmails.CreateInvitationEmailForAddressNotYetConfirmed(accountUsingTheRequestedEmailAddress.Email);
            return await SendEmailThenSaveRecoveryRequestInTheJournalAsync(invitationEmailForTheAddressOwner, SecurityEventResult.Failure, accountUsingTheRequestedEmailAddress, requestToProcessInTheBackground);
        }

        var generatedTokenOfTheRecoveryLink = await ReplaceLinkOfThisTypeForThisAccountAsync(
            accountUsingTheRequestedEmailAddress.Id,
            EmailTokenType.PasswordRecovery,
            EmailTokenValidityDurations.ValidityDurationOfThePasswordRecoveryLink);
        var passwordRecoveryEmailForTheAddressOwner = AccountRecoveryEmails.CreatePasswordRecoveryEmailWithItsLink(
            accountUsingTheRequestedEmailAddress.Email,
            settingsOfThePublicWalletFlowWebsite.BuildLinkToThisWebsitePageWithItsToken("/reset-password", generatedTokenOfTheRecoveryLink.TokenGivenOnlyToTheVisitor));

        return await SendEmailThenSaveRecoveryRequestInTheJournalAsync(passwordRecoveryEmailForTheAddressOwner, SecurityEventResult.Success, accountUsingTheRequestedEmailAddress, requestToProcessInTheBackground);
    }

    // Envoie l'e-mail, puis note la demande au journal. Si l'e-mail ne part pas, la demande est notée comme un échec.
    private async Task<BackgroundEmailRequestResult> SendEmailThenSaveRecoveryRequestInTheJournalAsync(
        EmailMessageToSend emailMessageToSendToTheAddressOwner,
        SecurityEventResult resultNotedIfTheEmailLeaves,
        User accountUsingTheRequestedEmailAddress,
        BackgroundEmailRequest requestToProcessInTheBackground)
    {
        var resultOfTheEmailSendingToTheOwner = await TryToSendEmailToTheAddressOwnerAsync(emailMessageToSendToTheAddressOwner, accountUsingTheRequestedEmailAddress.Id);
        var resultNotedForThisRecoveryRequest = resultOfTheEmailSendingToTheOwner.EmailCouldNotBeSent ? SecurityEventResult.Failure : resultNotedIfTheEmailLeaves;

        await SavePasswordRecoveryRequestInTheJournalAsync(resultNotedForThisRecoveryRequest, accountUsingTheRequestedEmailAddress.Id, requestToProcessInTheBackground);

        return resultOfTheEmailSendingToTheOwner;
    }

    // Envoie le lien de déblocage, sauf si le compte a disparu ou n'est plus bloqué : il n'y a alors rien à débloquer.
    private async Task<BackgroundEmailRequestResult> ProcessOneAccountUnlockRequestAsync(BackgroundEmailRequest requestToProcessInTheBackground)
    {
        var accountJustLockedByTheLoginAttempt = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(requestToProcessInTheBackground.IdentifierOfTheAccountJustLocked!.Value);

        if (accountJustLockedByTheLoginAttempt == null
            || !AccountLockoutRules.CheckThatAccountIsLockedAtThisDate(accountJustLockedByTheLoginAttempt, clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime))
        {
            return new BackgroundEmailRequestResult();
        }

        var generatedTokenOfTheUnlockLink = await ReplaceLinkOfThisTypeForThisAccountAsync(
            accountJustLockedByTheLoginAttempt.Id,
            EmailTokenType.AccountUnlock,
            EmailTokenValidityDurations.ValidityDurationOfTheAccountUnlockLink);
        var accountUnlockEmailForTheAddressOwner = AccountRecoveryEmails.CreateAccountUnlockEmailWithItsLink(
            accountJustLockedByTheLoginAttempt.Email,
            settingsOfThePublicWalletFlowWebsite.BuildLinkToThisWebsitePageWithItsToken("/unlock-account", generatedTokenOfTheUnlockLink.TokenGivenOnlyToTheVisitor),
            AccountLockoutRules.CalculateDurationOfTheLockoutWithThisNumber(accountJustLockedByTheLoginAttempt.LockoutCount));

        return await TryToSendEmailToTheAddressOwnerAsync(accountUnlockEmailForTheAddressOwner, accountJustLockedByTheLoginAttempt.Id);
    }

    // Tire un nouveau jeton et remplace l'ancien lien du même type, qui ne marche plus (SS §2). Seule l'empreinte va en base.
    private async Task<GeneratedSecureToken> ReplaceLinkOfThisTypeForThisAccountAsync(Guid identifierOfTheAccountReceivingTheLink, EmailTokenType typeOfTheLinkToReplaceNow, TimeSpan validityDurationOfTheNewLink)
    {
        var generatedTokenOfTheNewLink = generatorOfTheSecureRandomTokens.GenerateNewRandomSecureTokenWithItsHash();

        await repositoryOfTheUserAccountsInDatabase.ReplaceEmailTokenOfThisTypeForThisAccountAsync(new EmailToken
        {
            Type = typeOfTheLinkToReplaceNow,
            TokenHash = generatedTokenOfTheNewLink.HashToStoreInTheDatabase,
            ExpiresAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime + validityDurationOfTheNewLink,
            UserId = identifierOfTheAccountReceivingTheLink
        });

        return generatedTokenOfTheNewLink;
    }

    // Envoie un e-mail. S'il ne peut pas partir, la panne ira au journal technique : l'annoncer au visiteur révélerait le compte (SS §2).
    private async Task<BackgroundEmailRequestResult> TryToSendEmailToTheAddressOwnerAsync(EmailMessageToSend emailMessageToSendToTheAddressOwner, Guid identifierOfTheAccountConcernedByTheEmail)
    {
        try
        {
            await senderOfTheEmailsToTheVisitors.SendEmailToItsRecipientAsync(emailMessageToSendToTheAddressOwner);
            return new BackgroundEmailRequestResult();
        }
        catch (EmailCouldNotBeSentException)
        {
            return new BackgroundEmailRequestResult { EmailCouldNotBeSent = true, UserIdentifierForTheTechnicalLog = identifierOfTheAccountConcernedByTheEmail };
        }
    }

    // Écrit la demande de récupération dans le journal de sécurité, sans jamais l'adresse e-mail (SS §7 et §11).
    private async Task SavePasswordRecoveryRequestInTheJournalAsync(SecurityEventResult resultNotedInTheSecurityJournal, Guid? identifierOfTheAccountIfItExists, BackgroundEmailRequest requestToProcessInTheBackground)
    {
        await repositoryOfTheSecurityEventsInDatabase.SaveSecurityEventInTheJournalAsync(new SecurityEvent
        {
            Type = SecurityEventType.PasswordRecovery,
            Result = resultNotedInTheSecurityJournal,
            OccurredAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime,
            IpAddress = requestToProcessInTheBackground.IpAddressOfTheRequestingVisitor,
            UserId = identifierOfTheAccountIfItExists
        });
    }
}
