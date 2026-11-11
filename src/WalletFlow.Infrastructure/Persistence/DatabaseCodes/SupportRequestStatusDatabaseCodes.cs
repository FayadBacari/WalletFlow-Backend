using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit l'état d'une demande de support en code de la colonne statut_demande, et inversement.
public static class SupportRequestStatusDatabaseCodes
{
    // Code de la colonne statut_demande pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<SupportRequestStatus> Table =
        new EnumDatabaseCodes<SupportRequestStatus>(
            new Dictionary<SupportRequestStatus, string>
            {
                { SupportRequestStatus.Sent, "envoyee" },
                { SupportRequestStatus.InProgress, "en_cours" },
                { SupportRequestStatus.Resolved, "resolue" },
            });
}
