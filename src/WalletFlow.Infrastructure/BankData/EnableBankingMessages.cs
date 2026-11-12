namespace WalletFlow.Infrastructure.BankData;

// Les messages JSON de l'API d'Enable Banking, tels que sa référence les décrit. Ils restent ensemble dans ce fichier :
// ce sont seulement les formes des messages, sans aucune règle. Les noms passent en snake_case (voir EnableBankingClient).
// Chaque champ reçu peut manquer : rien de ce que le prestataire envoie n'est cru sans être vérifié (SS §5).

// Réponse de GET /aspsps : les banques.
internal sealed record AspspListResponse(List<AspspData>? Aspsps);

// Une banque : son nom, son pays, et la plus longue autorisation qu'elle accepte, en secondes.
internal sealed record AspspData(string? Name, string? Country, long? MaximumConsentValidity);

// Corps de POST /auth : commence une autorisation.
internal sealed record StartAuthorizationBody(AccessBody Access, AspspBody Aspsp, string State, string RedirectUrl, string PsuType);

// La durée demandée pour l'autorisation.
internal sealed record AccessBody(DateTimeOffset ValidUntil);

// La banque choisie.
internal sealed record AspspBody(string Name, string Country);

// Réponse de POST /auth : l'adresse de la page de la banque.
internal sealed record StartAuthorizationResponse(string? Url);

// Corps de POST /sessions : le code renvoyé par la banque.
internal sealed record AuthorizeSessionBody(string Code);

// Réponse de POST /sessions : la nouvelle session, ses comptes et la fin de l'autorisation.
internal sealed record AuthorizeSessionResponse(string? SessionId, List<AccountResource>? Accounts, AccessData? Access);

// La fin de l'autorisation accordée.
internal sealed record AccessData(DateTimeOffset? ValidUntil);

// Un compte d'une nouvelle session. Le champ « name » d'Enable Banking est le nom du titulaire : il n'est pas lu (SS §7).
internal sealed record AccountResource(string? Uid, string? IdentificationHash, string? Currency, string? Details, string? Product);

// Réponse de GET /sessions/{id} : l'état de la session et ses comptes.
internal sealed record SessionResponse(string? Status, List<SessionAccountData>? AccountsData);

// Un compte d'une session déjà autorisée.
internal sealed record SessionAccountData(string? Uid, string? IdentificationHash);

// Réponse de GET /accounts/{id}/balances : les soldes du compte.
internal sealed record BalancesResponse(List<BalanceResource>? Balances);

// Un solde : son montant et son type (code ISO 20022).
internal sealed record BalanceResource(AmountData? BalanceAmount, string? BalanceType);

// Un montant : sa devise (code ISO 4217) et sa valeur écrite en texte, avec un point pour les décimales.
internal sealed record AmountData(string? Currency, string? Amount);

// Réponse d'erreur : son message et son code d'erreur, comme « EXPIRED_SESSION ».
internal sealed record ErrorResponse(string? Message, string? Error);
