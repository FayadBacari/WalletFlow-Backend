using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie, pour chaque valeur des listes, le code écrit en base et le chemin inverse.
// Les codes attendus sont ceux du dictionnaire de données et des CHECK du script SQL.
public class EnumCodeTablesTests
{
    [Theory]
    [InlineData(UserRole.User, "utilisateur")]
    [InlineData(UserRole.Administrator, "administrateur")]
    public void UserRoleIsConvertedIntoItsDatabaseCodeAndBack(
        UserRole userRole, string databaseCode)
    {
        Assert.Equal(databaseCode,
            UserRoleDatabaseCodes.Table.ToCode(userRole));
        Assert.Equal(userRole,
            UserRoleDatabaseCodes.Table.ToValue(databaseCode));
    }

    [Theory]
    [InlineData(NotificationChannel.Email, "email")]
    [InlineData(NotificationChannel.Phone, "telephone")]
    [InlineData(NotificationChannel.EmailAndPhone, "les_deux")]
    public void NotificationChannelIsConvertedIntoItsDatabaseCodeAndBack(
        NotificationChannel notificationChannel, string databaseCode)
    {
        Assert.Equal(databaseCode,
            NotificationChannelDatabaseCodes.Table.ToCode(notificationChannel));
        Assert.Equal(notificationChannel,
            NotificationChannelDatabaseCodes.Table.ToValue(databaseCode));
    }

    [Theory]
    [InlineData(EmailTokenType.SignupConfirmation, "confirmation_inscription")]
    [InlineData(EmailTokenType.PasswordRecovery, "recuperation_mdp")]
    [InlineData(EmailTokenType.AccountUnlock, "deblocage")]
    [InlineData(EmailTokenType.EmailChange, "changement_email")]
    public void EmailTokenTypeIsConvertedIntoItsDatabaseCodeAndBack(
        EmailTokenType emailTokenType, string databaseCode)
    {
        Assert.Equal(databaseCode,
            EmailTokenTypeDatabaseCodes.Table.ToCode(emailTokenType));
        Assert.Equal(emailTokenType,
            EmailTokenTypeDatabaseCodes.Table.ToValue(databaseCode));
    }

    [Theory]
    [InlineData(SecurityEventType.Login, "connexion")]
    [InlineData(SecurityEventType.Lockout, "blocage")]
    [InlineData(SecurityEventType.Unlock, "deblocage")]
    [InlineData(SecurityEventType.PasswordRecovery, "recuperation_mdp")]
    [InlineData(SecurityEventType.PasswordChange, "changement_mdp")]
    [InlineData(SecurityEventType.EmailChange, "changement_email")]
    [InlineData(SecurityEventType.TwoFactorActivation, "activation_double_auth")]
    [InlineData(SecurityEventType.TwoFactorDeactivation, "desactivation_double_auth")]
    [InlineData(SecurityEventType.BankConnection, "connexion_banque")]
    [InlineData(SecurityEventType.BankDisconnection, "deconnexion_banque")]
    [InlineData(SecurityEventType.DataExport, "export_donnees")]
    [InlineData(SecurityEventType.DeletionRequest, "demande_suppression")]
    [InlineData(SecurityEventType.AdminAction, "action_admin")]
    [InlineData(SecurityEventType.RoleChange, "changement_role")]
    [InlineData(SecurityEventType.AccessDenied, "acces_refuse")]
    public void SecurityEventTypeIsConvertedIntoItsDatabaseCodeAndBack(
        SecurityEventType securityEventType, string databaseCode)
    {
        Assert.Equal(databaseCode,
            SecurityEventTypeDatabaseCodes.Table.ToCode(securityEventType));
        Assert.Equal(securityEventType,
            SecurityEventTypeDatabaseCodes.Table.ToValue(databaseCode));
    }

    [Theory]
    [InlineData(SecurityEventResult.Success, "succes")]
    [InlineData(SecurityEventResult.Failure, "echec")]
    public void SecurityEventResultIsConvertedIntoItsDatabaseCodeAndBack(
        SecurityEventResult securityEventResult, string databaseCode)
    {
        Assert.Equal(databaseCode,
            SecurityEventResultDatabaseCodes.Table.ToCode(securityEventResult));
        Assert.Equal(securityEventResult,
            SecurityEventResultDatabaseCodes.Table.ToValue(databaseCode));
    }

    [Theory]
    [InlineData(BankConnectionStatus.Active, "active")]
    [InlineData(BankConnectionStatus.InError, "en_erreur")]
    [InlineData(BankConnectionStatus.Expired, "expiree")]
    public void BankConnectionStatusIsConvertedIntoItsDatabaseCodeAndBack(
        BankConnectionStatus bankConnectionStatus, string databaseCode)
    {
        Assert.Equal(databaseCode,
            BankConnectionStatusDatabaseCodes.Table.ToCode(bankConnectionStatus));
        Assert.Equal(bankConnectionStatus,
            BankConnectionStatusDatabaseCodes.Table.ToValue(databaseCode));
    }

    [Theory]
    [InlineData(SuggestionRule.CategoryOverrun, "depassement_categorie")]
    [InlineData(SuggestionRule.UnallocatedMoney, "argent_disponible")]
    [InlineData(SuggestionRule.InsufficientSavings, "epargne_insuffisante")]
    public void SuggestionRuleIsConvertedIntoItsDatabaseCodeAndBack(
        SuggestionRule suggestionRule, string databaseCode)
    {
        Assert.Equal(databaseCode,
            SuggestionRuleDatabaseCodes.Table.ToCode(suggestionRule));
        Assert.Equal(suggestionRule,
            SuggestionRuleDatabaseCodes.Table.ToValue(databaseCode));
    }

    [Theory]
    [InlineData(SuggestionStatus.Proposed, "proposee")]
    [InlineData(SuggestionStatus.Accepted, "acceptee")]
    [InlineData(SuggestionStatus.Ignored, "ignoree")]
    public void SuggestionStatusIsConvertedIntoItsDatabaseCodeAndBack(
        SuggestionStatus suggestionStatus, string databaseCode)
    {
        Assert.Equal(databaseCode,
            SuggestionStatusDatabaseCodes.Table.ToCode(suggestionStatus));
        Assert.Equal(suggestionStatus,
            SuggestionStatusDatabaseCodes.Table.ToValue(databaseCode));
    }

    [Theory]
    [InlineData(SupportRequestStatus.Sent, "envoyee")]
    [InlineData(SupportRequestStatus.InProgress, "en_cours")]
    [InlineData(SupportRequestStatus.Resolved, "resolue")]
    public void SupportRequestStatusIsConvertedIntoItsDatabaseCodeAndBack(
        SupportRequestStatus supportRequestStatus, string databaseCode)
    {
        Assert.Equal(databaseCode,
            SupportRequestStatusDatabaseCodes.Table.ToCode(supportRequestStatus));
        Assert.Equal(supportRequestStatus,
            SupportRequestStatusDatabaseCodes.Table.ToValue(databaseCode));
    }
}
