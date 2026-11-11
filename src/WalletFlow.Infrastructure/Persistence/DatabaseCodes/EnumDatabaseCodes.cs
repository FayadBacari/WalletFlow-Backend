using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit les valeurs d'une liste du code C# en codes d'une colonne de la base, et inversement.
// Chaque liste donne seulement sa table de codes : la traduction dans les deux sens est écrite ici, une seule fois.
public sealed class EnumDatabaseCodes<TEnum>
    where TEnum : struct, Enum
{
    private readonly Dictionary<TEnum, string> codeByValue;
    private readonly Dictionary<string, TEnum> valueByCode;

    // Convertisseur utilisé par EF Core pour lire et écrire la colonne.
    public ValueConverter<TEnum, string> Converter { get; }

    // Garde la table de codes et refuse, dès le démarrage, une table incomplète ou avec un code en double.
    public EnumDatabaseCodes(Dictionary<TEnum, string> givenCodes)
    {
        codeByValue = new Dictionary<TEnum, string>();
        valueByCode = new Dictionary<string, TEnum>();

        foreach (TEnum value in Enum.GetValues<TEnum>())
        {
            bool hasCode = givenCodes.TryGetValue(
                value, out string? code);
            if (!hasCode || string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException(
                    $"La valeur {value} de la liste {typeof(TEnum).Name} n'a pas de code de base de données.");
            }

            if (valueByCode.ContainsKey(code))
            {
                throw new InvalidOperationException(
                    $"Le code {code} est donné à deux valeurs de la liste {typeof(TEnum).Name}.");
            }

            codeByValue.Add(value, code);
            valueByCode.Add(code, value);
        }

        Converter = new ValueConverter<TEnum, string>(
            value => ToCode(value),
            code => ToValue(code));
    }

    // Donne le code enregistré en base pour une valeur de la liste.
    public string ToCode(TEnum value)
    {
        if (codeByValue.TryGetValue(
                value, out string? code))
        {
            return code;
        }

        throw new ArgumentOutOfRangeException(nameof(value));
    }

    // Donne la valeur de la liste qui correspond à un code lu en base.
    public TEnum ToValue(string code)
    {
        if (valueByCode.TryGetValue(
                code, out TEnum value))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(nameof(code));
    }
}
