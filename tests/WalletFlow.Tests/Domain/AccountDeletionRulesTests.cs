using System.Net;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles de la suppression d'un compte (GR Suppression, DD §9.4 et §11) : 30 jours entre la demande et l'effacement,
// une demande qui ne repart pas à zéro, l'annulation, et les trois e-mails de la file.
public class AccountDeletionRulesTests
{
    private static readonly DateTime RequestedAt = new DateTime(2026, 11, 16, 21, 30, 0, DateTimeKind.Utc);
    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.40");

    // Le compte est effaçable 30 jours pile après la demande, pas une seconde avant.
    [Fact]
    public void AccountIsDueExactlyThirtyDaysAfterTheRequest()
    {
        var account = new User { Email = "a@exemple.test" };
        account.RequestDeletion(RequestedAt);

        Assert.Equal(RequestedAt, account.DeletionRequestedAt);
        Assert.Equal(RequestedAt.AddDays(30), AccountDeletionRules.FindErasureMoment(RequestedAt));
        Assert.False(account.IsDueForErasureAt(RequestedAt.AddDays(30).AddTicks(-1)));
        Assert.True(account.IsDueForErasureAt(RequestedAt.AddDays(30)));
    }

    // Un compte sans demande n'est jamais effaçable, et une demande annulée ne l'est plus.
    [Fact]
    public void AccountWithoutRequestIsNeverDue()
    {
        var account = new User { Email = "a@exemple.test" };
        var cancelled = new User { Email = "b@exemple.test" };
        cancelled.RequestDeletion(RequestedAt);

        cancelled.CancelDeletion();

        Assert.False(account.IsDueForErasureAt(RequestedAt.AddYears(1)));
        Assert.Null(cancelled.DeletionRequestedAt);
        Assert.False(cancelled.IsDueForErasureAt(RequestedAt.AddYears(1)));
    }

    // Une deuxième demande ne repousse pas l'effacement : elle est refusée.
    [Fact]
    public void SecondRequestDoesNotPostponeTheErasure()
    {
        var account = new User { Email = "a@exemple.test" };
        account.RequestDeletion(RequestedAt);

        Assert.Throws<InvalidOperationException>(() => account.RequestDeletion(RequestedAt.AddDays(10)));
        Assert.Equal(RequestedAt, account.DeletionRequestedAt);
    }

    // La demande et son annulation partent à l'adresse du compte ; l'e-mail d'effacement ne vise plus de compte, et garde
    // l'adresse jusqu'à son envoi, sans adresse IP : aucune requête ne le demande.
    [Fact]
    public void DeletionEmailsTargetTheAccountExceptTheErasureOne()
    {
        var userId = Guid.NewGuid();

        var request = EmailRequest.ForDeletionRequest(userId, IpAddress, RequestedAt);
        var cancellation = EmailRequest.ForDeletionCancellation(userId, IpAddress, RequestedAt);
        var erasure = EmailRequest.ForAccountErasure("a@exemple.test", RequestedAt);

        Assert.Equal(
            (EmailRequestType.DeletionRequest, (Guid?)userId, (string?)null, IpAddress, RequestedAt),
            (request.Type, request.UserId, request.EmailAddress, request.IpAddress, request.NextTryAt));
        Assert.Equal(
            (EmailRequestType.DeletionCancellation, (Guid?)userId, (string?)null, IpAddress),
            (cancellation.Type, cancellation.UserId, cancellation.EmailAddress, cancellation.IpAddress));
        Assert.Equal(
            (EmailRequestType.AccountErasure, (Guid?)null, (string?)"a@exemple.test", (IPAddress?)null, RequestedAt),
            (erasure.Type, erasure.UserId, erasure.EmailAddress, erasure.IpAddress, erasure.NextTryAt));
    }
}
