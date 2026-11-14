namespace WalletFlow.Application.Persistence;

// L'unité de travail de la requête en cours. Implémentée dans la couche Infrastructure.
// Le contrat est le suivant, et il est le même pour tous les dépôts :
// - un objet lu par un dépôt est suivi : les changements faits par ses propres règles seront enregistrés ;
// - un objet déjà lu dans la requête n'est pas relu : le dépôt rend celui qu'il suit, avec les valeurs de sa première lecture.
//   C'est l'enregistrement qui vérifie que personne ne l'a changé entre-temps ;
// - une méthode « Prepare… » d'un dépôt n'écrit rien : elle prépare un ajout ou un effacement ;
// - rien de tout cela ne part en base avant l'appel de cette unité de travail, qui enregistre tout en une seule transaction SQL ;
// - les rares écritures immédiates (journal d'un refus, remplacement d'un lien, suppression d'une catégorie ou d'une connexion
//   bancaire) refusent de partir tant que des changements attendent : elles n'emportent donc jamais rien d'autre qu'elles-mêmes.
public interface IDatabaseUnitOfWork
{
    // Enregistre tout ce qui a été changé ou préparé, ou rien du tout.
    // Renvoie faux si une autre requête vient de modifier ou d'effacer les mêmes lignes, de prendre l'adresse e-mail
    // ou le nom de catégorie que ces changements donnent, de supprimer la connexion bancaire à laquelle ils ajoutent un compte
    // ou le sac auquel ils ajoutent un mouvement, de supprimer un sac relié à celui qu'ils suppriment,
    // ou si PostgreSQL arrête l'enregistrement pour débloquer un interblocage :
    // tout est alors oublié, et il faut relire les objets avant de recommencer.
    Task<bool> TrySaveChangesAsync();
}
