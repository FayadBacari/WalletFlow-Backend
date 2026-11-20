using System.Globalization;

namespace WalletFlow.Api.Configuration;

// Le nombre de calculs Argon2id faits en même temps (SS §2), réglé par « Argon2:ConcurrentComputations ». Chaque calcul prend
// 32 Mio de mémoire, et au plus 4 attendent leur tour par calcul permis : au-delà, une connexion reçoit « serveur occupé ». Le
// bon nombre dépend du serveur, de sa mémoire et des connexions qui arrivent en même temps : sans réglage, c'est le nombre de
// cœurs. Une valeur illisible, ou hors des bornes, empêche l'API de démarrer.
public static class ArgonConfiguration
{
    public const string ConcurrentComputationsSetting = "Argon2:ConcurrentComputations";

    // Au-delà, les calculs à la fois prendraient plus de 2 Gio de mémoire : la limite ne protégerait plus le serveur.
    public const int MaximumConcurrentComputations = 64;

    // Lit le nombre de calculs à la fois, ou donne le nombre de cœurs sans réglage.
    public static int ReadConcurrentComputations(IConfiguration configuration)
    {
        var settingText = configuration[ConcurrentComputationsSetting];

        if (string.IsNullOrWhiteSpace(settingText))
        {
            return Environment.ProcessorCount;
        }

        if (!int.TryParse(settingText, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count < 1
            || count > MaximumConcurrentComputations)
        {
            throw new InvalidOperationException(
                "Le réglage « " + ConcurrentComputationsSetting + " » doit être un nombre entier de 1 à " + MaximumConcurrentComputations
                + " : le nombre de calculs Argon2id faits en même temps, chacun avec 32 Mio de mémoire.");
        }

        return count;
    }
}
