using System.Globalization;

namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : l'utilisateur connecté voit les banques qu'il peut connecter (GR Comptes).
public class BankListService
{
    // L'ordre alphabétique français : « Crédit Agricole » se range avec les C.
    private static readonly CompareInfo FrenchOrder = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    private readonly IBankDataProvider bankProvider;

    // Reçoit le prestataire bancaire.
    public BankListService(IBankDataProvider bankProvider)
    {
        this.bankProvider = bankProvider;
    }

    // Donne les banques que le prestataire sait lire, dans l'ordre alphabétique.
    public async Task<BankListResult> ListAsync()
    {
        try
        {
            var banks = await bankProvider.ListBanksAsync();
            banks.Sort((first, second) => FrenchOrder.Compare(first.Name, second.Name));

            return new BankListResult.Listed { Banks = banks };
        }
        catch (BankProviderException)
        {
            return new BankListResult.ProviderUnavailable();
        }
    }
}
