using System.Text;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Transactions;

// Cas d'utilisation : l'utilisateur connecté cherche une opération par libellé, montant ou date (GR Comptes),
// et peut aussi filtrer par compte et par catégorie. Les plus récentes viennent d'abord, 50 par page.
public class TransactionSearchService
{
    public const int PageSize = 50;
    public const int MaximumPage = 10_000;

    private readonly ITransactionRepository transactionRepository;

    // Reçoit l'accès aux opérations.
    public TransactionSearchService(ITransactionRepository transactionRepository)
    {
        this.transactionRepository = transactionRepository;
    }

    // Vérifie les critères, puis donne la page demandée. Le texte cherché perd ses espaces autour ; vide, il ne filtre rien.
    // Le montant est cherché sans son signe : « 12,50 € » trouve une dépense comme un revenu de 12,50 €.
    public async Task<TransactionSearchResult> SearchAsync(
        Guid userId,
        string? typedText,
        int? amountInCents,
        DateOnly? from,
        DateOnly? to,
        Guid? accountId,
        Guid? categoryId,
        int page)
    {
        var text = NormalizeText(typedText);

        if (text != null && text.EnumerateRunes().Count() > TransactionRules.MaximumLabelLength)
        {
            return new TransactionSearchResult.TextTooLong();
        }

        if (amountInCents != null && (amountInCents < 1 || amountInCents > TransactionRules.MaximumAmountInCents))
        {
            return new TransactionSearchResult.AmountBreaksRules();
        }

        if (from != null && to != null && to < from)
        {
            return new TransactionSearchResult.DatesReversed();
        }

        if (page < 1 || page > MaximumPage)
        {
            return new TransactionSearchResult.PageBreaksRules();
        }

        var search = new TransactionSearch(text, amountInCents, from, to, accountId, categoryId, page, PageSize);

        return new TransactionSearchResult.Found { Page = await transactionRepository.SearchAsync(userId, search) };
    }

    // Met le texte en Unicode NFC, comme les libellés enregistrés, et retire ses espaces autour. Un texte vide ne filtre rien.
    // .NET refuse de normaliser un texte mal formé : il est alors gardé tel quel, et ne trouvera simplement rien.
    private static string? NormalizeText(string? typedText)
    {
        if (string.IsNullOrWhiteSpace(typedText))
        {
            return null;
        }

        try
        {
            return typedText.Normalize(NormalizationForm.FormC).Trim();
        }
        catch (ArgumentException)
        {
            return typedText.Trim();
        }
    }
}
