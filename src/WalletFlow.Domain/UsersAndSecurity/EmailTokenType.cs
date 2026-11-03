namespace WalletFlow.Domain.UsersAndSecurity;

// Usage d'un lien envoyé par e-mail (type_jeton, DD §4.2).
public enum EmailTokenType
{
    SignupConfirmation, // confirmation_inscription
    PasswordRecovery,   // recuperation_mdp
    AccountUnlock,      // deblocage
    EmailChange         // changement_email
}
