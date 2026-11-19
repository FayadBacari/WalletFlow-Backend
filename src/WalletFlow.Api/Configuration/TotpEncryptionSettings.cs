using System.Globalization;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Api.Configuration;

// Les clés qui chiffrent les secrets de double authentification (SS §2 et §7), lues dans les secrets, jamais dans le code
// ni dans Git : la clé actuelle, son numéro (1 si rien n'est dit), et les anciennes clés rangées par numéro, qui ne servent
// plus qu'à déchiffrer. Sans clé actuelle, aucun compte avec double authentification ne pourrait utiliser son application ;
// un réglage absent ou illisible empêche donc l'API de démarrer.
public sealed class TotpEncryptionSettings
{
    public required int CurrentKeyId { get; init; }
    public required string CurrentKey { get; init; }
    public required IReadOnlyDictionary<int, string> PreviousKeys { get; init; }

    // Lit les clés. Les messages d'erreur ne citent jamais la valeur d'une clé.
    public static TotpEncryptionSettings Read(IConfiguration configuration)
    {
        var currentKey = configuration[AesGcmTotpSecretProtector.CurrentKeySetting];
        if (string.IsNullOrWhiteSpace(currentKey))
        {
            throw new InvalidOperationException(
                "Le réglage « " + AesGcmTotpSecretProtector.CurrentKeySetting + " » est absent. En développement, ajoute-le avec dotnet user-secrets.");
        }

        var currentKeyId = AesGcmTotpSecretProtector.FirstKeyId;
        var currentKeyIdText = configuration[AesGcmTotpSecretProtector.CurrentKeyIdSetting];
        if (!string.IsNullOrWhiteSpace(currentKeyIdText) && !TryReadKeyId(currentKeyIdText, out currentKeyId))
        {
            throw new InvalidOperationException(
                "Le réglage « " + AesGcmTotpSecretProtector.CurrentKeyIdSetting + " » doit être un entier positif.");
        }

        var previousKeys = new Dictionary<int, string>();
        foreach (var previousKey in configuration.GetSection(AesGcmTotpSecretProtector.PreviousKeysSetting).GetChildren())
        {
            if (!TryReadKeyId(previousKey.Key, out var keyId))
            {
                throw new InvalidOperationException(
                    "Dans « " + AesGcmTotpSecretProtector.PreviousKeysSetting + " », chaque ancienne clé porte son numéro, un entier positif.");
            }

            previousKeys[keyId] = previousKey.Value ?? "";
        }

        return new TotpEncryptionSettings { CurrentKeyId = currentKeyId, CurrentKey = currentKey, PreviousKeys = previousKeys };
    }

    // Lit un numéro de clé : des chiffres seulement, sans signe ni espace.
    private static bool TryReadKeyId(string text, out int keyId)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out keyId) && keyId >= AesGcmTotpSecretProtector.FirstKeyId;
    }
}
