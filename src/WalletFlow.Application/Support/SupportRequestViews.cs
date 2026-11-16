using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Support;

// Une demande de l'utilisateur dans sa liste : sa date, son état, et le nombre de réponses qu'elle a reçues (GR Admin).
// Le message n'y est pas : il se lit avec la demande elle-même, pour qu'une longue liste reste légère.
public record SupportRequestSummary(Guid Id, DateTime SentAt, SupportRequestStatus Status, int ResponseCount);

// Une demande de l'utilisateur, avec son message et ses réponses, de la plus ancienne à la plus récente (GR Admin).
public record SupportRequestDetails(
    Guid Id,
    DateTime SentAt,
    SupportRequestStatus Status,
    string Message,
    List<SupportResponseView> Responses);

// Une réponse du support, telle que l'utilisateur la lit.
public record SupportResponseView(string Content, DateTime SentAt);
