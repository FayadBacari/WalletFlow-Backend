using System.Net;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles de l'administration (GR Admin, GR Auth, SS §2, DD §4.2, §10 et §11) : l'état d'une demande, une réponse,
// les essais du lien qui désactive la double authentification, la règle des 7 jours, et l'état d'un compte.
public class AdministrationRulesTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.9");

    // --- État d'une demande

    // Une réponse passe la demande « en cours », qu'elle soit envoyée, déjà en cours, ou résolue : elle la rouvre.
    [Theory]
    [InlineData(SupportRequestStatus.Sent)]
    [InlineData(SupportRequestStatus.InProgress)]
    [InlineData(SupportRequestStatus.Resolved)]
    public void ResponsePutsTheRequestInProgress(SupportRequestStatus status)
    {
        var request = new SupportRequest { Message = "Bonjour", Status = status, SentAt = Now };

        request.NoteResponse();

        Assert.Equal(SupportRequestStatus.InProgress, request.Status);
    }

    // L'administrateur marque résolue une demande envoyée ou en cours, avec ou sans réponse ; une demande déjà résolue ne change pas.
    [Theory]
    [InlineData(SupportRequestStatus.Sent)]
    [InlineData(SupportRequestStatus.InProgress)]
    public void RequestIsMarkedAsResolvedOnce(SupportRequestStatus status)
    {
        var request = new SupportRequest { Message = "Bonjour", Status = status, SentAt = Now };

        request.MarkAsResolved();

        Assert.Equal(SupportRequestStatus.Resolved, request.Status);
        Assert.Throws<InvalidOperationException>(() => request.MarkAsResolved());
        Assert.Equal(SupportRequestStatus.Resolved, request.Status);
    }

    // --- Réponse

    // Une réponse garde son texte, sa date et sa demande.
    [Fact]
    public void ResponseKeepsItsTextDateAndRequest()
    {
        var requestId = Guid.NewGuid();

        var response = SupportResponse.Create(requestId, "Voici la marche à suivre.\nBonne journée.", Now);

        Assert.Equal((requestId, "Voici la marche à suivre.\nBonne journée.", Now), (response.SupportRequestId, response.Content, response.SentAt));
    }

    // Une réponse suit la règle du texte d'un message, déjà sous sa forme enregistrée.
    public static TheoryData<string> ContentsThatBreakTheRules()
    {
        return new TheoryData<string> { string.Empty, " Bonjour", "a\r\nb", new string('a', 5001), "a" + (char)0x202E + "b" };
    }

    [Theory]
    [MemberData(nameof(ContentsThatBreakTheRules))]
    public void ResponseRefusesAContentThatBreaksTheRules(string content)
    {
        Assert.Throws<ArgumentException>(() => SupportResponse.Create(Guid.NewGuid(), content, Now));
    }

    // --- Lien du support qui désactive la double authentification

    // Le lien compte ses essais du mot de passe jusqu'à 5 ; le 6e est refusé, et le compte ne bouge plus.
    [Fact]
    public void SupportLinkCountsFiveTriesThenNoMore()
    {
        var link = new EmailToken { Type = EmailTokenType.TwoFactorDeactivation, ExpiresAt = Now.AddHours(24), UserId = Guid.NewGuid() };

        for (var tryNumber = 1; tryNumber <= 5; tryNumber++)
        {
            Assert.True(link.HasPasswordTriesLeft());
            link.CountPasswordTry();
            Assert.Equal(tryNumber, link.PasswordTryCount);
        }

        Assert.False(link.HasPasswordTriesLeft());
        Assert.Throws<InvalidOperationException>(() => link.CountPasswordTry());
        Assert.Equal(5, link.PasswordTryCount);
    }

    // Aucun autre lien ne compte ses essais : la base le refuserait (DD §4.2).
    [Theory]
    [InlineData(EmailTokenType.SignupConfirmation)]
    [InlineData(EmailTokenType.PasswordRecovery)]
    [InlineData(EmailTokenType.AccountUnlock)]
    [InlineData(EmailTokenType.EmailChange)]
    public void OtherLinksNeverCountTries(EmailTokenType type)
    {
        var link = new EmailToken { Type = type, ExpiresAt = Now.AddHours(1), UserId = Guid.NewGuid() };

        Assert.Throws<InvalidOperationException>(() => link.CountPasswordTry());
        Assert.Equal(0, link.PasswordTryCount);
    }

    // Le mot de passe doit avoir au moins 7 jours, à la seconde près. Sans changement connu, c'est celui de l'inscription.
    [Theory]
    [InlineData(null, true)]
    [InlineData(-7 * 24 * 60, true)]
    [InlineData(-7 * 24 * 60 + 1, false)]
    [InlineData(-60, false)]
    [InlineData(-30 * 24 * 60, true)]
    public void PasswordMustBeAtLeastSevenDaysOld(int? minutesSinceChange, bool isOldEnough)
    {
        var lastChange = minutesSinceChange is int minutes ? Now.AddMinutes(minutes) : (DateTime?)null;

        Assert.Equal(isOldEnough, TwoFactorAuthenticationRules.IsPasswordOldEnoughForSupportLink(lastChange, Now));
    }

    // --- État d'un compte (DD §11)

    // La demande de suppression passe avant tout, puis l'adresse non confirmée, puis le blocage en cours.
    [Fact]
    public void AccountStateFollowsItsOrder()
    {
        var beingDeleted = new User { Email = "a@exemple.test", IsEmailConfirmed = true, DeletionRequestedAt = Now, LockoutEndsAt = Now.AddHours(1) };
        var notConfirmed = new User { Email = "b@exemple.test", IsEmailConfirmed = false, LockoutEndsAt = Now.AddHours(1) };
        var locked = new User { Email = "c@exemple.test", IsEmailConfirmed = true, LockoutEndsAt = Now.AddMinutes(1) };
        var lockoutJustOver = new User { Email = "d@exemple.test", IsEmailConfirmed = true, LockoutEndsAt = Now };
        var active = new User { Email = "e@exemple.test", IsEmailConfirmed = true };

        Assert.Equal(AccountState.BeingDeleted, AccountStateRules.FindState(beingDeleted, Now));
        Assert.Equal(AccountState.NotConfirmed, AccountStateRules.FindState(notConfirmed, Now));
        Assert.Equal(AccountState.Locked, AccountStateRules.FindState(locked, Now));
        Assert.Equal(AccountState.Active, AccountStateRules.FindState(lockoutJustOver, Now));
        Assert.Equal(AccountState.Active, AccountStateRules.FindState(active, Now));
    }

    // --- File d'e-mails

    // L'e-mail d'une réponse ne vise aucun compte et ne garde aucune adresse : il est lié à sa réponse, à sa date.
    [Fact]
    public void ResponseEmailIsLinkedToItsResponseOnly()
    {
        var response = SupportResponse.Create(Guid.NewGuid(), "Réponse", Now);

        var request = EmailRequest.ForSupportResponse(response, IpAddress);

        Assert.Equal(
            (EmailRequestType.SupportResponse, (Guid?)response.Id, (Guid?)null, (string?)null, (Guid?)null, IpAddress, Now, Now),
            (request.Type, request.SupportResponseId, request.UserId, request.EmailAddress, request.AlertId, request.IpAddress, request.RequestedAt, request.NextTryAt));
    }

    // Le lien du support vise le compte, sans adresse : il part à l'adresse du compte au moment de l'envoi.
    [Fact]
    public void SupportLinkEmailTargetsTheAccount()
    {
        var accountId = Guid.NewGuid();

        var request = EmailRequest.ForTwoFactorDeactivationLink(accountId, IpAddress, Now);

        Assert.Equal(
            (EmailRequestType.TwoFactorDeactivationLink, (Guid?)accountId, (string?)null, (Guid?)null),
            (request.Type, request.UserId, request.EmailAddress, request.SupportResponseId));
    }
}
