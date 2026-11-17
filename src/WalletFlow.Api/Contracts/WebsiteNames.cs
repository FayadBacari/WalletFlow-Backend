using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Api.Contracts;

// Les noms que le site reçoit et envoie pour chaque valeur d'une liste : l'état d'une demande de support, d'un compte, d'une
// connexion bancaire ou d'une suggestion, la règle d'une suggestion, le type d'une alerte et le canal des alertes. Une seule
// liste pour toutes les routes et pour le fichier des données : chaque valeur porte partout le même nom.
public static class WebsiteNames
{
    private static readonly Dictionary<SupportRequestStatus, string> SupportRequestStatuses = new Dictionary<SupportRequestStatus, string>
    {
        { SupportRequestStatus.Sent, "sent" },
        { SupportRequestStatus.InProgress, "inProgress" },
        { SupportRequestStatus.Resolved, "resolved" },
    };

    private static readonly Dictionary<AccountState, string> AccountStates = new Dictionary<AccountState, string>
    {
        { AccountState.NotConfirmed, "notConfirmed" },
        { AccountState.Active, "active" },
        { AccountState.Locked, "locked" },
        { AccountState.BeingDeleted, "beingDeleted" },
    };

    private static readonly Dictionary<BankConnectionStatus, string> BankConnectionStatuses = new Dictionary<BankConnectionStatus, string>
    {
        { BankConnectionStatus.Active, "active" },
        { BankConnectionStatus.InError, "inError" },
        { BankConnectionStatus.Expired, "expired" },
    };

    private static readonly Dictionary<SuggestionRule, string> SuggestionRules = new Dictionary<SuggestionRule, string>
    {
        { SuggestionRule.CategoryOverrun, "categoryOverrun" },
        { SuggestionRule.UnallocatedMoney, "unallocatedMoney" },
        { SuggestionRule.InsufficientSavings, "insufficientSavings" },
    };

    private static readonly Dictionary<SuggestionStatus, string> SuggestionStatuses = new Dictionary<SuggestionStatus, string>
    {
        { SuggestionStatus.Proposed, "proposed" },
        { SuggestionStatus.Accepted, "accepted" },
        { SuggestionStatus.Ignored, "ignored" },
    };

    private static readonly Dictionary<AlertKind, string> AlertKinds = new Dictionary<AlertKind, string>
    {
        { AlertKind.BudgetReached, "budgetReached" },
        { AlertKind.NewSuggestion, "newSuggestion" },
        { AlertKind.BagGoalReached, "bagGoalReached" },
        { AlertKind.BankAuthorizationToRenew, "bankAuthorizationToRenew" },
        { AlertKind.NewDevice, "newDevice" },
        { AlertKind.AccountLocked, "accountLocked" },
        { AlertKind.PasswordChange, "passwordChange" },
        { AlertKind.EmailChange, "emailChange" },
        { AlertKind.TwoFactorDeactivation, "twoFactorDeactivation" },
        { AlertKind.BackupCodeUse, "backupCodeUse" },
    };

    private static readonly Dictionary<NotificationChannel, string> NotificationChannels = new Dictionary<NotificationChannel, string>
    {
        { NotificationChannel.Email, "email" },
        { NotificationChannel.Phone, "phone" },
        { NotificationChannel.EmailAndPhone, "emailAndPhone" },
    };

    // Donne le nom de l'état d'une demande.
    public static string Of(SupportRequestStatus status)
    {
        return SupportRequestStatuses[status];
    }

    // Donne le nom de l'état d'un compte.
    public static string Of(AccountState state)
    {
        return AccountStates[state];
    }

    // Donne le nom de l'état d'une connexion bancaire.
    public static string Of(BankConnectionStatus status)
    {
        return BankConnectionStatuses[status];
    }

    // Donne le nom de la règle d'une suggestion.
    public static string Of(SuggestionRule rule)
    {
        return SuggestionRules[rule];
    }

    // Donne le nom de l'état d'une suggestion.
    public static string Of(SuggestionStatus status)
    {
        return SuggestionStatuses[status];
    }

    // Donne le nom d'un type d'alerte.
    public static string Of(AlertKind kind)
    {
        return AlertKinds[kind];
    }

    // Donne le nom d'un canal d'alerte.
    public static string Of(NotificationChannel channel)
    {
        return NotificationChannels[channel];
    }

    // Lit l'état d'une demande d'après son nom exact, majuscules comprises. Renvoie faux pour un nom inconnu ou absent.
    public static bool TryReadSupportRequestStatus(string? name, out SupportRequestStatus status)
    {
        return TryRead(SupportRequestStatuses, name, out status);
    }

    // Lit un type d'alerte d'après son nom exact, majuscules comprises. Renvoie faux pour un nom inconnu ou absent.
    public static bool TryReadAlertKind(string? name, out AlertKind kind)
    {
        return TryRead(AlertKinds, name, out kind);
    }

    // Lit un canal d'alerte d'après son nom exact, majuscules comprises. Renvoie faux pour un nom inconnu ou absent.
    public static bool TryReadNotificationChannel(string? name, out NotificationChannel channel)
    {
        return TryRead(NotificationChannels, name, out channel);
    }

    // Retrouve la valeur qui porte exactement ce nom. Renvoie faux pour tout autre nom.
    private static bool TryRead<TValue>(Dictionary<TValue, string> names, string? name, out TValue value)
        where TValue : struct, Enum
    {
        foreach (var pair in names)
        {
            if (string.Equals(pair.Value, name, StringComparison.Ordinal))
            {
                value = pair.Key;
                return true;
            }
        }

        value = default;
        return false;
    }
}
