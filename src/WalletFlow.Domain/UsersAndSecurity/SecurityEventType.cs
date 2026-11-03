namespace WalletFlow.Domain.UsersAndSecurity;

// Nature d'un événement du journal de sécurité (type_evenement, DD §4.5).
public enum SecurityEventType
{
    Login,               // connexion
    Lockout,             // blocage
    Unlock,              // deblocage
    PasswordRecovery,    // recuperation_mdp
    PasswordChange,      // changement_mdp
    EmailChange,         // changement_email
    TwoFactorActivation, // activation_double_auth
    BankConnection,      // connexion_banque (sert aussi au renouvellement de l'autorisation)
    BankDisconnection,   // deconnexion_banque
    DataExport,          // export_donnees
    DeletionRequest,     // demande_suppression
    AdminAction,         // action_admin
    RoleChange,          // changement_role
    AccessDenied         // acces_refuse
}
