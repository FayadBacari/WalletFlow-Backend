using WalletFlow.Application.Persistence;

namespace WalletFlow.Tests.Fakes;

// Dit si la fausse base répond : un test la met en panne pour voir le point de santé refuser.
public class FakeDatabaseHealth : IDatabaseHealth
{
    // Simule une base injoignable.
    public bool IsDown { get; set; } = false;

    // Répond comme une base qui marche, sauf en panne.
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!IsDown);
    }
}
