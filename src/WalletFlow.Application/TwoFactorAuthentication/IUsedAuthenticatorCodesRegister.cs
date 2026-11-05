namespace WalletFlow.Application.TwoFactorAuthentication;

// Se souvient du dernier code utilisé par chaque compte : un code ne sert qu'une seule fois (SS §2). Implémenté dans la couche Infrastructure.
public interface IUsedAuthenticatorCodesRegister
{
    // Note cette période comme utilisée par ce compte. Renvoie faux si ce compte a déjà utilisé
    // un code de cette période ou d'une période plus récente : une seule requête peut donc réussir avec un même code.
    bool TryToRecordThisPeriodAsUsedByThisAccount(Guid identifierOfTheAccountUsingTheCode, long numberOfThePeriodOfTheCode);
}
