using WalletFlow.Application.AccountSettings;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Crée les services du compte et des données personnelles avec les faux outils de TwoFactorServicesWithFakeTools :
// la base, le hachage et l'horloge sont faux ; les codes, le chiffrement et les jetons sont les vrais.
public static class AccountSettingsServices
{
    // Crée le service qui vérifie le mot de passe actuel et le code.
    public static AccountIdentityCheckingService CreateIdentityChecking(TwoFactorServicesWithFakeTools tools)
    {
        return new AccountIdentityCheckingService(
            tools.TwoFactorRepository,
            tools.PasswordHasher,
            tools.CreateCodeCheckingService(),
            tools.Clock);
    }

    // Crée le service qui change le mot de passe.
    public static PasswordChangeService CreatePasswordChange(TwoFactorServicesWithFakeTools tools)
    {
        return new PasswordChangeService(
            tools.AccountRepository,
            tools.SessionRepository,
            tools.CreateSavingService(),
            tools.CreateSecurityJournal(),
            CreateIdentityChecking(tools),
            tools.PasswordHasher,
            tools.TokenGenerator,
            tools.Clock);
    }

    // Crée le service qui reçoit les demandes de changement d'adresse.
    public static EmailChangeRequestService CreateEmailChangeRequest(TwoFactorServicesWithFakeTools tools, FakeEmailChangeRequestLimiter limiter)
    {
        return new EmailChangeRequestService(
            tools.AccountRepository,
            tools.CreateSavingService(),
            tools.CreateSecurityJournal(),
            CreateIdentityChecking(tools),
            limiter,
            tools.EmailQueue);
    }

    // Crée le service qui confirme une nouvelle adresse.
    public static EmailChangeConfirmationService CreateEmailChangeConfirmation(TwoFactorServicesWithFakeTools tools)
    {
        return new EmailChangeConfirmationService(
            tools.AccountRepository,
            tools.CreateSavingService(),
            tools.CreateSecurityJournal(),
            tools.PasswordHasher,
            tools.TokenGenerator,
            tools.EmailQueue,
            tools.Clock);
    }

    // Crée le service qui change le nom et le prénom.
    public static PersonalNameChangeService CreatePersonalNameChange(TwoFactorServicesWithFakeTools tools)
    {
        return new PersonalNameChangeService(tools.AccountRepository, tools.CreateSavingService());
    }

    // Crée le service qui lit le profil.
    public static ProfileService CreateProfile(TwoFactorServicesWithFakeTools tools)
    {
        return new ProfileService(tools.AccountRepository);
    }
}
