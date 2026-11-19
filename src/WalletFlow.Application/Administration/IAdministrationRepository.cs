using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Administration;

// Accès de l'administrateur aux demandes de support et aux statistiques (GR Admin). Implémenté dans la couche Infrastructure.
// Ces lectures ne filtrent sur aucun utilisateur : seul un administrateur y arrive (voir AdministratorAccessService).
// Elles ne lisent ni compte bancaire, ni opération, ni solde, ni sac (SS §8). Les méthodes « Prepare… » n'écrivent rien :
// voir IDatabaseUnitOfWork.
public interface IAdministrationRepository
{
    // Donne les demandes dans cet état, la plus ancienne d'abord, avec l'adresse où répondre. Rien n'est suivi.
    Task<List<AdminSupportRequestItem>> FindRequestsAsync(SupportRequestStatus status, CancellationToken cancellationToken = default);

    // Donne une demande avec son message, l'adresse où répondre et ses réponses, de la plus ancienne à la plus récente.
    // Renvoie null si elle n'existe pas. Rien n'est suivi.
    Task<AdminSupportRequestDetails?> FindRequestDetailsAsync(Guid requestId, CancellationToken cancellationToken = default);

    // Cherche la demande pour la changer : elle est suivie. Renvoie null si elle n'existe pas.
    Task<SupportRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

    // Prépare l'écriture de la ligne de la demande, même si son état ne change pas : l'enregistrement échoue si une autre requête
    // l'a écrite depuis sa lecture. Une réponse et une demande résolue au même instant passent ainsi l'une après l'autre.
    void PrepareRequestCheck(SupportRequest request);

    // Prépare l'entrée d'une réponse.
    void PrepareResponse(SupportResponse response);

    // Compte, à ce moment, les comptes par état, les connexions bancaires actives et les demandes par état. Aucun chiffre
    // ne porte sur l'argent (GR Admin).
    Task<AdministrationStatistics> CountStatisticsAsync(DateTime now, CancellationToken cancellationToken = default);
}
