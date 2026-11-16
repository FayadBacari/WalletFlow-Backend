namespace WalletFlow.Api.Contracts;

// Une demande de support dans la liste de l'utilisateur : sa date, son état (« sent », « inProgress » ou « resolved »),
// et le nombre de réponses reçues. Le message se lit avec la demande elle-même.
public record SupportRequestItemResponse(Guid Id, DateTime SentAt, string Status, int ResponseCount);

// Les demandes de l'utilisateur, la plus récente d'abord.
public record SupportRequestListResponse(List<SupportRequestItemResponse> Items);

// Une demande de l'utilisateur, avec son message et les réponses du support, de la plus ancienne à la plus récente.
// Le message et les réponses sont du contenu non fiable : le site les affiche toujours comme du texte, jamais comme du HTML,
// en gardant leurs retours à la ligne (SS §5).
public record SupportRequestDetailsResponse(
    Guid Id,
    DateTime SentAt,
    string Status,
    string Message,
    List<SupportAnswerResponse> Responses);

// Une réponse du support et sa date.
public record SupportAnswerResponse(string Content, DateTime SentAt);
