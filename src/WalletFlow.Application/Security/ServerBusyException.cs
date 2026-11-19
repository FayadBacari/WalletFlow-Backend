namespace WalletFlow.Application.Security;

// Le serveur refuse un calcul coûteux qu'il ne peut pas faire tout de suite : trop de calculs attendent déjà leur tour. La requête
// reçoit « serveur occupé » plutôt que d'attendre sans fin, et le client réessaie un peu plus tard.
public sealed class ServerBusyException : Exception
{
    // Crée le refus, avec un message qui ne cite aucune donnée de la requête.
    public ServerBusyException()
        : base("Le serveur est momentanément saturé : le calcul n'a pas été fait.")
    {
    }
}
