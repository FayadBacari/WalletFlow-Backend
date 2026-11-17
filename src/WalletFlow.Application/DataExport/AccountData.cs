using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.DataExport;

// Toutes les données d'un compte que l'utilisateur voit dans l'application, lues au même instant de la base (GR Compte, SS §6).
// Aucun secret : ni empreinte de mot de passe, ni secret de double authentification, ni code de secours, ni jeton, ni session,
// ni identifiant chez le prestataire bancaire. Ni le journal de sécurité, qui peut garder l'adresse IP d'un inconnu. Les appareils
// n'y sont pas : l'application n'en montre rien, ils ne gardent que des jetons. Chaque liste a un ordre fixe.
public record AccountData(
    ExportedProfile Profile,
    List<ExportedBankConnection> BankConnections,
    List<ExportedBankAccount> BankAccounts,
    List<ExportedTransaction> Transactions,
    List<ExportedCategory> Categories,
    List<ExportedCategorizationRule> CategorizationRules,
    List<ExportedBag> Bags,
    List<ExportedBagMovement> BagMovements,
    List<ExportedSuggestion> Suggestions,
    List<ExportedAlertSetting> AlertSettings,
    List<ExportedAlert> Alerts,
    List<ExportedSupportRequest> SupportRequests);

// L'adresse, le nom et le prénom, la double authentification activée ou non, et le canal des alertes.
public record ExportedProfile(string Email, string? LastName, string? FirstName, bool IsTwoFactorEnabled, NotificationChannel NotificationChannel);

// Une banque connectée, son état et la fin de son autorisation.
public record ExportedBankConnection(Guid Id, string BankName, BankConnectionStatus Status, DateTime AuthorizationExpiresAt);

// Un compte bancaire, son dernier solde connu et sa date.
public record ExportedBankAccount(Guid Id, Guid BankConnectionId, string Name, int BalanceInCents, DateTime BalanceUpdatedAt);

// Une opération, sa catégorie, et si c'est un transfert entre ses comptes.
public record ExportedTransaction(Guid Id, Guid BankAccountId, DateOnly Date, string Label, int AmountInCents, Guid CategoryId, bool IsInternalTransfer);

// Une catégorie et son budget du mois, s'il y en a un.
public record ExportedCategory(Guid Id, string Name, int? MonthlyBudgetInCents, bool IsOtherCategory);

// Une règle de l'utilisateur : ce libellé va dans cette catégorie.
public record ExportedCategorizationRule(string Label, Guid CategoryId);

// Un sac et son objectif, s'il y en a un.
public record ExportedBag(Guid Id, string Name, int? TargetAmountInCents, DateOnly? TargetDate);

// Un déplacement d'argent. Un sac absent est l'argent disponible, ou un sac supprimé depuis.
public record ExportedBagMovement(Guid Id, DateTime MovedAt, int AmountInCents, Guid? SourceBagId, Guid? DestinationBagId, Guid? SuggestionId);

// Une suggestion d'épargne, son mois et sa réponse.
public record ExportedSuggestion(
    Guid Id,
    DateOnly Month,
    SuggestionRule Rule,
    Guid? CategoryId,
    int MonthlySavingsInCents,
    SuggestionStatus Status,
    DateTime? StatusChangedAt);

// Un type d'alerte, activé ou non.
public record ExportedAlertSetting(AlertKind Kind, bool IsEnabled);

// Une alerte gardée pour ne pas être envoyée deux fois, et ce qu'elle visait.
public record ExportedAlert(
    Guid Id,
    AlertKind Kind,
    DateTime CreatedAt,
    DateOnly? Month,
    short? Threshold,
    Guid? CategoryId,
    Guid? BagId,
    Guid? BankConnectionId);

// Une demande de support et ses réponses, de la plus ancienne à la plus récente.
public record ExportedSupportRequest(Guid Id, DateTime SentAt, SupportRequestStatus Status, string Message, List<ExportedSupportResponse> Responses);

// Une réponse du support.
public record ExportedSupportResponse(DateTime SentAt, string Content);
