using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie que les nouvelles entités ont les valeurs par défaut du dictionnaire de données.
public class DefaultValuesOfNewEntitiesTests
{
    // DD §3 : rôle utilisateur, e-mail non confirmé, double authentification désactivée,
    // alertes par e-mail, compteurs à zéro, ni blocage ni demande de suppression.
    [Fact]
    public void NewUserStartsWithTheDefaultValuesOfTheDataDictionary()
    {
        var newUserCreatedWithoutAnyChosenValue = new User();

        Assert.Equal(UserRole.User, newUserCreatedWithoutAnyChosenValue.Role);
        Assert.False(newUserCreatedWithoutAnyChosenValue.IsEmailConfirmed);
        Assert.False(newUserCreatedWithoutAnyChosenValue.IsTwoFactorEnabled);
        Assert.Null(newUserCreatedWithoutAnyChosenValue.EncryptedTotpSecret);
        Assert.Equal(NotificationChannel.Email, newUserCreatedWithoutAnyChosenValue.NotificationChannel);
        Assert.Equal(0, newUserCreatedWithoutAnyChosenValue.FailedLoginAttemptCount);
        Assert.Equal(0, newUserCreatedWithoutAnyChosenValue.LockoutCount);
        Assert.Null(newUserCreatedWithoutAnyChosenValue.LockoutEndsAt);
        Assert.Null(newUserCreatedWithoutAnyChosenValue.DeletionRequestedAt);
    }

    // SS §4 : chaque identifiant est un UUID aléatoire, jamais vide et jamais réutilisé.
    [Fact]
    public void EveryNewEntityReceivesItsOwnRandomUniqueIdentifier()
    {
        var firstNewUserCreatedForThisTest = new User();
        var secondNewUserCreatedForThisTest = new User();

        Assert.NotEqual(Guid.Empty, firstNewUserCreatedForThisTest.Id);
        Assert.NotEqual(firstNewUserCreatedForThisTest.Id, secondNewUserCreatedForThisTest.Id);
    }

    // DD §5.1 : une nouvelle connexion bancaire est active.
    [Fact]
    public void NewBankConnectionStartsInTheActiveStatus()
    {
        var newBankConnectionCreatedWithoutAnyChosenValue = new BankConnection();

        Assert.Equal(BankConnectionStatus.Active, newBankConnectionCreatedWithoutAnyChosenValue.Status);
    }

    // DD §5.3 : une transaction n'est un transfert interne que si l'utilisateur la marque ainsi.
    [Fact]
    public void NewBankTransactionIsNotAnInternalTransferByDefault()
    {
        var newBankTransactionCreatedWithoutAnyChosenValue = new BankTransaction();

        Assert.False(newBankTransactionCreatedWithoutAnyChosenValue.IsInternalTransfer);
    }

    // DD §6.1 : seule la catégorie « Autres » a cette marque.
    [Fact]
    public void NewCategoryIsNotTheOtherCategoryByDefault()
    {
        var newCategoryCreatedWithoutAnyChosenValue = new Category();

        Assert.False(newCategoryCreatedWithoutAnyChosenValue.IsOtherCategory);
    }

    // DD §8 : une nouvelle suggestion est proposée, sans date de changement d'état.
    [Fact]
    public void NewSavingsSuggestionStartsInTheProposedStatus()
    {
        var newSavingsSuggestionCreatedWithoutAnyChosenValue = new SavingsSuggestion();

        Assert.Equal(SuggestionStatus.Proposed, newSavingsSuggestionCreatedWithoutAnyChosenValue.Status);
        Assert.Null(newSavingsSuggestionCreatedWithoutAnyChosenValue.StatusChangedAt);
    }

    // DD §9.2 : une alerte est activée tant que l'utilisateur ne la désactive pas.
    [Fact]
    public void NewAlertPreferenceKeepsTheAlertEnabledByDefault()
    {
        var newAlertPreferenceCreatedWithoutAnyChosenValue = new AlertPreference();

        Assert.True(newAlertPreferenceCreatedWithoutAnyChosenValue.IsEnabled);
    }

    // DD §10.1 : une nouvelle demande de support est envoyée.
    [Fact]
    public void NewSupportRequestStartsInTheSentStatus()
    {
        var newSupportRequestCreatedWithoutAnyChosenValue = new SupportRequest();

        Assert.Equal(SupportRequestStatus.Sent, newSupportRequestCreatedWithoutAnyChosenValue.Status);
    }
}
