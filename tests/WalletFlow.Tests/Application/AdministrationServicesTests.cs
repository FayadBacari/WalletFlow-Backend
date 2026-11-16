using System.Net;
using WalletFlow.Application.Administration;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie les cas d'utilisation de l'administration (GR Admin, SS §2, §8 et §11) avec de fausses bases : l'accès réservé à
// l'administrateur, la recherche d'un compte, le déblocage, le lien qui désactive la double authentification, les réponses
// aux demandes et les statistiques. Chaque changement et chaque recherche sont notés au journal, avec le compte visé.
public class AdministrationServicesTests
{
    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.40");

    private readonly FakeUserAccountRepository accounts = new FakeUserAccountRepository();
    private readonly FakeSecurityEventRepository journal = new FakeSecurityEventRepository();
    private readonly FakeEmailRequestRepository emailRequests = new FakeEmailRequestRepository();
    private readonly FakeBankConnectionRepository bank = new FakeBankConnectionRepository();
    private readonly FakeSupportRequestRepository support;
    private readonly FakeAdministrationRepository administration;
    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly FakeDatabaseUnitOfWork unitOfWork;
    private readonly User administrator;

    // Garde l'administrateur, avec la double authentification activée, dans les fausses bases reliées à l'unité de travail.
    public AdministrationServicesTests()
    {
        support = new FakeSupportRequestRepository(accounts.SavedUsers);
        administration = new FakeAdministrationRepository(support, bank);
        unitOfWork = new FakeDatabaseUnitOfWork(accounts, journal, emailRequests, support, administration);
        administrator = AddAccount("admin@exemple.test", UserRole.Administrator, withTwoFactor: true);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private SecurityJournalService CreateJournal()
    {
        return new SecurityJournalService(journal, clock);
    }

    private DatabaseChangesSavingService CreateSaving()
    {
        return new DatabaseChangesSavingService(unitOfWork);
    }

    // Garde un compte confirmé avec cette adresse, ce rôle, et la double authentification si le test la demande.
    private User AddAccount(string email, UserRole role = UserRole.User, bool withTwoFactor = false)
    {
        var account = new User { Email = email, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true, Role = role };

        if (withTwoFactor)
        {
            account.ActivateTwoFactor("secret-chiffre");
        }

        accounts.SavedUsers.Add(account);
        return account;
    }

    // Garde une demande de ce compte, ou d'un visiteur si le compte est null, dans cet état.
    private SupportRequest AddRequest(User? author, SupportRequestStatus status = SupportRequestStatus.Sent, int daysAgo = 1)
    {
        var request = new SupportRequest
        {
            Message = "Bonjour, j'ai perdu mon téléphone.",
            ContactEmail = author == null ? "visiteur@exemple.test" : null,
            Status = status,
            SentAt = Now.AddDays(-daysAgo),
            UserId = author?.Id
        };
        support.SavedRequests.Add(request);

        return request;
    }

    // Garde au journal un changement de mot de passe de ce compte, avec ce résultat, il y a ce temps.
    private void AddPasswordChange(User account, TimeSpan age, SecurityEventResult result = SecurityEventResult.Success)
    {
        journal.SavedSecurityEvents.Add(new SecurityEvent
        {
            Type = SecurityEventType.PasswordChange,
            Result = result,
            OccurredAt = Now - age,
            UserId = account.Id
        });
    }

    // Les actions de l'administrateur notées au journal.
    private List<SecurityEvent> AdminActions()
    {
        return journal.SavedSecurityEvents.Where(securityEvent => securityEvent.Type == SecurityEventType.AdminAction).ToList();
    }

    // Vérifie qu'une seule action de l'administrateur est notée, réussie, avec son adresse IP et ce compte visé.
    private void AssertSingleAdminAction(Guid? targetAccountId)
    {
        var action = Assert.Single(AdminActions());
        Assert.Equal(
            (SecurityEventResult.Success, (Guid?)administrator.Id, targetAccountId, IpAddress),
            (action.Result, action.UserId, action.TargetUserId, action.IpAddress));
    }

    // --- Accès

    // Seul un administrateur qui a activé la double authentification utilise l'administration. Le rôle est relu à chaque fois.
    [Fact]
    public async Task OnlyAnAdministratorWithTwoFactorGetsIn()
    {
        var user = AddAccount("utilisateur@exemple.test", withTwoFactor: true);
        var administratorWithoutTwoFactor = AddAccount("admin-sans-double-auth@exemple.test", UserRole.Administrator);
        var accessService = new AdministratorAccessService(accounts);

        Assert.Equal(AdministratorAccess.Granted, await accessService.CheckAsync(administrator.Id));
        Assert.Equal(AdministratorAccess.TwoFactorRequired, await accessService.CheckAsync(administratorWithoutTwoFactor.Id));
        Assert.Equal(AdministratorAccess.NotAdministrator, await accessService.CheckAsync(user.Id));
        Assert.Equal(AdministratorAccess.NotAdministrator, await accessService.CheckAsync(Guid.NewGuid()));

        EntityValues.WriteProperty(administrator, nameof(User.Role), UserRole.User);
        Assert.Equal(AdministratorAccess.NotAdministrator, await accessService.CheckAsync(administrator.Id));
    }

    // --- Recherche d'un compte

    // L'adresse est cherchée exactement, une fois mise en minuscules ; le compte trouvé vient avec son état et sa double
    // authentification, et la recherche est notée au journal avec lui.
    [Fact]
    public async Task SearchFindsTheExactAddressAndIsJournaledWithTheAccount()
    {
        var account = AddAccount("utilisateur@exemple.test", withTwoFactor: true);
        EntityValues.WriteProperty(account, nameof(User.LockoutEndsAt), Now.AddMinutes(10));
        AddAccount("utilisateur2@exemple.test");

        var result = await new AdminAccountSearchService(accounts, CreateJournal(), clock)
            .SearchAsync(administrator.Id, "  Utilisateur@Exemple.TEST ", IpAddress);

        var found = Assert.IsType<AdminAccountSearchResult.Found>(result);
        Assert.Equal(new AdminAccountView(account.Id, "utilisateur@exemple.test", AccountState.Locked, true), found.Account);
        AssertSingleAdminAction(account.Id);
    }

    // Une recherche sans résultat est notée aussi, sans compte visé. Un début d'adresse ne trouve rien : pas de parcours.
    [Theory]
    [InlineData("inconnu@exemple.test")]
    [InlineData("utilisateur@exemple.tes")]
    public async Task SearchWithoutAccountIsJournaledWithoutTarget(string typedEmail)
    {
        AddAccount("utilisateur@exemple.test");

        var result = await new AdminAccountSearchService(accounts, CreateJournal(), clock).SearchAsync(administrator.Id, typedEmail, IpAddress);

        Assert.IsType<AdminAccountSearchResult.NotFound>(result);
        AssertSingleAdminAction(null);
    }

    // Une adresse mal formée n'est pas cherchée, et ne laisse rien au journal.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("utilisateur")]
    [InlineData("%@exemple.test")]
    public async Task MalformedAddressIsNotSearched(string? typedEmail)
    {
        var result = await new AdminAccountSearchService(accounts, CreateJournal(), clock).SearchAsync(administrator.Id, typedEmail, IpAddress);

        Assert.IsType<AdminAccountSearchResult.EmailBreaksRules>(result);
        Assert.Empty(journal.SavedSecurityEvents);
    }

    // --- Déblocage

    // Le déblocage fait comme le lien : il termine le blocage et redonne les 5 essais, sans toucher au nombre de blocages.
    // Il est noté deux fois : déblocage du compte, et action de l'administrateur sur ce compte.
    [Fact]
    public async Task UnlockActsLikeTheUnlockLinkAndIsJournaledTwice()
    {
        var account = AddAccount("bloque@exemple.test");
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            account.CountLoginAttempt(Now);
        }

        var result = await new AdminAccountUnlockService(accounts, CreateSaving(), CreateJournal(), clock).UnlockAsync(administrator.Id, account.Id, IpAddress);

        var unlocked = Assert.IsType<AdminAccountUnlockResult.Unlocked>(result);
        Assert.Equal(AccountState.Active, unlocked.Account.State);
        Assert.Equal((0, (DateTime?)null, (short)1), (account.FailedLoginAttemptCount, account.LockoutEndsAt, account.LockoutCount));
        var unlockEvent = Assert.Single(journal.SavedSecurityEvents, securityEvent => securityEvent.Type == SecurityEventType.Unlock);
        Assert.Equal((SecurityEventResult.Success, (Guid?)account.Id, (Guid?)null), (unlockEvent.Result, unlockEvent.UserId, unlockEvent.TargetUserId));
        AssertSingleAdminAction(account.Id);
    }

    // Un compte qui n'est pas bloqué, même avec des essais ratés, ne change pas ; rien n'est noté.
    [Fact]
    public async Task AccountThatIsNotLockedIsLeftAlone()
    {
        var account = AddAccount("essais@exemple.test");
        account.CountLoginAttempt(Now);
        account.CountLoginAttempt(Now);

        var result = await new AdminAccountUnlockService(accounts, CreateSaving(), CreateJournal(), clock).UnlockAsync(administrator.Id, account.Id, IpAddress);

        Assert.IsType<AdminAccountUnlockResult.NotLocked>(result);
        Assert.Equal(2, account.FailedLoginAttemptCount);
        Assert.Empty(journal.SavedSecurityEvents);
    }

    [Fact]
    public async Task UnknownAccountIsNotUnlocked()
    {
        var result = await new AdminAccountUnlockService(accounts, CreateSaving(), CreateJournal(), clock).UnlockAsync(administrator.Id, Guid.NewGuid(), IpAddress);

        Assert.IsType<AdminAccountUnlockResult.NotFound>(result);
        Assert.Empty(journal.SavedSecurityEvents);
    }

    // --- Lien qui désactive la double authentification

    // Le lien entre dans la file pour l'adresse du compte, sans être créé : l'administrateur ne le voit jamais.
    [Fact]
    public async Task LinkIsQueuedForTheAccountAndJournaled()
    {
        var account = AddAccount("perdu@exemple.test", withTwoFactor: true);
        AddPasswordChange(account, TimeSpan.FromDays(7));

        var result = await CreateLinkService().SendAsync(administrator.Id, account.Id, IpAddress);

        Assert.IsType<AdminTwoFactorLinkResult.Sent>(result);
        var request = Assert.Single(emailRequests.SavedRequests);
        Assert.Equal(
            (EmailRequestType.TwoFactorDeactivationLink, (Guid?)account.Id, (string?)null, IpAddress),
            (request.Type, request.UserId, request.EmailAddress, request.IpAddress));
        Assert.Empty(accounts.SavedEmailTokens);
        AssertSingleAdminAction(account.Id);
    }

    // Un mot de passe changé il y a moins de 7 jours : le lien ne marcherait pas, et c'est le signe d'un voleur de la boîte mail.
    // Rien ne part, et rien n'est noté.
    [Fact]
    public async Task RecentPasswordChangeStopsTheLink()
    {
        var account = AddAccount("vole@exemple.test", withTwoFactor: true);
        AddPasswordChange(account, TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));

        var result = await CreateLinkService().SendAsync(administrator.Id, account.Id, IpAddress);

        Assert.IsType<AdminTwoFactorLinkResult.PasswordChangedRecently>(result);
        Assert.Empty(emailRequests.SavedRequests);
        Assert.Empty(AdminActions());
    }

    // Seul un changement réussi de ce compte compte : un essai raté, ou le changement d'un autre compte, n'empêche rien.
    [Fact]
    public async Task OnlyASuccessfulChangeOfThisAccountCounts()
    {
        var account = AddAccount("perdu@exemple.test", withTwoFactor: true);
        var otherAccount = AddAccount("autre@exemple.test", withTwoFactor: true);
        AddPasswordChange(account, TimeSpan.FromHours(1), SecurityEventResult.Failure);
        AddPasswordChange(otherAccount, TimeSpan.FromHours(1));

        var result = await CreateLinkService().SendAsync(administrator.Id, account.Id, IpAddress);

        Assert.IsType<AdminTwoFactorLinkResult.Sent>(result);
    }

    // Sans double authentification, il n'y a rien à désactiver ; un compte inconnu n'existe pas.
    [Fact]
    public async Task LinkNeedsAnAccountWithTwoFactor()
    {
        var account = AddAccount("sans-double-auth@exemple.test");

        Assert.IsType<AdminTwoFactorLinkResult.TwoFactorNotEnabled>(await CreateLinkService().SendAsync(administrator.Id, account.Id, IpAddress));
        Assert.IsType<AdminTwoFactorLinkResult.NotFound>(await CreateLinkService().SendAsync(administrator.Id, Guid.NewGuid(), IpAddress));
        Assert.Empty(emailRequests.SavedRequests);
        Assert.Empty(journal.SavedSecurityEvents);
    }

    // Le compte est contrôlé à l'enregistrement : un changement du compte juste avant fait tout relire, et le lien n'entre
    // qu'une fois dans la file, avec une seule action notée.
    [Fact]
    public async Task ConcurrentAccountChangeReplaysTheSendingOnce()
    {
        var account = AddAccount("perdu@exemple.test", withTwoFactor: true);
        accounts.PasswordChangedBeforeCheckedSave = "empreinte-changee-entre-temps";

        var result = await CreateLinkService().SendAsync(administrator.Id, account.Id, IpAddress);

        Assert.IsType<AdminTwoFactorLinkResult.Sent>(result);
        Assert.Equal("empreinte-changee-entre-temps", account.PasswordHash);
        Assert.Single(emailRequests.SavedRequests);
        AssertSingleAdminAction(account.Id);
    }

    private AdminTwoFactorLinkService CreateLinkService()
    {
        return new AdminTwoFactorLinkService(accounts, journal, emailRequests, CreateSaving(), CreateJournal(), clock);
    }

    // --- Réponses aux demandes

    // La réponse est gardée, son e-mail entre dans la file lié à elle, et la demande passe « en cours ». L'action vise le compte
    // qui a écrit la demande.
    [Fact]
    public async Task AnswerIsSavedEmailedAndPutsTheRequestInProgress()
    {
        var author = AddAccount("auteur@exemple.test");
        var request = AddRequest(author);

        var result = await CreateSupportService().AnswerAsync(administrator.Id, request.Id, "  Voici la marche à suivre.\r\nBonne journée. ", IpAddress);

        var answered = Assert.IsType<AdminSupportAnswerResult.Answered>(result);
        Assert.Equal(SupportRequestStatus.InProgress, answered.Request.Status);
        Assert.Equal("auteur@exemple.test", answered.Request.ReplyAddress);
        var response = Assert.Single(support.SavedResponses);
        Assert.Equal((request.Id, "Voici la marche à suivre.\nBonne journée.", Now), (response.SupportRequestId, response.Content, response.SentAt));
        var email = Assert.Single(emailRequests.SavedRequests);
        Assert.Equal((EmailRequestType.SupportResponse, (Guid?)response.Id, (Guid?)null), (email.Type, email.SupportResponseId, email.UserId));
        AssertSingleAdminAction(author.Id);
    }

    // La demande d'un visiteur se répond aussi : l'action ne vise aucun compte, et l'adresse est celle du visiteur.
    [Fact]
    public async Task AnswerToAVisitorTargetsNoAccount()
    {
        var request = AddRequest(null);

        var result = await CreateSupportService().AnswerAsync(administrator.Id, request.Id, "Ton compte est débloqué.", IpAddress);

        Assert.Equal("visiteur@exemple.test", Assert.IsType<AdminSupportAnswerResult.Answered>(result).Request.ReplyAddress);
        AssertSingleAdminAction(null);
    }

    // Une réponse rouvre une demande résolue (GR Admin) : elle redevient « en cours ».
    [Fact]
    public async Task AnswerReopensAResolvedRequest()
    {
        var request = AddRequest(AddAccount("auteur@exemple.test"), SupportRequestStatus.Resolved);

        var result = await CreateSupportService().AnswerAsync(administrator.Id, request.Id, "Un dernier conseil.", IpAddress);

        Assert.Equal(SupportRequestStatus.InProgress, Assert.IsType<AdminSupportAnswerResult.Answered>(result).Request.Status);
        Assert.Equal(SupportRequestStatus.InProgress, request.Status);
    }

    // Une réponse et une demande résolue au même instant passent l'une après l'autre : la réponse relit la demande résolue,
    // et la rouvre. Sa réponse n'est gardée qu'une fois.
    [Fact]
    public async Task AnswerAfterAConcurrentResolutionReopensTheRequest()
    {
        var request = AddRequest(AddAccount("auteur@exemple.test"), SupportRequestStatus.InProgress);
        administration.RequestResolvedBeforeSave = request.Id;

        var result = await CreateSupportService().AnswerAsync(administrator.Id, request.Id, "Réponse", IpAddress);

        Assert.IsType<AdminSupportAnswerResult.Answered>(result);
        Assert.Equal(SupportRequestStatus.InProgress, request.Status);
        Assert.Single(support.SavedResponses);
        Assert.Single(emailRequests.SavedRequests);
        Assert.Single(AdminActions());
    }

    // Un texte vide, ou qui contient un caractère invisible, est refusé ; rien n'est gardé ni noté.
    public static TheoryData<string?> ContentsThatBreakTheRules()
    {
        return new TheoryData<string?> { null, "   ", "a" + (char)0x202E + "b", new string('a', 5001) };
    }

    [Theory]
    [MemberData(nameof(ContentsThatBreakTheRules))]
    public async Task AnswerThatBreaksTheRulesIsRefused(string? content)
    {
        var request = AddRequest(AddAccount("auteur@exemple.test"));

        var result = await CreateSupportService().AnswerAsync(administrator.Id, request.Id, content, IpAddress);

        Assert.IsType<AdminSupportAnswerResult.ContentBreaksRules>(result);
        Assert.Empty(support.SavedResponses);
        Assert.Empty(journal.SavedSecurityEvents);
        Assert.Equal(SupportRequestStatus.Sent, request.Status);
    }

    [Fact]
    public async Task AnswerToAnUnknownRequestIsNotFound()
    {
        var result = await CreateSupportService().AnswerAsync(administrator.Id, Guid.NewGuid(), "Réponse", IpAddress);

        Assert.IsType<AdminSupportAnswerResult.NotFound>(result);
        Assert.Empty(journal.SavedSecurityEvents);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // Au bout de 10 passages en conflit, rien n'est gardé.
    [Fact]
    public async Task EndlessConflictsSaveNoAnswer()
    {
        var request = AddRequest(AddAccount("auteur@exemple.test"));
        administration.Conflicts = 10;

        var result = await CreateSupportService().AnswerAsync(administrator.Id, request.Id, "Réponse", IpAddress);

        Assert.IsType<AdminSupportAnswerResult.NotSaved>(result);
        Assert.Empty(support.SavedResponses);
        Assert.Equal(SupportRequestStatus.Sent, request.Status);
    }

    // L'administrateur marque résolue une demande, avec ou sans réponse ; l'action vise le compte de la demande.
    [Theory]
    [InlineData(SupportRequestStatus.Sent)]
    [InlineData(SupportRequestStatus.InProgress)]
    public async Task RequestIsResolvedAndJournaled(SupportRequestStatus status)
    {
        var author = AddAccount("auteur@exemple.test");
        var request = AddRequest(author, status);

        var result = await CreateSupportService().ResolveAsync(administrator.Id, request.Id, IpAddress);

        Assert.Equal(SupportRequestStatus.Resolved, Assert.IsType<AdminSupportResolutionResult.Resolved>(result).Request.Status);
        Assert.Equal(SupportRequestStatus.Resolved, request.Status);
        AssertSingleAdminAction(author.Id);
    }

    // Une demande déjà résolue ne change pas, et rien n'est noté ; une demande inconnue n'existe pas.
    [Fact]
    public async Task ResolvedOrUnknownRequestIsNotResolvedAgain()
    {
        var request = AddRequest(AddAccount("auteur@exemple.test"), SupportRequestStatus.Resolved);

        Assert.IsType<AdminSupportResolutionResult.AlreadyResolved>(await CreateSupportService().ResolveAsync(administrator.Id, request.Id, IpAddress));
        Assert.IsType<AdminSupportResolutionResult.NotFound>(await CreateSupportService().ResolveAsync(administrator.Id, Guid.NewGuid(), IpAddress));
        Assert.Empty(journal.SavedSecurityEvents);
    }

    // La liste d'un état vient la plus ancienne d'abord, avec l'adresse où répondre et le nombre de réponses.
    [Fact]
    public async Task ListOfAStatusIsOldestFirst()
    {
        var author = AddAccount("auteur@exemple.test");
        var newer = AddRequest(author, daysAgo: 1);
        var older = AddRequest(null, daysAgo: 3);
        AddRequest(author, SupportRequestStatus.Resolved, daysAgo: 5);
        support.SavedResponses.Add(new SupportResponse { Content = "Réponse", SentAt = Now, SupportRequestId = newer.Id });

        var list = await CreateSupportService().ListAsync(SupportRequestStatus.Sent);

        Assert.Equal(
            new List<AdminSupportRequestItem>
            {
                new AdminSupportRequestItem(older.Id, older.SentAt, SupportRequestStatus.Sent, "visiteur@exemple.test", null, 0),
                new AdminSupportRequestItem(newer.Id, newer.SentAt, SupportRequestStatus.Sent, "auteur@exemple.test", author.Id, 1)
            },
            list);
    }

    private AdminSupportService CreateSupportService()
    {
        return new AdminSupportService(administration, emailRequests, CreateSaving(), CreateJournal(), clock);
    }

    // --- Statistiques

    // Les comptes par état, les connexions actives et les demandes par état. Aucun chiffre ne porte sur l'argent.
    [Fact]
    public async Task StatisticsCountAccountsConnectionsAndRequests()
    {
        var unconfirmed = new User { Email = "non-confirme@exemple.test", PasswordHash = "empreinte" };
        accounts.SavedUsers.Add(unconfirmed);
        var locked = AddAccount("bloque@exemple.test");
        EntityValues.WriteProperty(locked, nameof(User.LockoutEndsAt), Now.AddMinutes(5));
        var lockoutOver = AddAccount("fin-de-blocage@exemple.test");
        EntityValues.WriteProperty(lockoutOver, nameof(User.LockoutEndsAt), Now);
        var leaving = AddAccount("suppression@exemple.test");
        EntityValues.WriteProperty(leaving, nameof(User.DeletionRequestedAt), Now.AddDays(-1));
        bank.SavedConnections.Add(new BankConnection { ExternalConnectionId = "c1", BankName = "Banque", AuthorizationExpiresAt = Now.AddDays(90), UserId = locked.Id });
        bank.SavedConnections.Add(new BankConnection
        {
            ExternalConnectionId = "c2",
            BankName = "Banque",
            AuthorizationExpiresAt = Now.AddDays(-1),
            UserId = locked.Id,
            Status = BankConnectionStatus.Expired
        });
        AddRequest(null);
        AddRequest(locked);
        AddRequest(locked, SupportRequestStatus.InProgress);
        AddRequest(null, SupportRequestStatus.Resolved);

        var statistics = await new AdminStatisticsService(administration, clock).ReadAsync();

        Assert.Equal(new AdministrationStatistics(1, 2, 1, 1, 1, 2, 1, 1), statistics);
    }
}
