using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.TwoFactorServicesWithFakeTools;

namespace WalletFlow.Tests.Application;

// Vérifie le lien du support qui désactive la double authentification (GR Auth, SS §2) : le mot de passe, la règle des 7 jours,
// les 5 essais comptés avant chaque vérification, et ce que la désactivation ferme et envoie.
public class TwoFactorSupportLinkServiceTests
{
    private readonly TwoFactorServicesWithFakeTools services = new TwoFactorServicesWithFakeTools();
    private readonly ActivatedAccount activatedAccount;
    private readonly string linkToken;
    private readonly EmailToken link;

    // Un compte avec la double authentification, et le lien que le support lui a fait envoyer.
    public TwoFactorSupportLinkServiceTests()
    {
        activatedAccount = services.AddActivatedAccount();
        var generated = services.TokenGenerator.GenerateToken();
        linkToken = generated.Token;
        link = new EmailToken
        {
            Type = EmailTokenType.TwoFactorDeactivation,
            TokenHash = generated.Hash,
            ExpiresAt = Now + EmailTokenValidityDurations.TwoFactorDeactivationLink,
            UserId = activatedAccount.UserAccount.Id
        };
        services.AccountRepository.SavedEmailTokens.Add(link);
    }

    private DateTime Now => services.Clock.GetUtcNow().UtcDateTime;

    private User Account => activatedAccount.UserAccount;

    private async Task<TwoFactorSupportLinkResult> DeactivateAsync(string password, string? token = null)
    {
        return await services.CreateSupportLinkService().DeactivateAsync(token ?? linkToken, password, IpAddress);
    }

    // Vérifie que la double authentification est restée activée, avec son secret et ses 10 codes de secours.
    private void AssertStillActivated()
    {
        Assert.True(Account.IsTwoFactorEnabled);
        Assert.NotNull(Account.EncryptedTotpSecret);
        Assert.Equal(10, services.TwoFactorRepository.SavedBackupCodes.Count);
        Assert.Null(link.UsedAt);
        Assert.Empty(services.EmailRequests.SavedRequests);
    }

    // Le bon mot de passe désactive la double authentification, efface le secret et les codes de secours, use le lien, ferme
    // toutes les sessions du compte et seulement elles, note la désactivation et envoie l'alerte.
    [Fact]
    public async Task RightPasswordDeactivatesClosesEverySessionAndSendsTheAlert()
    {
        var otherAccount = services.AddAccountWithoutTwoFactor("autre@exemple.test");
        services.SessionRepository.SavedSessions.Add(new UserSession { CreatedAt = Now, Id = "session-1", LastActivityAt = Now, UserId = Account.Id });
        services.SessionRepository.SavedSessions.Add(new UserSession { CreatedAt = Now, Id = "session-2", LastActivityAt = Now, UserId = Account.Id });
        services.SessionRepository.SavedSessions.Add(new UserSession { CreatedAt = Now, Id = "session-autre", LastActivityAt = Now, UserId = otherAccount.Id });

        var result = await DeactivateAsync(AccountPassword);

        Assert.IsType<TwoFactorSupportLinkResult.Deactivated>(result);
        Assert.False(Account.IsTwoFactorEnabled);
        Assert.Null(Account.EncryptedTotpSecret);
        Assert.Empty(services.TwoFactorRepository.SavedBackupCodes);
        Assert.Equal((Now, (short)1), (link.UsedAt, link.PasswordTryCount));
        Assert.Equal("session-autre", Assert.Single(services.SessionRepository.SavedSessions).Id);
        var success = Assert.Single(services.SecurityEventRepository.SavedSecurityEvents);
        Assert.Equal(
            (SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success, (Guid?)Account.Id, IpAddress),
            (success.Type, success.Result, success.UserId, success.IpAddress));
        var alert = Assert.Single(services.EmailRequests.SavedRequests);
        Assert.Equal((EmailRequestType.TwoFactorDeactivation, (Guid?)Account.Id), (alert.Type, alert.UserId));
    }

    // Le mot de passe est comparé sous sa forme normalisée (NFC), comme à l'inscription : des lettres accentuées tapées en deux
    // caractères, la lettre puis l'accent, donnent le même mot de passe.
    [Fact]
    public async Task PasswordIsComparedInItsNormalizedForm()
    {
        var composed = "Mot-De-Passe-" + (char)0x00C9 + "t" + (char)0x00E9 + "-1";
        var decomposed = "Mot-De-Passe-E" + (char)0x0301 + "te" + (char)0x0301 + "-1";
        Account.ReplacePasswordHash(FakePasswordHasher.HashPrefix + composed);

        var result = await DeactivateAsync(decomposed);

        Assert.IsType<TwoFactorSupportLinkResult.Deactivated>(result);
    }

    // Un mauvais mot de passe compte un essai, est noté au journal, et ne change rien d'autre.
    [Fact]
    public async Task WrongPasswordCountsATryAndChangesNothing()
    {
        var result = await DeactivateAsync(WrongPassword);

        Assert.IsType<TwoFactorSupportLinkResult.WrongPassword>(result);
        Assert.Equal(1, link.PasswordTryCount);
        AssertStillActivated();
        Assert.Equal(1, services.CountSecurityEvents(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Failure));
    }

    // Le 5e mauvais mot de passe annule le lien : même le bon mot de passe ne passe plus, et n'est même plus vérifié.
    [Fact]
    public async Task FifthWrongPasswordCancelsTheLink()
    {
        for (var tryNumber = 1; tryNumber <= 4; tryNumber++)
        {
            Assert.IsType<TwoFactorSupportLinkResult.WrongPassword>(await DeactivateAsync(WrongPassword));
        }

        Assert.IsType<TwoFactorSupportLinkResult.LinkCancelled>(await DeactivateAsync(WrongPassword));
        var verificationsBefore = services.PasswordHasher.VerifiedPasswordCount;

        Assert.IsType<TwoFactorSupportLinkResult.LinkNoLongerValid>(await DeactivateAsync(AccountPassword));

        Assert.Equal(verificationsBefore, services.PasswordHasher.VerifiedPasswordCount);
        Assert.Equal(5, link.PasswordTryCount);
        AssertStillActivated();
        Assert.Equal(5, services.CountSecurityEvents(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Failure));
    }

    // Le bon mot de passe au 5e essai marche encore : le lien n'a plus d'essai, mais il n'a pas encore servi.
    [Fact]
    public async Task RightPasswordOnTheFifthTryStillWorks()
    {
        for (var tryNumber = 1; tryNumber <= 4; tryNumber++)
        {
            await DeactivateAsync(WrongPassword);
        }

        var result = await DeactivateAsync(AccountPassword);

        Assert.IsType<TwoFactorSupportLinkResult.Deactivated>(result);
        Assert.False(Account.IsTwoFactorEnabled);
        Assert.Equal(5, link.PasswordTryCount);
    }

    // Un mot de passe changé ou réinitialisé il y a moins de 7 jours arrête le lien, sans compter d'essai ni vérifier le mot
    // de passe : c'est le cas d'un voleur de la boîte mail.
    [Fact]
    public async Task RecentPasswordChangeStopsTheLinkWithoutATry()
    {
        services.SecurityEventRepository.SavedSecurityEvents.Add(new SecurityEvent
        {
            Type = SecurityEventType.PasswordChange,
            Result = SecurityEventResult.Success,
            OccurredAt = Now.AddDays(-2),
            UserId = Account.Id
        });

        var result = await DeactivateAsync(AccountPassword);

        Assert.IsType<TwoFactorSupportLinkResult.PasswordChangedRecently>(result);
        Assert.Equal(0, link.PasswordTryCount);
        Assert.Equal(0, services.PasswordHasher.VerifiedPasswordCount);
        AssertStillActivated();
    }

    // Un lien expiré, déjà utilisé, inconnu, d'un autre type, ou dont la double authentification est déjà désactivée,
    // est refusé sans compter d'essai.
    [Theory]
    [InlineData("expire")]
    [InlineData("utilise")]
    [InlineData("inconnu")]
    [InlineData("autre-type")]
    [InlineData("deja-desactivee")]
    public async Task LinkThatNoLongerWorksIsRefusedWithoutATry(string situation)
    {
        var token = linkToken;

        switch (situation)
        {
            case "expire":
                services.Clock.Advance(EmailTokenValidityDurations.TwoFactorDeactivationLink);
                break;
            case "utilise":
                link.MarkAsUsed(Now);
                break;
            case "inconnu":
                token = services.TokenGenerator.GenerateToken().Token;
                break;
            case "autre-type":
                var recovery = services.TokenGenerator.GenerateToken();
                services.AccountRepository.SavedEmailTokens.Add(new EmailToken
                {
                    Type = EmailTokenType.PasswordRecovery,
                    TokenHash = recovery.Hash,
                    ExpiresAt = Now.AddMinutes(30),
                    UserId = Account.Id
                });
                token = recovery.Token;
                break;
            default:
                Account.DeactivateTwoFactor();
                break;
        }

        var result = await DeactivateAsync(AccountPassword, token);

        Assert.IsType<TwoFactorSupportLinkResult.LinkNoLongerValid>(result);
        Assert.Equal(0, link.PasswordTryCount);
        Assert.Equal(0, services.PasswordHasher.VerifiedPasswordCount);
        Assert.Empty(services.EmailRequests.SavedRequests);
    }

    // Des essais d'autres requêtes comptés au même instant passent avant celui-ci : le lien relu compte après eux.
    [Fact]
    public async Task SimultaneousTriesAreCountedOneAfterTheOther()
    {
        services.AccountRepository.SupportLinkTryConflicts = 2;

        var result = await DeactivateAsync(WrongPassword);

        Assert.IsType<TwoFactorSupportLinkResult.WrongPassword>(result);
        Assert.Equal(3, link.PasswordTryCount);
    }

    // Si l'autre requête a pris le 5e essai, celui-ci n'est plus compté ni vérifié : le lien est déjà sans essai.
    [Fact]
    public async Task TryAfterTheFifthTakenByAnotherRequestIsRefused()
    {
        for (var tryNumber = 1; tryNumber <= 4; tryNumber++)
        {
            link.CountPasswordTry();
        }

        services.AccountRepository.SupportLinkTryConflicts = 1;

        var result = await DeactivateAsync(AccountPassword);

        Assert.IsType<TwoFactorSupportLinkResult.LinkNoLongerValid>(result);
        Assert.Equal(5, link.PasswordTryCount);
        Assert.Equal(0, services.PasswordHasher.VerifiedPasswordCount);
        AssertStillActivated();
    }

    // Ce qui change entre l'essai et la désactivation arrête tout : un nouveau mot de passe, une double authentification déjà
    // désactivée, le lien utilisé par une autre requête, ou un changement de mot de passe noté au journal.
    [Theory]
    [InlineData("mot-de-passe-change")]
    [InlineData("double-auth-desactivee")]
    [InlineData("lien-utilise")]
    [InlineData("changement-note")]
    public async Task ChangeBetweenTheTryAndTheDeactivationStopsIt(string change)
    {
        services.PasswordHasher.OnPasswordVerified = () =>
        {
            switch (change)
            {
                case "mot-de-passe-change":
                    Account.ReplacePasswordHash("empreinte-du-nouveau-mot-de-passe");
                    break;
                case "double-auth-desactivee":
                    Account.DeactivateTwoFactor();
                    break;
                case "lien-utilise":
                    link.MarkAsUsed(Now);
                    break;
                default:
                    services.SecurityEventRepository.SavedSecurityEvents.Add(new SecurityEvent
                    {
                        Type = SecurityEventType.PasswordChange,
                        Result = SecurityEventResult.Success,
                        OccurredAt = Now,
                        UserId = Account.Id
                    });
                    break;
            }
        };

        var result = await DeactivateAsync(AccountPassword);

        Assert.IsType<TwoFactorSupportLinkResult.LinkNoLongerValid>(result);
        Assert.Empty(services.EmailRequests.SavedRequests);
        Assert.Equal(0, services.CountSecurityEvents(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success));
    }
}
