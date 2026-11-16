namespace WalletFlow.Api.Contracts;

// Données envoyées par l'administrateur qui cherche un compte : son adresse exacte (GR Admin). Seul ce champ est lu (SS §4).
public class AdminAccountSearchRequest
{
    public string? Email { get; set; }
}

// Données envoyées par l'administrateur qui répond à une demande de support (GR Admin). Seul ce champ est lu (SS §4).
public class AdminAnswerRequest
{
    public string? Content { get; set; }
}

// Un compte tel que l'administrateur le voit : son adresse, son état (« notConfirmed », « active », « locked » ou
// « beingDeleted ») et si sa double authentification est activée (SS §8). Rien d'autre.
public record AdminAccountResponse(Guid Id, string Email, string State, bool IsTwoFactorEnabled);

// Le résultat d'une recherche par adresse exacte : le compte, ou null si aucun compte n'utilise cette adresse.
public record AdminAccountSearchResponse(AdminAccountResponse? Account);

// Une demande de support dans la liste de l'administrateur : l'adresse où répondre est celle du compte, ou celle du visiteur
// quand le compte est null.
public record AdminSupportRequestItemResponse(
    Guid Id,
    DateTime SentAt,
    string Status,
    string ReplyAddress,
    Guid? AccountId,
    int ResponseCount);

// Les demandes dans un état, la plus ancienne d'abord.
public record AdminSupportRequestListResponse(List<AdminSupportRequestItemResponse> Items);

// Une demande avec son message et ses réponses, de la plus ancienne à la plus récente. Le message est du contenu non fiable :
// l'interface d'administration l'affiche toujours comme du texte, jamais comme du HTML (SS §8).
public record AdminSupportRequestDetailsResponse(
    Guid Id,
    DateTime SentAt,
    string Status,
    string ReplyAddress,
    Guid? AccountId,
    string Message,
    List<SupportAnswerResponse> Responses);

// Les comptes par état.
public record AccountsByStateResponse(int NotConfirmed, int Active, int Locked, int BeingDeleted);

// Les demandes de support par état.
public record SupportRequestsByStatusResponse(int Sent, int InProgress, int Resolved);

// Les statistiques globales et anonymes (GR Admin) : aucun chiffre ne porte sur l'argent.
public record AdminStatisticsResponse(AccountsByStateResponse Accounts, int ActiveBankConnections, SupportRequestsByStatusResponse SupportRequests);
