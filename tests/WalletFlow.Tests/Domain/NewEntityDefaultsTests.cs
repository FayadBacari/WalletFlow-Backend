using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie que les nouvelles entités ont les valeurs par défaut du dictionnaire de données.
public class NewEntityDefaultsTests
{
    // DD §3 : rôle utilisateur, e-mail non confirmé, double authentification désactivée,
    // alertes par e-mail, compteurs à zéro, ni blocage ni demande de suppression.
    [Fact]
    public void NewUserStartsWithTheDefaultValuesOfTheDataDictionary()
    {
        var user = new User();

        Assert.Equal(UserRole.User, user.Role);
        Assert.False(user.IsEmailConfirmed);
        Assert.False(user.IsTwoFactorEnabled);
        Assert.Null(user.EncryptedTotpSecret);
        Assert.Equal(NotificationChannel.Email, user.NotificationChannel);
        Assert.Equal(0, user.FailedLoginAttemptCount);
        Assert.Equal(0, user.LockoutCount);
        Assert.Null(user.LockoutEndsAt);
        Assert.Null(user.DeletionRequestedAt);
    }

    // SS §4 : chaque identifiant est un UUID aléatoire, jamais vide et jamais réutilisé.
    [Fact]
    public void EveryNewEntityReceivesItsOwnRandomUniqueIdentifier()
    {
        var firstUser = new User();
        var secondUser = new User();

        Assert.NotEqual(Guid.Empty, firstUser.Id);
        Assert.NotEqual(firstUser.Id, secondUser.Id);
    }

    // DD §5.1 : une nouvelle connexion bancaire est active.
    [Fact]
    public void NewBankConnectionStartsInTheActiveStatus()
    {
        var bankConnection = new BankConnection();

        Assert.Equal(BankConnectionStatus.Active, bankConnection.Status);
    }

    // DD §5.3 : une transaction n'est un transfert interne que si l'utilisateur la marque ainsi.
    [Fact]
    public void NewBankTransactionIsNotAnInternalTransferByDefault()
    {
        var bankTransaction = new BankTransaction();

        Assert.False(bankTransaction.IsInternalTransfer);
    }

    // DD §6.1 : seule la catégorie « Autres » a cette marque.
    [Fact]
    public void NewCategoryIsNotTheOtherCategoryByDefault()
    {
        var category = new Category();

        Assert.False(category.IsOtherCategory);
    }

    // DD §8 : une nouvelle suggestion est proposée, sans date de changement d'état.
    [Fact]
    public void NewSavingsSuggestionStartsInTheProposedStatus()
    {
        var savingsSuggestion = new SavingsSuggestion();

        Assert.Equal(SuggestionStatus.Proposed, savingsSuggestion.Status);
        Assert.Null(savingsSuggestion.StatusChangedAt);
    }

    // DD §9.2 : une alerte est activée tant que l'utilisateur ne la désactive pas.
    [Fact]
    public void NewAlertPreferenceKeepsTheAlertEnabledByDefault()
    {
        var alertPreference = new AlertPreference();

        Assert.True(alertPreference.IsEnabled);
    }

    // DD §10.1 : une nouvelle demande de support est envoyée.
    [Fact]
    public void NewSupportRequestStartsInTheSentStatus()
    {
        var supportRequest = new SupportRequest();

        Assert.Equal(SupportRequestStatus.Sent, supportRequest.Status);
    }
}
