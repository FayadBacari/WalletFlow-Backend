using System.Text.Json.Serialization;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour créer un sac (GR Sacs) : son nom, et un objectif s'il le souhaite, avec un montant à atteindre
// en centimes et une date facultative écrite comme 2027-06-30. Seuls ces champs sont lus (SS §4).
public class BagCreationRequest
{
    public string? Name { get; set; }
    public int? TargetAmountInCents { get; set; }
    public DateOnly? TargetDate { get; set; }
}

// Données envoyées pour renommer un sac (GR Sacs). Seul ce champ est lu (SS §4).
public class BagNameRequest
{
    public string? Name { get; set; }
}

// Données envoyées pour fixer l'objectif d'un sac, ou l'enlever avec deux null (GR Sacs). Les deux champs doivent être écrits,
// même à null : une requête vide n'enlève jamais un objectif par erreur. Seuls ces champs sont lus (SS §4).
public class BagGoalRequest
{
    [JsonRequired]
    public int? TargetAmountInCents { get; set; }

    [JsonRequired]
    public DateOnly? TargetDate { get; set; }
}

// Données envoyées pour déplacer de l'argent (GR Sacs). Un sac null est l'argent disponible. Les trois champs doivent être écrits,
// même à null : une requête incomplète ne prend jamais l'argent disponible par erreur. Seuls ces champs sont lus (SS §4).
public class BagMovementRequest
{
    [JsonRequired]
    public Guid? SourceBagId { get; set; }

    [JsonRequired]
    public Guid? DestinationBagId { get; set; }

    [JsonRequired]
    public int AmountInCents { get; set; }
}

// La page de l'historique d'un sac, lue dans l'adresse. Elle commence à 1.
public class BagHistoryRequest
{
    public int? Page { get; set; }
}
