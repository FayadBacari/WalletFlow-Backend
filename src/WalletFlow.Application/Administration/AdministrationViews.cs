using WalletFlow.Application.Support;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Administration;

// Un compte tel que l'administrateur le voit : son adresse, son état, et si sa double authentification est activée (SS §8).
// Rien d'autre : ni nom, ni banque, ni argent.
public record AdminAccountView(Guid Id, string Email, AccountState State, bool IsTwoFactorEnabled)
{
    // Donne la vue de ce compte à ce moment.
    public static AdminAccountView Create(User account, DateTime now)
    {
        return new AdminAccountView(account.Id, account.Email, AccountStateRules.FindState(account, now), account.IsTwoFactorEnabled);
    }
}

// Une demande de support dans la liste de l'administrateur : l'adresse où répondre est celle du compte, ou celle du visiteur.
// Le compte est vide pour un visiteur.
public record AdminSupportRequestItem(
    Guid Id,
    DateTime SentAt,
    SupportRequestStatus Status,
    string ReplyAddress,
    Guid? AccountId,
    int ResponseCount);

// Une demande de support avec son message et ses réponses, de la plus ancienne à la plus récente. Le message est du contenu
// non fiable : il est toujours affiché comme du texte (SS §8).
public record AdminSupportRequestDetails(
    Guid Id,
    DateTime SentAt,
    SupportRequestStatus Status,
    string ReplyAddress,
    Guid? AccountId,
    string Message,
    List<SupportResponseView> Responses);

// Les statistiques globales et anonymes (GR Admin) : les comptes par état, les connexions bancaires actives, et les demandes
// de support par état. Aucun chiffre ne porte sur l'argent.
public record AdministrationStatistics(
    int NotConfirmedAccounts,
    int ActiveAccounts,
    int LockedAccounts,
    int AccountsBeingDeleted,
    int ActiveBankConnections,
    int SentRequests,
    int RequestsInProgress,
    int ResolvedRequests);
