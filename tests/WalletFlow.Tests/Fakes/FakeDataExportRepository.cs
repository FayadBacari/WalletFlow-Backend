using WalletFlow.Application.DataExport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Faux dépôt du téléchargement des données : rend les données gardées pour chaque compte, et note chaque lecture.
// La vraie lecture, compte par compte et au même instant, est vérifiée sur la vraie base (DataExportRepositoryTests).
public class FakeDataExportRepository : IDataExportRepository
{
    public Dictionary<Guid, AccountData> SavedData { get; } = new Dictionary<Guid, AccountData>();
    public List<Guid> ReadUserIds { get; } = new List<Guid>();

    // Crée des données à rendre pour ce compte : son adresse, et une catégorie pour les reconnaître.
    public AccountData SaveDataFor(Guid userId, string email)
    {
        var data = new AccountData(
            new ExportedProfile(email, null, null, false, NotificationChannel.Email),
            new List<ExportedBankConnection>(),
            new List<ExportedBankAccount>(),
            new List<ExportedTransaction>(),
            new List<ExportedCategory> { new ExportedCategory(Guid.NewGuid(), "Catégorie de " + email, null, false) },
            new List<ExportedCategorizationRule>(),
            new List<ExportedBag>(),
            new List<ExportedBagMovement>(),
            new List<ExportedSuggestion>(),
            new List<ExportedAlertSetting>(),
            new List<ExportedAlert>(),
            new List<ExportedSupportRequest>());
        SavedData[userId] = data;

        return data;
    }

    public Task<AccountData?> ReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        ReadUserIds.Add(userId);

        return Task.FromResult(SavedData.GetValueOrDefault(userId));
    }
}
