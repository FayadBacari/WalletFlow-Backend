using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Infrastructure.Security;

// Se souvient en mémoire du dernier code utilisé par chaque compte : un code ne sert qu'une seule fois (SS §2).
// Si l'API redémarre, un code déjà utilisé peut resservir pendant le peu de temps où il reste valable.
public class InMemoryUsedAuthenticatorCodesRegister : IUsedAuthenticatorCodesRegister
{
    // Un code reste valable 90 secondes au plus après le moment où il a servi : 2 minutes de mémoire suffisent donc.
    public static readonly TimeSpan DurationOfTheMemoryOfOneUsedCode = TimeSpan.FromMinutes(2);

    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;
    private readonly Dictionary<Guid, UsedCodeKeptInMemory> usedCodesKeptByTheIdentifierOfTheirAccount = new Dictionary<Guid, UsedCodeKeptInMemory>();
    private readonly object lockThatProtectsTheUsedCodes = new object();

    // Le nombre de codes gardés en mémoire : il montre que cette mémoire ne grossit pas sans fin.
    public int NumberOfUsedCodesKeptInMemory
    {
        get
        {
            lock (lockThatProtectsTheUsedCodes)
            {
                return usedCodesKeptByTheIdentifierOfTheirAccount.Count;
            }
        }
    }

    // Reçoit l'horloge qui mesure la durée de cette mémoire.
    public InMemoryUsedAuthenticatorCodesRegister(TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Tout se passe sous le verrou : de deux requêtes simultanées avec le même code, une seule peut réussir.
    public bool TryToRecordThisPeriodAsUsedByThisAccount(Guid identifierOfTheAccountUsingTheCode, long numberOfThePeriodOfTheCode)
    {
        var currentDateOfTheCodeBeingUsed = clockThatGivesTheCurrentDateAndTime.GetUtcNow();

        lock (lockThatProtectsTheUsedCodes)
        {
            RemoveTheUsedCodesThatCanNoLongerBeValid(currentDateOfTheCodeBeingUsed);

            if (usedCodesKeptByTheIdentifierOfTheirAccount.TryGetValue(identifierOfTheAccountUsingTheCode, out var lastCodeUsedByThisAccount)
                && lastCodeUsedByThisAccount.NumberOfThePeriodOfTheCode >= numberOfThePeriodOfTheCode)
            {
                return false;
            }

            usedCodesKeptByTheIdentifierOfTheirAccount[identifierOfTheAccountUsingTheCode] = new UsedCodeKeptInMemory
            {
                NumberOfThePeriodOfTheCode = numberOfThePeriodOfTheCode,
                DateWhenTheCodeWasUsed = currentDateOfTheCodeBeingUsed
            };
            return true;
        }
    }

    // Oublie les codes trop vieux pour être encore valables, pour que la mémoire ne grossisse pas.
    private void RemoveTheUsedCodesThatCanNoLongerBeValid(DateTimeOffset currentDateOfTheCodeBeingUsed)
    {
        var accountsWhoseUsedCodeIsTooOld = new List<Guid>();

        foreach (var usedCodeOfOneAccountBeingChecked in usedCodesKeptByTheIdentifierOfTheirAccount)
        {
            if (currentDateOfTheCodeBeingUsed - usedCodeOfOneAccountBeingChecked.Value.DateWhenTheCodeWasUsed >= DurationOfTheMemoryOfOneUsedCode)
            {
                accountsWhoseUsedCodeIsTooOld.Add(usedCodeOfOneAccountBeingChecked.Key);
            }
        }

        foreach (var accountWhoseUsedCodeIsForgotten in accountsWhoseUsedCodeIsTooOld)
        {
            usedCodesKeptByTheIdentifierOfTheirAccount.Remove(accountWhoseUsedCodeIsForgotten);
        }
    }

    // Le dernier code utilisé par un compte.
    private class UsedCodeKeptInMemory
    {
        public required long NumberOfThePeriodOfTheCode { get; init; }
        public required DateTimeOffset DateWhenTheCodeWasUsed { get; init; }
    }
}
