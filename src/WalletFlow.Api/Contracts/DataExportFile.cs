namespace WalletFlow.Api.Contracts;

// Le fichier des données d'un compte (GR Compte, SS §6) : un JSON aux mêmes noms que les autres réponses de l'API. Les montants
// sont en centimes, les dates en UTC, les jours au format AAAA-MM-JJ, et chaque valeur d'une liste porte son nom du site
// (voir WebsiteNames). Les identifiants relient les listes entre elles : une opération à son compte et à sa catégorie, par exemple.
public record DataExportFile(
    DateTime ExportedAt,
    ProfileInFile Profile,
    List<BankConnectionInFile> BankConnections,
    List<BankAccountInFile> BankAccounts,
    List<TransactionInFile> Transactions,
    List<CategoryInFile> Categories,
    List<CategorizationRuleInFile> CategorizationRules,
    List<BagInFile> Bags,
    List<BagMovementInFile> BagMovements,
    List<SuggestionInFile> Suggestions,
    List<AlertSettingInFile> AlertSettings,
    List<AlertInFile> Alerts,
    List<SupportRequestInFile> SupportRequests);

public record ProfileInFile(string Email, string? LastName, string? FirstName, bool IsTwoFactorEnabled, string NotificationChannel);

public record BankConnectionInFile(Guid Id, string BankName, string Status, DateTime AuthorizationExpiresAt);

public record BankAccountInFile(Guid Id, Guid BankConnectionId, string Name, int BalanceInCents, DateTime BalanceUpdatedAt);

public record TransactionInFile(Guid Id, Guid BankAccountId, DateOnly Date, string Label, int AmountInCents, Guid CategoryId, bool IsInternalTransfer);

public record CategoryInFile(Guid Id, string Name, int? MonthlyBudgetInCents, bool IsOtherCategory);

public record CategorizationRuleInFile(string Label, Guid CategoryId);

public record BagInFile(Guid Id, string Name, int? TargetAmountInCents, DateOnly? TargetDate);

public record BagMovementInFile(Guid Id, DateTime MovedAt, int AmountInCents, Guid? SourceBagId, Guid? DestinationBagId, Guid? SuggestionId);

public record SuggestionInFile(Guid Id, DateOnly Month, string Rule, Guid? CategoryId, int MonthlySavingsInCents, string Status, DateTime? StatusChangedAt);

public record AlertSettingInFile(string Type, bool IsEnabled);

public record AlertInFile(
    Guid Id,
    string Type,
    DateTime CreatedAt,
    DateOnly? Month,
    short? Threshold,
    Guid? CategoryId,
    Guid? BagId,
    Guid? BankConnectionId);

public record SupportRequestInFile(Guid Id, DateTime SentAt, string Status, string Message, List<SupportResponseInFile> Responses);

public record SupportResponseInFile(DateTime SentAt, string Content);
