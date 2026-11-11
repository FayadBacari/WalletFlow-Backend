namespace WalletFlow.Application.TwoFactorAuthentication;

// Les secrets remis à l'utilisateur, qui attendent le premier code, 10 minutes au plus.
// Le MPD interdit de les écrire en base avant l'activation. Implémenté dans la couche Infrastructure.
public interface IWaitingSecretStore
{
    // Garde le secret de ce compte. Un nouveau secret remplace le précédent.
    void Save(Guid accountId, string secret);

    // Donne le secret de ce compte. Renvoie null s'il n'y en a pas, ou s'il a expiré.
    string? Find(Guid accountId);

    // Oublie le secret de ce compte. Ne fait rien s'il n'y en a plus.
    void Remove(Guid accountId);
}
