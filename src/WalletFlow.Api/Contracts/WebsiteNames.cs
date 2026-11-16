using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Api.Contracts;

// Les noms que le site reçoit et envoie pour l'état d'une demande de support et l'état d'un compte. Une seule liste pour toutes
// les routes : l'utilisateur et l'administrateur voient les mêmes noms.
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

    // Lit l'état d'une demande d'après son nom exact, majuscules comprises. Renvoie faux pour un nom inconnu ou absent.
    public static bool TryReadSupportRequestStatus(string? name, out SupportRequestStatus status)
    {
        foreach (var pair in SupportRequestStatuses)
        {
            if (string.Equals(pair.Value, name, StringComparison.Ordinal))
            {
                status = pair.Key;
                return true;
            }
        }

        status = default;
        return false;
    }
}
