using WalletFlow.Application.Configuration;
using WalletFlow.Application.Security;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : l'utilisateur connecté commence l'autorisation de sa banque, pour la connecter ou la renouveler (GR Comptes).
// La connexion se lance toujours depuis WalletFlow, avec un paramètre state aléatoire lié à son compte (SS §7).
public class BankAuthorizationStartService
{
    // La page fixe du site où la banque renvoie l'utilisateur (SS §7).
    public const string ReturnPagePath = "/bank-callback";

    private readonly IBankDataProvider bankProvider;
    private readonly IBankConnectionRepository connectionRepository;
    private readonly IBankAuthorizationStore authorizationStore;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly PublicWebsiteSettings websiteSettings;
    private readonly TimeProvider clock;

    // Reçoit le prestataire, l'accès aux connexions, la mémoire des autorisations commencées, le créateur de jetons,
    // l'adresse du site et l'horloge.
    public BankAuthorizationStartService(
        IBankDataProvider bankProvider,
        IBankConnectionRepository connectionRepository,
        IBankAuthorizationStore authorizationStore,
        ISecureTokenGenerator tokenGenerator,
        PublicWebsiteSettings websiteSettings,
        TimeProvider clock)
    {
        this.bankProvider = bankProvider;
        this.connectionRepository = connectionRepository;
        this.authorizationStore = authorizationStore;
        this.tokenGenerator = tokenGenerator;
        this.websiteSettings = websiteSettings;
        this.clock = clock;
    }

    // Commence l'autorisation d'une nouvelle banque, choisie dans la liste du prestataire.
    public async Task<BankAuthorizationStartResult> StartNewAsync(Guid userId, string? bankName)
    {
        if (string.IsNullOrWhiteSpace(bankName))
        {
            return new BankAuthorizationStartResult.UnknownBank();
        }

        return await StartAsync(userId, bankName, null);
    }

    // Commence le renouvellement d'une connexion de l'utilisateur, auprès de la même banque (GR Comptes).
    public async Task<BankAuthorizationStartResult> StartRenewalAsync(Guid userId, Guid connectionId)
    {
        var connection = await connectionRepository.FindConnectionAsync(userId, connectionId);

        if (connection == null)
        {
            return new BankAuthorizationStartResult.NotFound();
        }

        return await StartAsync(userId, connection.BankName, connection.Id);
    }

    // Demande l'autorisation la plus longue que la banque accepte, jusqu'à 180 jours, et garde le state 15 minutes.
    private async Task<BankAuthorizationStartResult> StartAsync(Guid userId, string bankName, Guid? renewedConnectionId)
    {
        try
        {
            var banks = await bankProvider.ListBanksAsync();
            var bank = banks.FirstOrDefault(provided => provided.Name == bankName);

            if (bank == null)
            {
                return new BankAuthorizationStartResult.UnknownBank();
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var state = tokenGenerator.GenerateToken();
            authorizationStore.Save(state.Hash, new PendingBankAuthorization(userId, bank.Name, renewedConnectionId));

            var bankAddress = await bankProvider.StartAuthorizationAsync(new BankAuthorizationRequest(
                bank.Name,
                BankConnectionRules.ChooseAuthorizationEnd(now, bank.MaximumAuthorization),
                state.Token,
                websiteSettings.BuildPageAddress(ReturnPagePath)));

            return new BankAuthorizationStartResult.Started { BankAddress = bankAddress };
        }
        catch (BankProviderException)
        {
            return new BankAuthorizationStartResult.ProviderUnavailable();
        }
    }
}
