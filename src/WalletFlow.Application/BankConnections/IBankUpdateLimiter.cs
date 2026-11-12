namespace WalletFlow.Application.BankConnections;

// Limite les mises à jour des comptes demandées par un utilisateur : une par minute (SS §6). Implémenté dans la couche Infrastructure.
public interface IBankUpdateLimiter
{
    // Compte une mise à jour de plus pour cet utilisateur. Renvoie faux s'il en a déjà demandé une dans la dernière minute.
    bool TryCountUpdate(Guid userId);
}
