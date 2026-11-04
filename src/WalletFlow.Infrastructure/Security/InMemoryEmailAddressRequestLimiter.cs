using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Security;

// Compte en mémoire les demandes par adresse e-mail : 3 par heure (SS §6).
// Les compteurs repartent à zéro si l'API redémarre, comme les limites par adresse IP.
public class InMemoryEmailAddressRequestLimiter : IEmailAddressRequestLimiter
{
    public const int MaximumNumberOfRequestsPerEmailAddress = 3;
    public static readonly TimeSpan DurationOfTheWindowCountingTheRequests = TimeSpan.FromHours(1);

    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;
    private readonly Dictionary<string, RequestCounterOfOneEmailAddress> requestCountersKeptByEmailAddress = new Dictionary<string, RequestCounterOfOneEmailAddress>();
    private readonly object lockThatProtectsTheRequestCounters = new object();

    // Reçoit l'horloge qui mesure la durée de chaque compteur.
    public InMemoryEmailAddressRequestLimiter(TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Compte une demande de plus pour cette adresse. Renvoie faux si la limite est déjà atteinte.
    public bool TryToCountNewRequestForThisEmailAddress(string emailAddressInItsStoredForm)
    {
        var currentDateOfTheRequestBeingCounted = clockThatGivesTheCurrentDateAndTime.GetUtcNow();

        lock (lockThatProtectsTheRequestCounters)
        {
            RemoveTheCountersWhoseWindowHasEnded(currentDateOfTheRequestBeingCounted);

            if (!requestCountersKeptByEmailAddress.TryGetValue(emailAddressInItsStoredForm, out var requestCounterOfThisEmailAddress))
            {
                requestCountersKeptByEmailAddress[emailAddressInItsStoredForm] = new RequestCounterOfOneEmailAddress
                {
                    DateOfTheFirstRequest = currentDateOfTheRequestBeingCounted,
                    NumberOfRequests = 1
                };
                return true;
            }

            if (requestCounterOfThisEmailAddress.NumberOfRequests >= MaximumNumberOfRequestsPerEmailAddress)
            {
                return false;
            }

            requestCounterOfThisEmailAddress.NumberOfRequests = requestCounterOfThisEmailAddress.NumberOfRequests + 1;
            return true;
        }
    }

    // Oublie les adresses dont la période de comptage est finie, pour que la mémoire ne grossisse pas.
    private void RemoveTheCountersWhoseWindowHasEnded(DateTimeOffset currentDateOfTheRequestBeingCounted)
    {
        var emailAddressesWhoseWindowHasEnded = new List<string>();

        foreach (var requestCounterOfAddressBeingChecked in requestCountersKeptByEmailAddress)
        {
            if (currentDateOfTheRequestBeingCounted - requestCounterOfAddressBeingChecked.Value.DateOfTheFirstRequest >= DurationOfTheWindowCountingTheRequests)
            {
                emailAddressesWhoseWindowHasEnded.Add(requestCounterOfAddressBeingChecked.Key);
            }
        }

        foreach (var emailAddressToForgetFromTheCounters in emailAddressesWhoseWindowHasEnded)
        {
            requestCountersKeptByEmailAddress.Remove(emailAddressToForgetFromTheCounters);
        }
    }

    // Compteur d'une adresse e-mail pendant sa période de comptage.
    private class RequestCounterOfOneEmailAddress
    {
        public DateTimeOffset DateOfTheFirstRequest { get; init; }
        public int NumberOfRequests { get; set; }
    }
}
