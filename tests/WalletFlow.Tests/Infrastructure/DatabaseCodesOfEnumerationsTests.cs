using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie, pour chaque valeur des listes, le code écrit en base et le chemin inverse.
// Les codes attendus sont ceux du dictionnaire de données et des CHECK du script SQL.
public class DatabaseCodesOfEnumerationsTests
{
    [Theory]
    [InlineData(UserRole.User, "utilisateur")]
    [InlineData(UserRole.Administrator, "administrateur")]
    public void UserRoleIsConvertedIntoItsDatabaseCodeAndBack(
        UserRole userRoleExpectedOnTheCodeSide, string databaseCodeExpectedInTheRoleColumn)
    {
        Assert.Equal(databaseCodeExpectedInTheRoleColumn,
            UserRoleDatabaseCodes.ConvertUserRoleIntoItsDatabaseCode(userRoleExpectedOnTheCodeSide));
        Assert.Equal(userRoleExpectedOnTheCodeSide,
            UserRoleDatabaseCodes.ConvertDatabaseCodeIntoItsUserRole(databaseCodeExpectedInTheRoleColumn));
    }

    [Theory]
    [InlineData(NotificationChannel.Email, "email")]
    [InlineData(NotificationChannel.Phone, "telephone")]
    [InlineData(NotificationChannel.EmailAndPhone, "les_deux")]
    public void NotificationChannelIsConvertedIntoItsDatabaseCodeAndBack(
        NotificationChannel notificationChannelExpectedOnTheCodeSide, string databaseCodeExpectedInTheChannelColumn)
    {
        Assert.Equal(databaseCodeExpectedInTheChannelColumn,
            NotificationChannelDatabaseCodes.ConvertNotificationChannelIntoItsDatabaseCode(notificationChannelExpectedOnTheCodeSide));
        Assert.Equal(notificationChannelExpectedOnTheCodeSide,
            NotificationChannelDatabaseCodes.ConvertDatabaseCodeIntoItsNotificationChannel(databaseCodeExpectedInTheChannelColumn));
    }

    [Theory]
    [InlineData(EmailTokenType.SignupConfirmation, "confirmation_inscription")]
    [InlineData(EmailTokenType.PasswordRecovery, "recuperation_mdp")]
    [InlineData(EmailTokenType.AccountUnlock, "deblocage")]
    [InlineData(EmailTokenType.EmailChange, "changement_email")]
    public void EmailTokenTypeIsConvertedIntoItsDatabaseCodeAndBack(
        EmailTokenType emailTokenTypeExpectedOnTheCodeSide, string databaseCodeExpectedInTheTokenTypeColumn)
    {
        Assert.Equal(databaseCodeExpectedInTheTokenTypeColumn,
            EmailTokenTypeDatabaseCodes.ConvertEmailTokenTypeIntoItsDatabaseCode(emailTokenTypeExpectedOnTheCodeSide));
        Assert.Equal(emailTokenTypeExpectedOnTheCodeSide,
            EmailTokenTypeDatabaseCodes.ConvertDatabaseCodeIntoItsEmailTokenType(databaseCodeExpectedInTheTokenTypeColumn));
    }

    [Theory]
    [InlineData(SecurityEventType.Login, "connexion")]
    [InlineData(SecurityEventType.Lockout, "blocage")]
    [InlineData(SecurityEventType.Unlock, "deblocage")]
    [InlineData(SecurityEventType.PasswordRecovery, "recuperation_mdp")]
    [InlineData(SecurityEventType.PasswordChange, "changement_mdp")]
    [InlineData(SecurityEventType.EmailChange, "changement_email")]
    [InlineData(SecurityEventType.TwoFactorActivation, "activation_double_auth")]
    [InlineData(SecurityEventType.BankConnection, "connexion_banque")]
    [InlineData(SecurityEventType.BankDisconnection, "deconnexion_banque")]
    [InlineData(SecurityEventType.DataExport, "export_donnees")]
    [InlineData(SecurityEventType.DeletionRequest, "demande_suppression")]
    [InlineData(SecurityEventType.AdminAction, "action_admin")]
    [InlineData(SecurityEventType.RoleChange, "changement_role")]
    [InlineData(SecurityEventType.AccessDenied, "acces_refuse")]
    public void SecurityEventTypeIsConvertedIntoItsDatabaseCodeAndBack(
        SecurityEventType securityEventTypeExpectedOnTheCodeSide, string databaseCodeExpectedInTheEventTypeColumn)
    {
        Assert.Equal(databaseCodeExpectedInTheEventTypeColumn,
            SecurityEventTypeDatabaseCodes.ConvertSecurityEventTypeIntoItsDatabaseCode(securityEventTypeExpectedOnTheCodeSide));
        Assert.Equal(securityEventTypeExpectedOnTheCodeSide,
            SecurityEventTypeDatabaseCodes.ConvertDatabaseCodeIntoItsSecurityEventType(databaseCodeExpectedInTheEventTypeColumn));
    }

    [Theory]
    [InlineData(SecurityEventResult.Success, "succes")]
    [InlineData(SecurityEventResult.Failure, "echec")]
    public void SecurityEventResultIsConvertedIntoItsDatabaseCodeAndBack(
        SecurityEventResult securityEventResultExpectedOnTheCodeSide, string databaseCodeExpectedInTheEventResultColumn)
    {
        Assert.Equal(databaseCodeExpectedInTheEventResultColumn,
            SecurityEventResultDatabaseCodes.ConvertSecurityEventResultIntoItsDatabaseCode(securityEventResultExpectedOnTheCodeSide));
        Assert.Equal(securityEventResultExpectedOnTheCodeSide,
            SecurityEventResultDatabaseCodes.ConvertDatabaseCodeIntoItsSecurityEventResult(databaseCodeExpectedInTheEventResultColumn));
    }

    [Theory]
    [InlineData(BankConnectionStatus.Active, "active")]
    [InlineData(BankConnectionStatus.InError, "en_erreur")]
    [InlineData(BankConnectionStatus.Expired, "expiree")]
    public void BankConnectionStatusIsConvertedIntoItsDatabaseCodeAndBack(
        BankConnectionStatus bankConnectionStatusExpectedOnTheCodeSide, string databaseCodeExpectedInTheConnectionStatusColumn)
    {
        Assert.Equal(databaseCodeExpectedInTheConnectionStatusColumn,
            BankConnectionStatusDatabaseCodes.ConvertBankConnectionStatusIntoItsDatabaseCode(bankConnectionStatusExpectedOnTheCodeSide));
        Assert.Equal(bankConnectionStatusExpectedOnTheCodeSide,
            BankConnectionStatusDatabaseCodes.ConvertDatabaseCodeIntoItsBankConnectionStatus(databaseCodeExpectedInTheConnectionStatusColumn));
    }

    [Theory]
    [InlineData(SuggestionRule.CategoryOverrun, "depassement_categorie")]
    [InlineData(SuggestionRule.UnallocatedMoney, "argent_disponible")]
    [InlineData(SuggestionRule.InsufficientSavings, "epargne_insuffisante")]
    public void SuggestionRuleIsConvertedIntoItsDatabaseCodeAndBack(
        SuggestionRule suggestionRuleExpectedOnTheCodeSide, string databaseCodeExpectedInTheSuggestionRuleColumn)
    {
        Assert.Equal(databaseCodeExpectedInTheSuggestionRuleColumn,
            SuggestionRuleDatabaseCodes.ConvertSuggestionRuleIntoItsDatabaseCode(suggestionRuleExpectedOnTheCodeSide));
        Assert.Equal(suggestionRuleExpectedOnTheCodeSide,
            SuggestionRuleDatabaseCodes.ConvertDatabaseCodeIntoItsSuggestionRule(databaseCodeExpectedInTheSuggestionRuleColumn));
    }

    [Theory]
    [InlineData(SuggestionStatus.Proposed, "proposee")]
    [InlineData(SuggestionStatus.Accepted, "acceptee")]
    [InlineData(SuggestionStatus.Ignored, "ignoree")]
    public void SuggestionStatusIsConvertedIntoItsDatabaseCodeAndBack(
        SuggestionStatus suggestionStatusExpectedOnTheCodeSide, string databaseCodeExpectedInTheSuggestionStatusColumn)
    {
        Assert.Equal(databaseCodeExpectedInTheSuggestionStatusColumn,
            SuggestionStatusDatabaseCodes.ConvertSuggestionStatusIntoItsDatabaseCode(suggestionStatusExpectedOnTheCodeSide));
        Assert.Equal(suggestionStatusExpectedOnTheCodeSide,
            SuggestionStatusDatabaseCodes.ConvertDatabaseCodeIntoItsSuggestionStatus(databaseCodeExpectedInTheSuggestionStatusColumn));
    }

    [Theory]
    [InlineData(SupportRequestStatus.Sent, "envoyee")]
    [InlineData(SupportRequestStatus.InProgress, "en_cours")]
    [InlineData(SupportRequestStatus.Resolved, "resolue")]
    public void SupportRequestStatusIsConvertedIntoItsDatabaseCodeAndBack(
        SupportRequestStatus supportRequestStatusExpectedOnTheCodeSide, string databaseCodeExpectedInTheRequestStatusColumn)
    {
        Assert.Equal(databaseCodeExpectedInTheRequestStatusColumn,
            SupportRequestStatusDatabaseCodes.ConvertSupportRequestStatusIntoItsDatabaseCode(supportRequestStatusExpectedOnTheCodeSide));
        Assert.Equal(supportRequestStatusExpectedOnTheCodeSide,
            SupportRequestStatusDatabaseCodes.ConvertDatabaseCodeIntoItsSupportRequestStatus(databaseCodeExpectedInTheRequestStatusColumn));
    }
}
