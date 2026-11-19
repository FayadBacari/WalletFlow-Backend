using System.Security.Cryptography;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.TwoFactorServicesWithFakeTools;

namespace WalletFlow.Tests.Application;

// Vérifie le rechiffrement des secrets de double authentification avec la clé actuelle (SS §7), avec de faux outils et le
// vrai AES-256-GCM.
public class TotpSecretReEncryptionServiceTests
{
    private readonly TwoFactorServicesWithFakeTools services = new TwoFactorServicesWithFakeTools();
    private readonly string oldKey = TwoFactorTestTools.CreateEncryptionKey();

    // Les comptes de ces tests ont leur secret chiffré avec l'ancienne clé, la clé 1.
    public TotpSecretReEncryptionServiceTests()
    {
        services.SecretProtector = new AesGcmTotpSecretProtector(oldKey);
    }

    // Passe à la clé 2, et garde la clé 1 pour déchiffrer.
    private void ChangeToANewKey()
    {
        services.SecretProtector = new AesGcmTotpSecretProtector(2, TwoFactorTestTools.CreateEncryptionKey(), new Dictionary<int, string> { [1] = oldKey });
    }

    // Crée le service à tester, avec la clé de ce moment.
    private TotpSecretReEncryptionService CreateService()
    {
        return new TotpSecretReEncryptionService(
            services.AccountRepository,
            new DatabaseChangesSavingService(services.UnitOfWork),
            services.SecretProtector);
    }

    // Le secret de la clé 1 est trouvé, puis rechiffré avec la clé 2 : c'est toujours le même secret, et le pas du dernier code
    // ne change pas. Un second passage n'a plus rien à faire.
    [Fact]
    public async Task OldKeySecretIsReEncryptedWithTheCurrentKeyOnce()
    {
        var activatedAccount = services.AddActivatedAccount();
        var account = activatedAccount.UserAccount;
        var lastStep = account.LastAuthenticatorStep;
        ChangeToANewKey();
        var service = CreateService();

        var found = await service.FindAccountsToReEncryptAsync(null);
        var firstTime = await service.ReEncryptAsync(account.Id);
        var secondTime = await service.ReEncryptAsync(account.Id);

        Assert.Equal(account.Id, Assert.Single(found));
        Assert.True(firstTime);
        Assert.False(secondTime);
        Assert.StartsWith("k2$", account.EncryptedTotpSecret);
        Assert.Equal(activatedAccount.Secret, services.SecretProtector.Decrypt(account.EncryptedTotpSecret!, account.Id));
        Assert.Equal(lastStep, account.LastAuthenticatorStep);
        Assert.Empty(await service.FindAccountsToReEncryptAsync(null));
    }

    // Le téléphone de l'utilisateur ne change pas : ses codes marchent avant le rechiffrement, avec l'ancienne clé gardée pour
    // déchiffrer, et après, avec la nouvelle.
    [Fact]
    public async Task CodesOfThePhoneWorkBeforeAndAfterTheReEncryption()
    {
        var activatedAccount = services.AddActivatedAccount();
        ChangeToANewKey();

        var before = await services.FinishLoginAsync(await services.LogInThenGetWaitingLoginTokenAsync(), services.CalculateCurrentCode(activatedAccount.Secret));
        await CreateService().ReEncryptAsync(activatedAccount.UserAccount.Id);
        services.WaitForNextCode();
        var after = await services.FinishLoginAsync(await services.LogInThenGetWaitingLoginTokenAsync(), services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(before);
        Assert.IsType<TwoFactorLoginResult.LoggedIn>(after);
        Assert.StartsWith("k2$", activatedAccount.UserAccount.EncryptedTotpSecret);
    }

    // Un compte sans double authentification, ou déjà sur la clé actuelle, n'est ni trouvé ni changé.
    [Fact]
    public async Task AccountWithoutOldKeyIsLeftAlone()
    {
        var withoutTwoFactor = services.AddAccountWithoutTwoFactor("sans-double-auth@exemple.test");
        ChangeToANewKey();
        var onCurrentKey = services.AddActivatedAccount();
        var encryptedSecret = onCurrentKey.UserAccount.EncryptedTotpSecret;
        var service = CreateService();

        Assert.Empty(await service.FindAccountsToReEncryptAsync(null));
        Assert.False(await service.ReEncryptAsync(withoutTwoFactor.Id));
        Assert.False(await service.ReEncryptAsync(onCurrentKey.UserAccount.Id));
        Assert.Equal(encryptedSecret, onCurrentKey.UserAccount.EncryptedTotpSecret);
        Assert.Null(withoutTwoFactor.EncryptedTotpSecret);
    }

    // Un secret qu'aucune clé configurée ne déchiffre lève une erreur, et reste tel quel : il n'est jamais remplacé.
    [Fact]
    public async Task SecretOfARemovedKeyIsNeverReplaced()
    {
        var activatedAccount = services.AddActivatedAccount();
        var encryptedSecret = activatedAccount.UserAccount.EncryptedTotpSecret;
        services.SecretProtector = new AesGcmTotpSecretProtector(2, TwoFactorTestTools.CreateEncryptionKey(), new Dictionary<int, string>());

        await Assert.ThrowsAnyAsync<CryptographicException>(() => CreateService().ReEncryptAsync(activatedAccount.UserAccount.Id));

        Assert.Equal(encryptedSecret, activatedAccount.UserAccount.EncryptedTotpSecret);
    }
}
