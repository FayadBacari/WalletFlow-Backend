using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// SS §6 : sous une rafale de requêtes, Argon2 refuse les calculs de trop. Chaque route qui calcule un hachage répond alors
// « serveur occupé » (503), avec le délai après lequel réessayer, sans rien changer. La connexion et l'inscription ont leur
// propre test ; voici toutes les autres.
public class ServerBusyEndpointTests
{
    private const string NewEmail = "nouvelle@exemple.test";

    // Lance l'API avec les vrais outils Argon2, qu'un test sature au moment choisi : il prépare d'abord ce qu'il lui faut.
    private sealed class ApiFactoryWithSaturableArgon : ApiFactoryWithFakes
    {
        public bool IsSaturated { get; set; } = false;

        // Remplace les deux outils de hachage avant le démarrage de l'API (nom imposé par le framework).
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IPasswordHasher>(new SaturablePasswordHasher(this, RealArgonHashers.CreatePasswordHasher()));
                services.AddSingleton<IBackupCodeHasher>(new SaturableBackupCodeHasher(this, RealArgonHashers.CreateBackupCodeHasher()));
            });
        }
    }

    // Le vrai hachage des mots de passe, qui refuse tout calcul une fois l'API saturée.
    private sealed class SaturablePasswordHasher(ApiFactoryWithSaturableArgon api, IPasswordHasher realHasher) : IPasswordHasher
    {
        // Hache le mot de passe, sauf si l'API est saturée.
        public Task<string> HashAsync(string password, CancellationToken cancellationToken = default)
        {
            ThrowIfSaturated(api);
            return realHasher.HashAsync(password, cancellationToken);
        }

        // Compare le mot de passe à son empreinte, sauf si l'API est saturée.
        public Task<bool> VerifyAsync(string password, string storedHash, CancellationToken cancellationToken = default)
        {
            ThrowIfSaturated(api);
            return realHasher.VerifyAsync(password, storedHash, cancellationToken);
        }
    }

    // Le vrai hachage des codes de secours, qui refuse tout calcul une fois l'API saturée.
    private sealed class SaturableBackupCodeHasher(ApiFactoryWithSaturableArgon api, IBackupCodeHasher realHasher) : IBackupCodeHasher
    {
        // Hache le code de secours, sauf si l'API est saturée.
        public Task<string> HashAsync(string cleanedCode, Guid accountId, CancellationToken cancellationToken = default)
        {
            ThrowIfSaturated(api);
            return realHasher.HashAsync(cleanedCode, accountId, cancellationToken);
        }
    }

    // Refuse le calcul comme Argon2 quand sa file d'attente est pleine.
    private static void ThrowIfSaturated(ApiFactoryWithSaturableArgon api)
    {
        if (api.IsSaturated)
        {
            throw new ServerBusyException();
        }
    }

    // Vérifie le refus « serveur occupé » : son code, son titre, et le délai de 5 secondes avant de réessayer.
    private static async Task AssertServerBusyAsync(HttpResponseMessage response)
    {
        await AssertProblemTitleAsync(response, HttpStatusCode.ServiceUnavailable, ErrorMessages.ServerBusy);
        Assert.Equal("5", Assert.Single(response.Headers.GetValues("Retry-After")));
    }

    // Connecte le compte des tests et renvoie son navigateur et son jeton anti-CSRF.
    private static async Task<(HttpClient Client, string CsrfToken)> LogInAsync(ApiFactoryWithSaturableArgon api)
    {
        var client = CreateBrowserClient(api);

        return (client, await LogInThenGetCsrfTokenAsync(client));
    }

    // Attend l'e-mail envoyé à cette adresse, et renvoie le jeton de son lien.
    private static async Task<string> WaitForLinkTokenAsync(ApiFactoryWithSaturableArgon api, string recipient)
    {
        await WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo(recipient) == 1);

        return ReadConfirmationToken(api.EmailSender.SentEmails.Single(email => email.RecipientAddress == recipient).Body);
    }

    // --- Liens reçus par e-mail

    [Fact]
    public async Task AccountConfirmationAnswersBusy()
    {
        using var api = new ApiFactoryWithSaturableArgon();
        var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RegistrationEndpoint, new { email = Email, password = ValidPassword })).StatusCode);
        var token = await WaitForLinkTokenAsync(api, Email);
        api.IsSaturated = true;

        var response = await client.PostAsJsonAsync(ConfirmationEndpoint, new { token = token, password = ValidPassword });

        await AssertServerBusyAsync(response);
        Assert.False(Assert.Single(api.AccountRepository.SavedUsers).IsEmailConfirmed);
    }

    [Fact]
    public async Task PasswordResetAnswersBusy()
    {
        using var api = new ApiFactoryWithSaturableArgon();
        var account = await AddAccountAsync(api);
        var passwordHashBefore = account.PasswordHash;
        var client = CreateBrowserClient(api);
        var token = await RequestRecoveryThenReadTokenAsync(api, client, Email);
        api.IsSaturated = true;

        var response = await SendResetPasswordRequestAsync(client, token);

        await AssertServerBusyAsync(response);
        Assert.Equal(passwordHashBefore, account.PasswordHash);
    }

    [Fact]
    public async Task EmailChangeConfirmationAnswersBusy()
    {
        using var api = new ApiFactoryWithSaturableArgon();
        var account = await AddAccountAsync(api);
        var (client, csrfToken) = await LogInAsync(api);
        var request = await SendSettingsRequestAsync(client, "/api/account/email", new { newEmail = NewEmail, password = ValidPassword }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var token = await WaitForLinkTokenAsync(api, NewEmail);
        api.IsSaturated = true;

        var response = await api.CreateClient().PostAsJsonAsync("/api/account/confirm-email-change", new { token = token, password = ValidPassword });

        await AssertServerBusyAsync(response);
        Assert.Equal(Email, account.Email);
    }

    [Fact]
    public async Task TwoFactorSupportLinkAnswersBusy()
    {
        using var api = new ApiFactoryWithSaturableArgon();
        var account = (await AddActivatedAccountAsync(api)).UserAccount;
        var generated = api.Services.GetRequiredService<ISecureTokenGenerator>().GenerateToken();
        api.AccountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.TwoFactorDeactivation,
            TokenHash = generated.Hash,
            ExpiresAt = api.Clock.GetUtcNow().UtcDateTime + EmailTokenValidityDurations.TwoFactorDeactivationLink,
            UserId = account.Id
        });
        api.IsSaturated = true;

        var response = await api.CreateClient().PostAsJsonAsync("/api/account/disable-two-factor", new { token = generated.Token, password = ValidPassword });

        await AssertServerBusyAsync(response);
        Assert.True(account.IsTwoFactorEnabled);
    }

    // --- Actions sensibles d'un utilisateur connecté

    // Chaque action sensible vérifie d'abord le mot de passe : saturé, ce contrôle refuse tout, et rien ne change.
    [Theory]
    [InlineData("/api/account/delete")]
    [InlineData("/api/account/email")]
    [InlineData("/api/account/export")]
    [InlineData("/api/account/password")]
    [InlineData(ActivationStartEndpoint)]
    public async Task ActionCheckingThePasswordAnswersBusy(string address)
    {
        using var api = new ApiFactoryWithSaturableArgon();
        var account = await AddAccountAsync(api);
        var (client, csrfToken) = await LogInAsync(api);
        api.IsSaturated = true;

        var response = await SendSettingsRequestAsync(client, address,
            new { password = ValidPassword, currentPassword = ValidPassword, newPassword = "Nouveau-Mot-De-Passe-3", newEmail = NewEmail }, csrfToken);

        await AssertServerBusyAsync(response);
        Assert.Null(account.DeletionRequestedAt);
        Assert.Equal(Email, account.Email);
        Assert.Empty(api.ExportRepository.ReadUserIds);
        Assert.Empty(api.EmailRequests.SavedRequests);
    }

    [Fact]
    public async Task ActivationFinishAnswersBusy()
    {
        using var api = new ApiFactoryWithSaturableArgon();
        var account = await AddAccountAsync(api);
        var (client, csrfToken) = await LogInAsync(api);
        var secret = await StartActivationThenReadSecretAsync(client, csrfToken);
        api.IsSaturated = true;

        var response = await SendSettingsRequestAsync(client, ActivationFinishEndpoint, new { code = CalculateCurrentCode(api, secret) }, csrfToken);

        await AssertServerBusyAsync(response);
        Assert.False(account.IsTwoFactorEnabled);
        Assert.Empty(api.TwoFactorRepository.SavedBackupCodes);
    }

    [Fact]
    public async Task DeactivationAnswersBusy()
    {
        using var api = new ApiFactoryWithSaturableArgon();
        var activated = await AddActivatedAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInWithCodeThenGetCsrfTokenAsync(api, client, activated.Secret);
        api.IsSaturated = true;

        var response = await SendSettingsRequestAsync(client, DeactivationEndpoint, new { password = ValidPassword, code = activated.BackupCode }, csrfToken);

        await AssertServerBusyAsync(response);
        Assert.True(activated.UserAccount.IsTwoFactorEnabled);
    }

    // Un code de secours se vérifie par Argon2 : saturé, la connexion reste en attente, sans session.
    [Fact]
    public async Task BackupCodeAtLoginAnswersBusy()
    {
        using var api = new ApiFactoryWithSaturableArgon();
        var activated = await AddActivatedAccountAsync(api);
        var client = CreateBrowserClient(api);
        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestAsync(client)).StatusCode);
        api.IsSaturated = true;

        var response = await SendTwoFactorLoginRequestAsync(client, activated.BackupCode);

        await AssertServerBusyAsync(response);
        Assert.Empty(api.SessionRepository.SavedSessions);
        Assert.Single(api.TwoFactorRepository.SavedBackupCodes);
    }
}
