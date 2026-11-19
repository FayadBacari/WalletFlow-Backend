using WalletFlow.Application.Emails;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Tests.Application;

// Vérifie la lecture des champs d'une demande de la file : la valeur quand elle est là, et un arrêt net quand elle manque. La base
// refuse une demande à qui il manque ce que son type demande (DD §9.4) : ce manque ne peut venir que d'une erreur du code.
public class EmailRequestFieldsTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EachFieldIsReadWhenItIsThere()
    {
        var userId = Guid.NewGuid();
        var alertId = Guid.NewGuid();
        var responseId = Guid.NewGuid();
        var request = new EmailRequest
        {
            RequestedAt = Now,
            EmailAddress = "nouvelle@exemple.test",
            UserId = userId,
            AlertId = alertId,
            SupportResponseId = responseId
        };

        Assert.Equal("nouvelle@exemple.test", EmailRequestFields.ReadEmailAddress(request));
        Assert.Equal(userId, EmailRequestFields.ReadUserId(request));
        Assert.Equal(alertId, EmailRequestFields.ReadAlertId(request));
        Assert.Equal(responseId, EmailRequestFields.ReadSupportResponseId(request));
    }

    // Chaque champ absent arrête la préparation de l'e-mail, avec un message qui dit lequel manque.
    [Fact]
    public void MissingFieldStopsThePreparation()
    {
        var empty = new EmailRequest { RequestedAt = Now };

        Assert.Equal("Cette demande d'e-mail n'a pas d'adresse.",
            Assert.Throws<InvalidOperationException>(() => EmailRequestFields.ReadEmailAddress(empty)).Message);
        Assert.Equal("Cette demande d'e-mail ne vise aucun compte.",
            Assert.Throws<InvalidOperationException>(() => EmailRequestFields.ReadUserId(empty)).Message);
        Assert.Equal("Cette demande d'e-mail n'annonce aucune alerte.",
            Assert.Throws<InvalidOperationException>(() => EmailRequestFields.ReadAlertId(empty)).Message);
        Assert.Equal("Cette demande d'e-mail n'envoie aucune réponse du support.",
            Assert.Throws<InvalidOperationException>(() => EmailRequestFields.ReadSupportResponseId(empty)).Message);
    }

    // Un champ n'est jamais pris dans un autre : une demande qui n'a qu'un compte n'a ni adresse, ni alerte, ni réponse.
    [Fact]
    public void FieldIsNeverReadFromAnother()
    {
        var accountOnly = EmailRequest.ForNewDevice(Guid.NewGuid(), null, Now);

        Assert.Throws<InvalidOperationException>(() => EmailRequestFields.ReadEmailAddress(accountOnly));
        Assert.Throws<InvalidOperationException>(() => EmailRequestFields.ReadAlertId(accountOnly));
        Assert.Throws<InvalidOperationException>(() => EmailRequestFields.ReadSupportResponseId(accountOnly));
        Assert.Equal(accountOnly.UserId, EmailRequestFields.ReadUserId(accountOnly));
    }
}
