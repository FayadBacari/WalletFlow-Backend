using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Support;

// Accès aux demandes de support et à leurs réponses (GR Admin, DD §10). Implémenté dans la couche Infrastructure.
// Les méthodes « Prepare… » n'écrivent rien : voir IDatabaseUnitOfWork.
public interface ISupportRequestRepository
{
    // Lit la ligne de l'utilisateur, pour contrôler à l'enregistrement que personne ne l'a écrite depuis.
    // Renvoie null si le compte n'existe plus.
    Task<User?> FindUserAsync(Guid userId);

    // Prépare l'écriture de la ligne de l'utilisateur, même si rien n'y change : l'enregistrement échoue si une autre requête
    // l'a écrite depuis sa lecture.
    void PrepareUserCheck(User user);

    // Compte les demandes que l'utilisateur a envoyées depuis ce moment, ce moment compris.
    Task<int> CountSentSinceAsync(Guid userId, DateTime since);

    // Prépare l'entrée d'une nouvelle demande.
    void PrepareRequest(SupportRequest request);

    // Donne les demandes de l'utilisateur, la plus récente d'abord, avec le nombre de réponses de chacune. Rien n'est suivi.
    Task<List<SupportRequestSummary>> FindUserRequestsAsync(Guid userId);

    // Donne une demande de l'utilisateur, avec ses réponses de la plus ancienne à la plus récente. Renvoie null si elle
    // n'existe pas, ou si elle n'est pas à lui : celle d'un autre utilisateur ou d'un visiteur. Rien n'est suivi.
    Task<SupportRequestDetails?> FindUserRequestAsync(Guid userId, Guid requestId);

    // Lit, pour l'e-mail d'une réponse, l'adresse où répondre (celle du compte au moment de l'envoi, ou celle du visiteur),
    // la date de la demande et le texte de la réponse. Renvoie null si la réponse n'existe plus. Rien n'est suivi.
    // Seule la file d'e-mails s'en sert : la réponse est trouvée par son identifiant, sans filtre sur un utilisateur.
    Task<SupportResponseEmailContent?> ReadResponseEmailAsync(Guid responseId);

    // Efface tout de suite les demandes envoyées avant ce moment, avec leurs réponses et les e-mails de ces réponses
    // encore en attente, et renvoie le nombre de demandes effacées.
    Task<int> DeleteSentBeforeAsync(DateTime limit);
}
