using WalletFlow.Application.Persistence;
using WalletFlow.Application.Support;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie les cas d'utilisation du support (GR Admin, SS §4 à §7) : une demande envoyée par l'utilisateur connecté, avec sa limite
// de 10 sur 24 heures, une demande envoyée par un visiteur, la lecture des demandes, et leur effacement un an après.
public class SupportRequestServicesTests
{
    private readonly User user = new User { Email = "support@exemple.test", PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
    private readonly User otherUser = new User { Email = "autre@exemple.test", PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
    private readonly FakeSupportRequestRepository repository = new FakeSupportRequestRepository();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly FakeDatabaseUnitOfWork unitOfWork;

    // Garde les deux comptes dans la fausse base, reliée à la fausse unité de travail.
    public SupportRequestServicesTests()
    {
        repository.SavedUsers.Add(user);
        repository.SavedUsers.Add(otherUser);
        unitOfWork = new FakeDatabaseUnitOfWork(repository);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Envoie ce message au nom de l'utilisateur des tests.
    private async Task<SupportRequestSendingResult> SendAsync(string? message, Guid? userId = null)
    {
        var service = new SupportRequestSendingService(repository, new DatabaseChangesSavingService(unitOfWork), clock);

        return await service.SendAsync(userId ?? user.Id, message);
    }

    // Envoie ce message au nom d'un visiteur, avec cette adresse de contact.
    private async Task<VisitorSupportRequestResult> SendAsVisitorAsync(string? contactEmail, string? message)
    {
        return await new VisitorSupportRequestService(repository, unitOfWork, clock).SendAsync(contactEmail, message);
    }

    // Garde une demande de cet utilisateur, envoyée à ce moment.
    private SupportRequest AddRequest(Guid userId, DateTime sentAt, string message = "Bonjour")
    {
        var request = SupportRequest.FromUser(userId, message, sentAt);
        repository.SavedRequests.Add(request);

        return request;
    }

    // --- Demande de l'utilisateur connecté

    // Le message est gardé sous sa forme enregistrée, à la date de l'envoi, sans réponse. La ligne du compte est réécrite :
    // c'est elle qui fait passer une à une les demandes simultanées.
    [Fact]
    public async Task UserRequestIsSavedUnderItsStoredForm()
    {
        var result = await SendAsync("  Bonjour,\r\nmon sac a disparu.  ");

        var sent = Assert.IsType<SupportRequestSendingResult.Sent>(result);
        var saved = Assert.Single(repository.SavedRequests);
        Assert.Equal((user.Id, "Bonjour,\nmon sac a disparu.", Now, SupportRequestStatus.Sent, (string?)null),
            (saved.UserId, saved.Message, saved.SentAt, saved.Status, saved.ContactEmail));
        Assert.Equal(
            (saved.Id, Now, SupportRequestStatus.Sent, saved.Message),
            (sent.Request.Id, sent.Request.SentAt, sent.Request.Status, sent.Request.Message));
        Assert.Empty(sent.Request.Responses);
        Assert.Equal(user.Id, Assert.Single(repository.CommittedUserChecks).Id);
    }

    // Un message vide, fait seulement d'espaces, ou qui contient un caractère invisible, est refusé.
    public static TheoryData<string?> UserMessagesThatBreakTheRules()
    {
        return new TheoryData<string?> { (string?)null, "   \r\n  ", "a" + (char)0x202E + "b" };
    }

    [Theory]
    [MemberData(nameof(UserMessagesThatBreakTheRules))]
    public async Task UserMessageThatBreaksTheRulesIsRefused(string? message)
    {
        var result = await SendAsync(message);

        Assert.IsType<SupportRequestSendingResult.MessageBreaksRules>(result);
        Assert.Empty(repository.SavedRequests);
    }

    [Fact]
    public async Task UserMessageOfFiveThousandCharactersIsSavedButNotOneMore()
    {
        Assert.IsType<SupportRequestSendingResult.Sent>(await SendAsync(new string('a', 5000)));
        Assert.IsType<SupportRequestSendingResult.MessageBreaksRules>(await SendAsync(new string('a', 5001)));
        Assert.Single(repository.SavedRequests);
    }

    // SS §6 : 10 demandes sur les dernières 24 heures, pas sur le jour du calendrier. Les demandes d'un autre ne comptent pas.
    [Fact]
    public async Task EleventhRequestWithinTwentyFourHoursIsRefused()
    {
        AddRequest(otherUser.Id, Now);

        for (var number = 1; number <= 10; number++)
        {
            Assert.IsType<SupportRequestSendingResult.Sent>(await SendAsync("Demande " + number));
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.IsType<SupportRequestSendingResult.TooManyRequests>(await SendAsync("Demande 11"));
        Assert.Equal(10, repository.SavedRequests.Count(request => request.UserId == user.Id));
    }

    // La demande la plus ancienne ne compte plus 24 heures après son envoi, à la seconde près.
    [Fact]
    public async Task OldestRequestStopsCountingTwentyFourHoursAfterItWasSent()
    {
        var oldest = AddRequest(user.Id, Now - TimeSpan.FromHours(24));
        for (var number = 1; number <= 9; number++)
        {
            AddRequest(user.Id, Now - TimeSpan.FromHours(1));
        }

        Assert.IsType<SupportRequestSendingResult.TooManyRequests>(await SendAsync("Encore une"));

        clock.Advance(TimeSpan.FromTicks(1));

        Assert.IsType<SupportRequestSendingResult.Sent>(await SendAsync("Encore une"));
        Assert.Contains(oldest, repository.SavedRequests);
    }

    // Une autre requête enregistre une demande juste après le compte : le compte lu est déjà ancien. Le contrôle de la ligne
    // de l'utilisateur fait échouer l'enregistrement, et le passage suivant recompte : la 11e demande est refusée.
    [Fact]
    public async Task RequestSavedByAnotherRequestDuringTheCountIsCountedAgain()
    {
        for (var number = 1; number <= 9; number++)
        {
            AddRequest(user.Id, Now);
        }

        repository.RequestSavedAfterCount = SupportRequest.FromUser(user.Id, "Envoyée en même temps", Now);

        var result = await SendAsync("La onzième");

        Assert.IsType<SupportRequestSendingResult.TooManyRequests>(result);
        Assert.Equal(10, repository.SavedRequests.Count);
        Assert.DoesNotContain(repository.SavedRequests, request => request.Message == "La onzième");
    }

    // Les conflits sont rejoués : la demande finit par passer, une seule fois.
    [Fact]
    public async Task ConflictsAreReplayedAndTheRequestIsSavedOnce()
    {
        repository.Conflicts = 3;

        var result = await SendAsync("Bonjour");

        Assert.IsType<SupportRequestSendingResult.Sent>(result);
        Assert.Single(repository.SavedRequests);
    }

    // Au bout de 10 passages en conflit, rien n'est enregistré.
    [Fact]
    public async Task EndlessConflictsSaveNothing()
    {
        repository.Conflicts = 10;

        var result = await SendAsync("Bonjour");

        Assert.IsType<SupportRequestSendingResult.NotSaved>(result);
        Assert.Empty(repository.SavedRequests);
    }

    // Un compte effacé entre-temps n'envoie rien.
    [Fact]
    public async Task ErasedAccountSendsNothing()
    {
        var result = await SendAsync("Bonjour", Guid.NewGuid());

        Assert.IsType<SupportRequestSendingResult.NotSaved>(result);
        Assert.Empty(repository.SavedRequests);
    }

    // Une panne de la base n'est pas cachée, et rien n'est enregistré.
    [Fact]
    public async Task DatabaseFailureIsNotHidden()
    {
        repository.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync("Bonjour"));

        repository.IsDown = false;
        Assert.Empty(repository.SavedRequests);
    }

    // --- Demande d'un visiteur

    // L'adresse est gardée en minuscules, le message sous sa forme enregistrée, sans utilisateur.
    [Fact]
    public async Task VisitorRequestIsSavedWithItsContactAddress()
    {
        var result = await SendAsVisitorAsync("  Visiteur@Exemple.TEST ", " Mon compte est bloqué. ");

        Assert.IsType<VisitorSupportRequestResult.Sent>(result);
        var saved = Assert.Single(repository.SavedRequests);
        Assert.Equal(((Guid?)null, "visiteur@exemple.test", "Mon compte est bloqué.", Now, SupportRequestStatus.Sent),
            (saved.UserId, saved.ContactEmail, saved.Message, saved.SentAt, saved.Status));
        Assert.Empty(repository.CommittedUserChecks);
    }

    // Les deux champs sont vérifiés avant de répondre : le visiteur apprend en une fois tout ce qui ne va pas.
    [Theory]
    [InlineData(null, "Bonjour", true, false)]
    [InlineData("pas-une-adresse", "Bonjour", true, false)]
    [InlineData("visiteur@exemple.test", null, false, true)]
    [InlineData("visiteur@exemple.test", "a\tb", false, true)]
    [InlineData("pas-une-adresse", "", true, true)]
    public async Task VisitorFieldsThatBreakTheRulesAreAllReported(string? contactEmail, string? message, bool isEmailInvalid, bool isMessageInvalid)
    {
        var result = await SendAsVisitorAsync(contactEmail, message);

        var refusal = Assert.IsType<VisitorSupportRequestResult.RefusedByRules>(result);
        Assert.Equal((isEmailInvalid, isMessageInvalid), (refusal.IsContactEmailInvalid, refusal.IsMessageInvalid));
        Assert.Empty(repository.SavedRequests);
    }

    // La limite des visiteurs est celle de la route, par adresse IP : le cas d'utilisation ne compte rien.
    [Fact]
    public async Task VisitorRequestsAreNotCountedByTheUseCase()
    {
        for (var number = 1; number <= 12; number++)
        {
            Assert.IsType<VisitorSupportRequestResult.Sent>(await SendAsVisitorAsync("visiteur@exemple.test", "Demande " + number));
        }

        Assert.Equal(12, repository.SavedRequests.Count);
    }

    // --- Lecture

    // La liste ne montre que les demandes de l'utilisateur, la plus récente d'abord, avec leur nombre de réponses.
    [Fact]
    public async Task ListShowsOnlyTheUsersRequestsNewestFirst()
    {
        var older = AddRequest(user.Id, Now - TimeSpan.FromDays(2));
        var newer = AddRequest(user.Id, Now - TimeSpan.FromDays(1));
        AddRequest(otherUser.Id, Now);
        repository.SavedRequests.Add(SupportRequest.FromVisitor("visiteur@exemple.test", "Bonjour", Now));
        repository.SavedResponses.Add(new SupportResponse { Content = "Réponse", SentAt = Now, SupportRequestId = older.Id });
        repository.SavedResponses.Add(new SupportResponse { Content = "Autre réponse", SentAt = Now, SupportRequestId = older.Id });

        var list = await new SupportRequestReadingService(repository).ListAsync(user.Id);

        Assert.Equal(
            new List<SupportRequestSummary>
            {
                new SupportRequestSummary(newer.Id, newer.SentAt, SupportRequestStatus.Sent, 0),
                new SupportRequestSummary(older.Id, older.SentAt, SupportRequestStatus.Sent, 2)
            },
            list);
    }

    // Une demande se lit avec son message et ses réponses, de la plus ancienne à la plus récente.
    [Fact]
    public async Task RequestIsReadWithItsResponsesInOrder()
    {
        var request = AddRequest(user.Id, Now - TimeSpan.FromDays(1), "Mon sac a disparu.");
        repository.SavedResponses.Add(new SupportResponse { Content = "Seconde", SentAt = Now, SupportRequestId = request.Id });
        repository.SavedResponses.Add(new SupportResponse { Content = "Première", SentAt = Now - TimeSpan.FromHours(1), SupportRequestId = request.Id });

        var details = await new SupportRequestReadingService(repository).ReadAsync(user.Id, request.Id);

        Assert.NotNull(details);
        Assert.Equal("Mon sac a disparu.", details.Message);
        Assert.Equal(new List<string> { "Première", "Seconde" }, details.Responses.Select(response => response.Content).ToList());
    }

    // La demande d'un autre utilisateur, ou d'un visiteur, n'est jamais trouvée.
    [Fact]
    public async Task RequestOfSomeoneElseIsNeverFound()
    {
        var otherRequest = AddRequest(otherUser.Id, Now);
        var visitorRequest = SupportRequest.FromVisitor("visiteur@exemple.test", "Bonjour", Now);
        repository.SavedRequests.Add(visitorRequest);
        var readingService = new SupportRequestReadingService(repository);

        Assert.Null(await readingService.ReadAsync(user.Id, otherRequest.Id));
        Assert.Null(await readingService.ReadAsync(user.Id, visitorRequest.Id));
        Assert.Null(await readingService.ReadAsync(user.Id, Guid.NewGuid()));
    }

    // --- Effacement après un an (GR Admin)

    // Une demande envoyée il y a plus d'un an est effacée avec ses réponses ; celle d'il y a un an exactement est gardée.
    [Fact]
    public async Task RequestsOlderThanOneYearAreErasedWithTheirResponses()
    {
        var tooOld = AddRequest(user.Id, Now.AddYears(-1) - TimeSpan.FromTicks(10));
        var exactlyOneYear = AddRequest(user.Id, Now.AddYears(-1));
        var recent = AddRequest(otherUser.Id, Now);
        repository.SavedResponses.Add(new SupportResponse { Content = "Ancienne", SentAt = tooOld.SentAt, SupportRequestId = tooOld.Id });
        repository.SavedResponses.Add(new SupportResponse { Content = "Gardée", SentAt = Now, SupportRequestId = exactlyOneYear.Id });

        var deletedCount = await new SupportRequestCleanupService(repository, clock).DeleteOldRequestsAsync();

        Assert.Equal(1, deletedCount);
        Assert.Equal(new List<SupportRequest> { exactlyOneYear, recent }, repository.SavedRequests);
        Assert.Equal("Gardée", Assert.Single(repository.SavedResponses).Content);
    }
}
