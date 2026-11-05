using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Security;

// Garde en mémoire les connexions qui attendent leur code, 5 minutes au plus (SS §2).
// Elles disparaissent si l'API redémarre : il suffit alors de se reconnecter.
public class InMemoryLoginsWaitingForTheirCodeStore : ILoginsWaitingForTheirCodeStore
{
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;
    private readonly Dictionary<string, WaitingLoginKeptInMemory> waitingLoginsKeptByTheHashOfTheirToken = new Dictionary<string, WaitingLoginKeptInMemory>();
    private readonly object lockThatProtectsTheWaitingLogins = new object();

    // Le nombre de connexions gardées en mémoire : il montre que cette mémoire ne grossit pas sans fin.
    public int NumberOfWaitingLoginsKeptInMemory
    {
        get
        {
            lock (lockThatProtectsTheWaitingLogins)
            {
                return waitingLoginsKeptByTheHashOfTheirToken.Count;
            }
        }
    }

    // Reçoit l'horloge qui mesure la durée de chaque attente.
    public InMemoryLoginsWaitingForTheirCodeStore(TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Garde une nouvelle connexion en attente, et oublie celles qui ont expiré, pour que la mémoire ne grossisse pas.
    public void SaveNewLoginWaitingForItsCode(string hashOfTheTokenOfTheWaitingLogin, Guid identifierOfTheAccountLoggingIn, string secureHashOfThePasswordJustChecked)
    {
        var currentDateOfTheNewWaitingLogin = clockThatGivesTheCurrentDateAndTime.GetUtcNow();

        lock (lockThatProtectsTheWaitingLogins)
        {
            RemoveTheWaitingLoginsThatHaveExpired(currentDateOfTheNewWaitingLogin);

            waitingLoginsKeptByTheHashOfTheirToken[hashOfTheTokenOfTheWaitingLogin] = new WaitingLoginKeptInMemory
            {
                IdentifierOfTheAccountLoggingIn = identifierOfTheAccountLoggingIn,
                SecureHashOfThePasswordJustChecked = secureHashOfThePasswordJustChecked,
                DateOfTheEndOfTheWait = currentDateOfTheNewWaitingLogin + TwoFactorAuthenticationRules.MaximumDurationOfLoginWaitingForItsCode
            };
        }
    }

    // Compte un essai de plus, sous le verrou : chaque requête simultanée reçoit donc son propre numéro d'essai.
    public LoginWaitingForItsCode? TryToCountOneCodeAttemptOfThisWaitingLogin(string hashOfTheTokenOfTheWaitingLogin)
    {
        var currentDateOfTheCodeAttempt = clockThatGivesTheCurrentDateAndTime.GetUtcNow();

        lock (lockThatProtectsTheWaitingLogins)
        {
            if (!waitingLoginsKeptByTheHashOfTheirToken.TryGetValue(hashOfTheTokenOfTheWaitingLogin, out var waitingLoginOfTheTokenFoundInMemory))
            {
                return null;
            }

            if (waitingLoginOfTheTokenFoundInMemory.DateOfTheEndOfTheWait <= currentDateOfTheCodeAttempt
                || waitingLoginOfTheTokenFoundInMemory.NumberOfCodesTriedSoFar >= TwoFactorAuthenticationRules.MaximumNumberOfCodesTriedDuringOneLogin)
            {
                return null;
            }

            waitingLoginOfTheTokenFoundInMemory.NumberOfCodesTriedSoFar = waitingLoginOfTheTokenFoundInMemory.NumberOfCodesTriedSoFar + 1;

            return new LoginWaitingForItsCode
            {
                IdentifierOfTheAccountLoggingIn = waitingLoginOfTheTokenFoundInMemory.IdentifierOfTheAccountLoggingIn,
                SecureHashOfThePasswordJustChecked = waitingLoginOfTheTokenFoundInMemory.SecureHashOfThePasswordJustChecked,
                NumberOfCodesTriedSoFar = waitingLoginOfTheTokenFoundInMemory.NumberOfCodesTriedSoFar
            };
        }
    }

    // Oublie une connexion en attente. Ne fait rien si elle n'existe plus.
    public void RemoveThisLoginWaitingForItsCode(string hashOfTheTokenOfTheWaitingLogin)
    {
        lock (lockThatProtectsTheWaitingLogins)
        {
            waitingLoginsKeptByTheHashOfTheirToken.Remove(hashOfTheTokenOfTheWaitingLogin);
        }
    }

    // Oublie les connexions dont l'attente est finie.
    private void RemoveTheWaitingLoginsThatHaveExpired(DateTimeOffset currentDateOfTheNewWaitingLogin)
    {
        var hashesOfTheWaitingLoginsThatHaveExpired = new List<string>();

        foreach (var waitingLoginInMemoryBeingCheckedNow in waitingLoginsKeptByTheHashOfTheirToken)
        {
            if (waitingLoginInMemoryBeingCheckedNow.Value.DateOfTheEndOfTheWait <= currentDateOfTheNewWaitingLogin)
            {
                hashesOfTheWaitingLoginsThatHaveExpired.Add(waitingLoginInMemoryBeingCheckedNow.Key);
            }
        }

        foreach (var hashOfTheWaitingLoginToForget in hashesOfTheWaitingLoginsThatHaveExpired)
        {
            waitingLoginsKeptByTheHashOfTheirToken.Remove(hashOfTheWaitingLoginToForget);
        }
    }

    // Une connexion en attente, avec son nombre d'essais de code et la fin de son attente.
    private class WaitingLoginKeptInMemory
    {
        public required Guid IdentifierOfTheAccountLoggingIn { get; init; }
        public required string SecureHashOfThePasswordJustChecked { get; init; }
        public required DateTimeOffset DateOfTheEndOfTheWait { get; init; }
        public int NumberOfCodesTriedSoFar { get; set; }
    }
}
