namespace WalletFlow.Tests.Fakes;

// Remplace l'horloge dans les tests : le temps n'avance que quand un test le demande.
public class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset CurrentDate { get; private set; } = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    // Donne la date de la fausse horloge (nom imposé par le framework).
    public override DateTimeOffset GetUtcNow()
    {
        return CurrentDate;
    }

    // Fait avancer la fausse horloge.
    public void Advance(TimeSpan duration)
    {
        CurrentDate = CurrentDate + duration;
    }
}
