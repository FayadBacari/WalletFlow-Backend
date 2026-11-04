namespace WalletFlow.Tests.Fakes;

// Remplace l'horloge dans les tests : le temps n'avance que quand un test le demande.
public class FakeTimeProviderForTests : TimeProvider
{
    public DateTimeOffset CurrentDateOfTheFakeClock { get; private set; } = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    // Donne la date de la fausse horloge (nom imposé par le framework).
    public override DateTimeOffset GetUtcNow()
    {
        return CurrentDateOfTheFakeClock;
    }

    // Fait avancer la fausse horloge.
    public void MoveFakeClockForwardByThisDuration(TimeSpan durationToAddToTheFakeClock)
    {
        CurrentDateOfTheFakeClock = CurrentDateOfTheFakeClock + durationToAddToTheFakeClock;
    }
}
