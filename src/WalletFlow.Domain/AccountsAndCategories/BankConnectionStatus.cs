namespace WalletFlow.Domain.AccountsAndCategories;

// État d'une connexion bancaire (statut_connexion, DD §5.1).
public enum BankConnectionStatus
{
    Active,  // active
    InError, // en_erreur : la banque ne répond pas, le dernier solde connu est affiché
    Expired  // expiree : l'autorisation de 180 jours n'a pas été renouvelée
}
