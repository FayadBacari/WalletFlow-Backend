using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Security;

// Garde en mémoire les secrets qui attendent le premier code, 10 minutes au plus : le MPD interdit de les écrire en base avant l'activation.
// Ils disparaissent si l'API redémarre : il suffit alors de recommencer l'activation.
public class InMemorySecretsWaitingForActivationStore : ISecretsWaitingForActivationStore
{
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;
    private readonly Dictionary<Guid, SecretKeptInMemory> secretsKeptByTheIdentifierOfTheirAccount = new Dictionary<Guid, SecretKeptInMemory>();
    private readonly object lockThatProtectsTheWaitingSecrets = new object();

    // Le nombre de secrets gardés en mémoire : il montre que cette mémoire ne grossit pas sans fin.
    public int NumberOfWaitingSecretsKeptInMemory
    {
        get
        {
            lock (lockThatProtectsTheWaitingSecrets)
            {
                return secretsKeptByTheIdentifierOfTheirAccount.Count;
            }
        }
    }

    // Reçoit l'horloge qui mesure la durée de chaque attente.
    public InMemorySecretsWaitingForActivationStore(TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Garde le secret de ce compte, et oublie ceux qui ont expiré, pour que la mémoire ne grossisse pas.
    public void SaveSecretWaitingForActivationOfThisAccount(Guid identifierOfTheAccountBeingActivated, string secretSharedWithTheAuthenticatorApplication)
    {
        var currentDateOfTheNewWaitingSecret = clockThatGivesTheCurrentDateAndTime.GetUtcNow();

        lock (lockThatProtectsTheWaitingSecrets)
        {
            RemoveTheWaitingSecretsThatHaveExpired(currentDateOfTheNewWaitingSecret);

            secretsKeptByTheIdentifierOfTheirAccount[identifierOfTheAccountBeingActivated] = new SecretKeptInMemory
            {
                SecretOfTheAuthenticatorApplication = secretSharedWithTheAuthenticatorApplication,
                DateOfTheEndOfTheWait = currentDateOfTheNewWaitingSecret + TwoFactorAuthenticationRules.MaximumDurationOfSecretWaitingForActivation
            };
        }
    }

    // Donne le secret de ce compte. Renvoie null s'il n'y en a pas, ou s'il a expiré.
    public string? FindSecretWaitingForActivationOfThisAccount(Guid identifierOfTheAccountBeingActivated)
    {
        var currentDateOfTheSecretSearch = clockThatGivesTheCurrentDateAndTime.GetUtcNow();

        lock (lockThatProtectsTheWaitingSecrets)
        {
            if (!secretsKeptByTheIdentifierOfTheirAccount.TryGetValue(identifierOfTheAccountBeingActivated, out var secretWaitingForActivationFoundInMemory)
                || secretWaitingForActivationFoundInMemory.DateOfTheEndOfTheWait <= currentDateOfTheSecretSearch)
            {
                return null;
            }

            return secretWaitingForActivationFoundInMemory.SecretOfTheAuthenticatorApplication;
        }
    }

    // Oublie le secret de ce compte. Ne fait rien s'il n'y en a plus.
    public void RemoveSecretWaitingForActivationOfThisAccount(Guid identifierOfTheAccountBeingActivated)
    {
        lock (lockThatProtectsTheWaitingSecrets)
        {
            secretsKeptByTheIdentifierOfTheirAccount.Remove(identifierOfTheAccountBeingActivated);
        }
    }

    // Oublie les secrets dont l'attente est finie.
    private void RemoveTheWaitingSecretsThatHaveExpired(DateTimeOffset currentDateOfTheNewWaitingSecret)
    {
        var accountsWhoseWaitingSecretHasExpired = new List<Guid>();

        foreach (var waitingSecretInMemoryBeingCheckedNow in secretsKeptByTheIdentifierOfTheirAccount)
        {
            if (waitingSecretInMemoryBeingCheckedNow.Value.DateOfTheEndOfTheWait <= currentDateOfTheNewWaitingSecret)
            {
                accountsWhoseWaitingSecretHasExpired.Add(waitingSecretInMemoryBeingCheckedNow.Key);
            }
        }

        foreach (var accountWhoseWaitingSecretIsForgotten in accountsWhoseWaitingSecretHasExpired)
        {
            secretsKeptByTheIdentifierOfTheirAccount.Remove(accountWhoseWaitingSecretIsForgotten);
        }
    }

    // Un secret en attente, avec la fin de son attente.
    private class SecretKeptInMemory
    {
        public required string SecretOfTheAuthenticatorApplication { get; init; }
        public required DateTimeOffset DateOfTheEndOfTheWait { get; init; }
    }
}
