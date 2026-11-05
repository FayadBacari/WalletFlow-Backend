using System.Net;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : un visiteur demande à retrouver son mot de passe (GR Auth, SS §2 et §6).
// Ce service ne lit jamais la base : sa réponse et son temps de réponse sont donc les mêmes pour toutes les adresses.
public class PasswordRecoveryRequestService
{
    private readonly IPasswordRecoveryRequestLimiter limiterOfTheRecoveryRequestsPerEmailAddress;
    private readonly IBackgroundEmailRequestQueue queueOfTheEmailsToPrepareInTheBackground;

    // Reçoit la limite par adresse e-mail et la file des e-mails.
    public PasswordRecoveryRequestService(
        IPasswordRecoveryRequestLimiter limiterOfTheRecoveryRequestsPerEmailAddress,
        IBackgroundEmailRequestQueue queueOfTheEmailsToPrepareInTheBackground)
    {
        this.limiterOfTheRecoveryRequestsPerEmailAddress = limiterOfTheRecoveryRequestsPerEmailAddress;
        this.queueOfTheEmailsToPrepareInTheBackground = queueOfTheEmailsToPrepareInTheBackground;
    }

    // Vérifie l'adresse et la limite, puis confie la suite à la file des e-mails.
    // La limite compte les demandes, qu'un compte existe ou non : elle ne révèle donc rien.
    public PasswordRecoveryRequestResult RequestPasswordRecoveryForThisEmailAddress(string emailAddressTypedByTheVisitor, IPAddress? ipAddressOfTheRequestingVisitor)
    {
        var emailAddressInItsStoredForm = EmailAddressRules.ConvertEmailAddressIntoItsStoredForm(emailAddressTypedByTheVisitor);

        if (!EmailAddressRules.CheckThatEmailAddressHasValidFormat(emailAddressInItsStoredForm))
        {
            return new PasswordRecoveryRequestResult { EmailAddressErrorMessage = AccountRegistrationService.ErrorMessageForInvalidEmailAddressFormat };
        }

        if (!limiterOfTheRecoveryRequestsPerEmailAddress.TryToCountNewRequestForThisEmailAddress(emailAddressInItsStoredForm))
        {
            return new PasswordRecoveryRequestResult { TooManyRequestsForThisEmailAddress = true };
        }

        queueOfTheEmailsToPrepareInTheBackground.AddEmailRequestToTheBackgroundQueue(new BackgroundEmailRequest
        {
            TypeOfTheRequestedBackgroundEmail = BackgroundEmailRequestType.PasswordRecovery,
            EmailAddressOfTheRecoveryRequest = emailAddressInItsStoredForm,
            IpAddressOfTheRequestingVisitor = ipAddressOfTheRequestingVisitor
        });

        return new PasswordRecoveryRequestResult();
    }
}
