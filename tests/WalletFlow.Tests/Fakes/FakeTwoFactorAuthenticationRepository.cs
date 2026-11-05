using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les codes de secours en mémoire.
public class FakeTwoFactorAuthenticationRepository : ITwoFactorAuthenticationRepository
{
    public const string TechnicalDetailOfTheSimulatedTwoFactorFailure = "detail-technique-de-la-panne-de-la-double-authentification";

    private readonly FakeUserAccountRepository repositoryThatKeepsTheAccountsInMemory;
    private readonly FakeSecurityEventRepository repositoryThatKeepsTheSecurityEventsInMemory;

    public List<BackupCode> SavedBackupCodes { get; } = new List<BackupCode>();

    // Simule d'autres requêtes qui changent le compte juste avant l'enregistrement d'une activation.
    public int NumberOfConflictsBeforeTheActivationIsSaved { get; set; } = 0;

    // Simule d'autres requêtes qui changent le compte juste avant l'enregistrement d'une désactivation.
    public int NumberOfConflictsBeforeTheDeactivationIsSaved { get; set; } = 0;

    // Simule une panne de la base au moment d'enregistrer une activation ou une désactivation.
    public bool DatabaseIsDownWhileSavingTheSettings { get; set; } = false;

    // Simule une panne de la base au moment de lire les codes de secours.
    public bool DatabaseIsDownWhileReadingTheBackupCodes { get; set; } = false;

    // Reçoit les faux comptes et le faux journal, où l'activation et la désactivation écrivent aussi.
    public FakeTwoFactorAuthenticationRepository(FakeUserAccountRepository repositoryThatKeepsTheAccountsInMemory, FakeSecurityEventRepository repositoryThatKeepsTheSecurityEventsInMemory)
    {
        this.repositoryThatKeepsTheAccountsInMemory = repositoryThatKeepsTheAccountsInMemory;
        this.repositoryThatKeepsTheSecurityEventsInMemory = repositoryThatKeepsTheSecurityEventsInMemory;
    }

    // Cherche en mémoire les codes de secours de ce compte qui n'ont pas servi.
    public Task<List<BackupCode>> FindUnusedBackupCodesOfThisAccountAsync(Guid identifierOfTheAccountOwningTheCodes)
    {
        if (DatabaseIsDownWhileReadingTheBackupCodes)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedTwoFactorFailure);
        }

        var unusedBackupCodesOfTheAccount = new List<BackupCode>();

        foreach (var backupCodeAlreadyKeptInThisMemoryList in SavedBackupCodes)
        {
            if (backupCodeAlreadyKeptInThisMemoryList.UserId == identifierOfTheAccountOwningTheCodes && backupCodeAlreadyKeptInThisMemoryList.UsedAt == null)
            {
                unusedBackupCodesOfTheAccount.Add(backupCodeAlreadyKeptInThisMemoryList);
            }
        }

        return Task.FromResult(unusedBackupCodesOfTheAccount);
    }

    // Cherche en mémoire un code de secours par son identifiant.
    public Task<BackupCode?> FindBackupCodeByItsIdentifierAsync(Guid identifierOfTheBackupCodeToFind)
    {
        foreach (var backupCodeAlreadyKeptInThisMemoryList in SavedBackupCodes)
        {
            if (backupCodeAlreadyKeptInThisMemoryList.Id == identifierOfTheBackupCodeToFind)
            {
                return Task.FromResult<BackupCode?>(backupCodeAlreadyKeptInThisMemoryList);
            }
        }

        return Task.FromResult<BackupCode?>(null);
    }

    // Garde tout en mémoire, sauf si un conflit est simulé : rien n'est alors gardé, comme avec la vraie transaction.
    public Task<bool> TryToSaveTwoFactorActivationOfThisAccountAsync(
        User accountBeingActivatedByTheFirstCode,
        string secretOfThePhoneEncryptedForTheDatabase,
        List<BackupCode> newBackupCodesOfTheAccount,
        SecurityEvent securityEventOfTheTwoFactorActivation)
    {
        if (DatabaseIsDownWhileSavingTheSettings)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedTwoFactorFailure);
        }

        if (NumberOfConflictsBeforeTheActivationIsSaved > 0)
        {
            NumberOfConflictsBeforeTheActivationIsSaved = NumberOfConflictsBeforeTheActivationIsSaved - 1;
            return Task.FromResult(false);
        }

        accountBeingActivatedByTheFirstCode.IsTwoFactorEnabled = true;
        accountBeingActivatedByTheFirstCode.EncryptedTotpSecret = secretOfThePhoneEncryptedForTheDatabase;
        SavedBackupCodes.AddRange(newBackupCodesOfTheAccount);
        repositoryThatKeepsTheSecurityEventsInMemory.SavedSecurityEvents.Add(securityEventOfTheTwoFactorActivation);

        return Task.FromResult(true);
    }

    // Efface le secret et tous les codes en mémoire, sauf si un conflit est simulé.
    public Task<bool> TryToSaveTwoFactorDeactivationOfThisAccountAsync(User accountBeingDeactivatedByItsOwner, SecurityEvent securityEventOfTheTwoFactorDeactivation)
    {
        if (DatabaseIsDownWhileSavingTheSettings)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedTwoFactorFailure);
        }

        if (NumberOfConflictsBeforeTheDeactivationIsSaved > 0)
        {
            NumberOfConflictsBeforeTheDeactivationIsSaved = NumberOfConflictsBeforeTheDeactivationIsSaved - 1;
            return Task.FromResult(false);
        }

        accountBeingDeactivatedByItsOwner.IsTwoFactorEnabled = false;
        accountBeingDeactivatedByItsOwner.EncryptedTotpSecret = null;
        SavedBackupCodes.RemoveAll(backupCodeAlreadyKeptInThisMemoryList => backupCodeAlreadyKeptInThisMemoryList.UserId == accountBeingDeactivatedByItsOwner.Id);
        repositoryThatKeepsTheSecurityEventsInMemory.SavedSecurityEvents.Add(securityEventOfTheTwoFactorDeactivation);

        return Task.FromResult(true);
    }
}
