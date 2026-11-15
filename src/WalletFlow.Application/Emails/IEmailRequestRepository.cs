using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Emails;

// Accès à la file d'e-mails, gardée en base (SS §9). Implémenté dans la couche Infrastructure.
// Les méthodes « Prepare… » n'écrivent rien : voir IDatabaseUnitOfWork. Une demande préparée n'entre dans la file
// qu'avec les autres changements : un e-mail n'annonce donc jamais un changement qui n'a pas été enregistré.
public interface IEmailRequestRepository
{
    // Prépare l'entrée d'une demande dans la file.
    void PrepareRequest(EmailRequest request);

    // Donne l'identifiant de la demande dont l'heure d'essai est passée depuis le plus longtemps, ou null s'il n'y en a pas.
    Task<Guid?> FindNextDueIdAsync(DateTime now);

    // Cherche une demande par son identifiant. Renvoie null si elle n'est plus dans la file.
    // La demande lue est suivie : ses essais ratés seront enregistrés.
    Task<EmailRequest?> FindAsync(Guid requestId);

    // Prépare la sortie d'une demande de la file.
    void PrepareRemoval(EmailRequest request);
}
