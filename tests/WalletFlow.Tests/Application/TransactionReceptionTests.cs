using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Transactions;

namespace WalletFlow.Tests.Application;

// Vérifie le contrôle des opérations données par la banque et leur identifiant (GR Comptes, DD §5.3).
public class TransactionReceptionTests
{
    private static readonly DateOnly Day = new DateOnly(2026, 11, 10);

    // Crée une opération comptabilisée en euros, à modifier par le test.
    private static ProvidedTransaction Booked(string label, long amountInCents, string? reference = null, DateOnly? date = null)
    {
        return new ProvidedTransaction(reference, true, date ?? Day, amountInCents, "EUR", label);
    }

    // Seules les opérations comptabilisées, en euros, datées à partir de 2000 et d'un montant non nul et permis sont gardées.
    [Fact]
    public void OnlyValidBookedEuroOperationsAreKept()
    {
        var received = TransactionReception.Receive(new List<ProvidedTransaction>
        {
            Booked("GARDEE", -1250),
            Booked("EN ATTENTE", -100) with { IsBooked = false },
            Booked("EN DOLLARS", -100) with { Currency = "USD" },
            Booked("SANS DATE", -100) with { Date = null },
            Booked("TROP ANCIENNE", -100) with { Date = new DateOnly(1999, 12, 31) },
            Booked("MONTANT ILLISIBLE", -100) with { AmountInCents = null },
            Booked("MONTANT NUL", 0),
            Booked("TROP GRANDE", 1_000_000_001),
            Booked("TROP PETITE", -1_000_000_001)
        });

        var kept = Assert.Single(received);
        Assert.Equal("GARDEE", kept.Label);
        Assert.Equal(-1250, kept.AmountInCents);
        Assert.Equal(Day, kept.Date);
    }

    // SS §5 : le libellé reçu est nettoyé avant d'être gardé.
    [Fact]
    public void LabelIsCleaned()
    {
        var received = TransactionReception.Receive(new List<ProvidedTransaction> { Booked("  CB" + (char)0x0A + "CARREFOUR  ", -100) });

        Assert.Equal("CB CARREFOUR", Assert.Single(received).Label);
    }

    // DD §5.3 : la même opération relue donne le même identifiant : elle ne sera jamais ajoutée deux fois.
    [Fact]
    public void SameOperationReadAgainHasTheSameIdentifier()
    {
        var firstReading = TransactionReception.Receive(new List<ProvidedTransaction> { Booked("CB CARREFOUR", -100), Booked("CB SNCF", -200, "REF-1") });
        var secondReading = TransactionReception.Receive(new List<ProvidedTransaction> { Booked("CB SNCF", -200, "REF-1"), Booked("CB CARREFOUR", -100) });

        Assert.Equal(
            firstReading.Select(transaction => transaction.ExternalId).OrderBy(id => id),
            secondReading.Select(transaction => transaction.ExternalId).OrderBy(id => id));
    }

    // Deux cafés identiques le même jour, sans référence de la banque : deux opérations distinctes, chacune toujours la même.
    [Fact]
    public void IdenticalOperationsOfTheSameDayAreKeptApart()
    {
        var reading = new List<ProvidedTransaction> { Booked("CB CAFE", -250), Booked("CB CAFE", -250) };

        var firstIds = TransactionReception.Receive(reading).Select(transaction => transaction.ExternalId).ToList();
        var secondIds = TransactionReception.Receive(reading).Select(transaction => transaction.ExternalId).ToList();

        Assert.Equal(2, firstIds.Distinct().Count());
        Assert.Equal(firstIds, secondIds);
    }

    // Une banque peut donner la même référence à plusieurs opérations, par exemple un prélèvement chaque mois :
    // la date et le montant les distinguent, et aucune n'écrase l'autre.
    [Fact]
    public void SharedReferenceDoesNotMergeOperations()
    {
        var received = TransactionReception.Receive(new List<ProvidedTransaction>
        {
            Booked("PRLV EDF", -6000, "MANDAT-1", new DateOnly(2026, 9, 5)),
            Booked("PRLV EDF", -6000, "MANDAT-1", new DateOnly(2026, 10, 5)),
            Booked("PRLV EDF", -6100, "MANDAT-1", new DateOnly(2026, 10, 5))
        });

        Assert.Equal(3, received.Select(transaction => transaction.ExternalId).Distinct().Count());
    }

    // Un prélèvement mensuel garde la même référence, le même libellé et le même montant chaque mois : la date distingue
    // les mois, lus dans deux mises à jour différentes. Sinon le second mois prendrait l'identifiant du premier.
    [Fact]
    public void MonthlyOperationGetsANewIdentifierEachMonth()
    {
        var september = new DateOnly(2026, 9, 5);
        var october = new DateOnly(2026, 10, 5);

        var withReference = new List<string>
        {
            TransactionReception.Receive(new List<ProvidedTransaction> { Booked("PRLV NETFLIX", -1399, "MANDAT-1", september) }).Single().ExternalId,
            TransactionReception.Receive(new List<ProvidedTransaction> { Booked("PRLV NETFLIX", -1399, "MANDAT-1", october) }).Single().ExternalId
        };
        var withoutReference = new List<string>
        {
            TransactionReception.Receive(new List<ProvidedTransaction> { Booked("PRLV NETFLIX", -1399, null, september) }).Single().ExternalId,
            TransactionReception.Receive(new List<ProvidedTransaction> { Booked("PRLV NETFLIX", -1399, null, october) }).Single().ExternalId
        };

        Assert.NotEqual(withReference[0], withReference[1]);
        Assert.NotEqual(withoutReference[0], withoutReference[1]);
    }

    // Une opération déjà reçue garde son identifiant quand une autre, de même référence et du même jour, est comptabilisée
    // plus tard et passe devant elle dans la lecture suivante. Sans le montant dans l'empreinte, la nouvelle prendrait
    // l'identifiant de l'ancienne : elle serait perdue, et l'ancienne ajoutée une seconde fois.
    [Fact]
    public void OperationKeepsItsIdentifierWhenAnotherOfTheSameReferenceArrivesLater()
    {
        var first = Booked("PRLV EDF", -6000, "LOT-1");
        var later = Booked("PRLV EDF", -6100, "LOT-1");

        var firstId = TransactionReception.Receive(new List<ProvidedTransaction> { first }).Single().ExternalId;
        var secondReading = TransactionReception.Receive(new List<ProvidedTransaction> { later, first });

        Assert.Equal(-6000, secondReading.Single(transaction => transaction.ExternalId == firstId).AmountInCents);
    }

    // Sans référence, c'est le libellé qui tient ce rôle : une opération déjà reçue garde son identifiant quand une autre,
    // du même jour et du même montant mais d'un autre libellé, passe devant elle dans la lecture suivante.
    [Fact]
    public void OperationWithoutReferenceKeepsItsIdentifierWhenAnotherArrivesLater()
    {
        var first = Booked("CB CAFE DE LA GARE", -250);
        var later = Booked("CB BOULANGERIE", -250);

        var firstId = TransactionReception.Receive(new List<ProvidedTransaction> { first }).Single().ExternalId;
        var secondReading = TransactionReception.Receive(new List<ProvidedTransaction> { later, first });

        Assert.Equal("CB CAFE DE LA GARE", secondReading.Single(transaction => transaction.ExternalId == firstId).Label);
    }

    // Avec une référence, un libellé corrigé par la banque garde le même identifiant ; sans référence, le libellé compte.
    [Fact]
    public void LabelCountsOnlyWithoutReference()
    {
        var withReference = TransactionReception.Receive(new List<ProvidedTransaction> { Booked("CB CARREF", -100, "REF-1"), Booked("CB CARREFOUR", -100, "REF-2") });
        var correctedWithReference = TransactionReception.Receive(new List<ProvidedTransaction> { Booked("CB CARREFOUR MARKET", -100, "REF-1") });
        var withoutReference = TransactionReception.Receive(new List<ProvidedTransaction> { Booked("CB CARREF", -100), Booked("CB CARREFOUR", -100) });

        Assert.Equal(withReference[0].ExternalId, correctedWithReference[0].ExternalId);
        Assert.NotEqual(withoutReference[0].ExternalId, withoutReference[1].ExternalId);
    }

    // DD §5.3 : l'identifiant tient dans les 100 caractères de id_transaction_externe, même pour une très longue référence.
    [Fact]
    public void IdentifierFitsTheColumn()
    {
        var received = TransactionReception.Receive(new List<ProvidedTransaction> { Booked("CB CAFE", -250, new string('R', 5000)) });

        Assert.True(Assert.Single(received).ExternalId.Length <= 100);
    }
}
