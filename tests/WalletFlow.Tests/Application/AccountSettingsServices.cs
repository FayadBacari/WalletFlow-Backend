using WalletFlow.Application.AccountDeletion;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.DataExport;
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
            tools.EmailRequests,
            tools.Clock);
    }

    // Crée le service qui change le mot de passe.
    public static PasswordChangeService CreatePasswordChange(TwoFactorServicesWithFakeTools tools)
    {
        return new PasswordChangeService(
            tools.AccountRepository,
            tools.SessionRepository,
            tools.EmailRequests,
            tools.CreateSavingService(),
            tools.CreateSecurityJournal(),
            CreateIdentityChecking(tools),
            tools.PasswordHasher,
            tools.BreachedPasswordChecker,
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
            tools.EmailRequests,
            tools.Clock);
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
            tools.EmailRequests,
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

    // Crée le service qui donne le fichier des données, avec la vraie limite de 3 essais par heure, sur l'horloge des outils.
    public static DataExportService CreateDataExport(TwoFactorServicesWithFakeTools tools, FakeDataExportRepository exportRepository, IDataExportLimiter limiter)
    {
        return new DataExportService(
            tools.AccountRepository,
            exportRepository,
            limiter,
            tools.CreateSavingService(),
            tools.CreateSecurityJournal(),
            CreateIdentityChecking(tools),
            tools.Clock);
    }

    // Crée la révocation des banques d'un compte en suppression, avec le faux prestataire des outils.
    public static DeletedAccountBankRevocation CreateBankRevocation(TwoFactorServicesWithFakeTools tools)
    {
        return new DeletedAccountBankRevocation(
            tools.BankRepository,
            tools.AccountRepository,
            tools.BankProvider,
            tools.CreateSavingService(),
            tools.CreateSecurityJournal());
    }

    // Crée le service qui reçoit les demandes de suppression.
    public static AccountDeletionService CreateAccountDeletion(TwoFactorServicesWithFakeTools tools)
    {
        return new AccountDeletionService(
            tools.AccountRepository,
            tools.SessionRepository,
            tools.EmailRequests,
            tools.CreateSavingService(),
            tools.CreateSecurityJournal(),
            CreateIdentityChecking(tools),
            CreateBankRevocation(tools),
            tools.Clock);
    }

    // Crée le service qui efface les comptes dont la suppression est demandée depuis 30 jours.
    public static AccountErasureService CreateAccountErasure(TwoFactorServicesWithFakeTools tools)
    {
        return new AccountErasureService(tools.AccountRepository, tools.EmailRequests, tools.CreateSavingService(), tools.Clock);
    }
}
