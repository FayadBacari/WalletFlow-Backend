using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie le choix de l'envoi des e-mails (SS §9) : en développement seulement, ils sont écrits dans un dossier local ;
// ailleurs, aucun e-mail n'est jamais écrit sur le disque, et l'API refuse de démarrer tant que l'envoi réel n'est pas branché.
public class EmailSenderChoiceTests
{
    // Retire les faux envois des tests : l'API garde celui que Program.cs a choisi.
    private static WebApplicationFactory<Program> WithoutFakeEmailSender(WebApplicationFactory<Program> api)
    {
        return api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            foreach (var fakeSender in services.Where(service => service.ServiceType == typeof(IEmailSender) && service.ImplementationInstance is FakeEmailSender).ToList())
            {
                services.Remove(fakeSender);
            }
        }));
    }

    // Donne à cette API les réglages complets d'un serveur SMTP.
    private static WebApplicationFactory<Program> WithSmtpSettings(WebApplicationFactory<Program> api)
    {
        return api.WithWebHostBuilder(builder => builder
            .UseSetting(SmtpConfiguration.HostSetting, "smtp.prestataire.test")
            .UseSetting(SmtpConfiguration.UsernameSetting, "compte-envoi")
            .UseSetting(SmtpConfiguration.PasswordSetting, "Exemple-De-Mot-De-Passe-9")
            .UseSetting(SmtpConfiguration.SenderAddressSetting, "noreply@walletflow.test"));
    }

    // Les liens des e-mails ouvrent un compte : en production, l'API refuse de démarrer plutôt que de les écrire sur le disque.
    [Fact]
    public void ApiRefusesToStartOutsideDevelopmentWithoutARealEmailSender()
    {
        using var productionApi = new ApiFactoryInProduction();
        using var apiWithoutFake = WithoutFakeEmailSender(productionApi);

        var exception = Assert.Throws<InvalidOperationException>(() => apiWithoutFake.CreateClient());

        Assert.Contains("Aucun envoi réel des e-mails", exception.Message);
    }

    // En développement, les e-mails vont dans le dossier local.
    [Fact]
    public void DevelopmentWritesTheEmailsInTheLocalFolder()
    {
        using var developmentApi = new ApiFactoryInDevelopment();
        using var apiWithoutFake = WithoutFakeEmailSender(developmentApi);

        Assert.IsType<LocalFolderEmailSender>(apiWithoutFake.Services.GetRequiredService<IEmailSender>());
    }

    // En développement, sans dossier, l'API refuse de démarrer : aucun e-mail ne se perdrait sans que personne le voie.
    [Fact]
    public void DevelopmentWithoutLocalFolderRefusesToStart()
    {
        using var developmentApi = new ApiFactoryInDevelopment();
        using var apiWithoutFolder = developmentApi.WithWebHostBuilder(builder => builder.UseSetting("LocalEmailsFolder", ""));

        var exception = Assert.Throws<InvalidOperationException>(() => apiWithoutFolder.CreateClient());

        Assert.Contains("LocalEmailsFolder", exception.Message);
    }

    // L'envoi de secours, s'il servait quand même, n'envoie rien et n'écrit rien : il refuse.
    [Fact]
    public async Task SenderNotConfiguredRefusesEverySending()
    {
        await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() =>
            new EmailSenderNotConfigured().SendAsync(new EmailMessage("destinataire@exemple.test", "Objet", "Texte")));
    }

    // En production, avec les réglages SMTP, les e-mails partent par le serveur du prestataire.
    [Fact]
    public void ProductionWithSmtpSettingsSendsThroughTheSmtpServer()
    {
        using var productionApi = new ApiFactoryInProduction();
        using var apiWithSmtp = WithSmtpSettings(productionApi);
        using var apiWithoutFake = WithoutFakeEmailSender(apiWithSmtp);

        Assert.IsType<SmtpEmailSender>(apiWithoutFake.Services.GetRequiredService<IEmailSender>());
    }

    // En développement, même avec les réglages SMTP, les e-mails restent dans le dossier local : aucun vrai e-mail ne part d'un
    // poste de développement.
    [Fact]
    public void DevelopmentKeepsTheLocalFolderEvenWithSmtpSettings()
    {
        using var developmentApi = new ApiFactoryInDevelopment();
        using var apiWithSmtp = WithSmtpSettings(developmentApi);
        using var apiWithoutFake = WithoutFakeEmailSender(apiWithSmtp);

        Assert.IsType<LocalFolderEmailSender>(apiWithoutFake.Services.GetRequiredService<IEmailSender>());
    }

    // Des réglages SMTP à moitié faits empêchent l'API de démarrer, en nommant le réglage qui manque.
    [Fact]
    public void HalfConfiguredSmtpRefusesToStart()
    {
        using var productionApi = new ApiFactoryInProduction();
        using var halfConfiguredApi = productionApi.WithWebHostBuilder(builder => builder.UseSetting(SmtpConfiguration.HostSetting, "smtp.prestataire.test"));

        var exception = Assert.Throws<InvalidOperationException>(() => halfConfiguredApi.CreateClient());

        Assert.Contains(SmtpConfiguration.UsernameSetting, exception.Message, StringComparison.Ordinal);
    }
}
