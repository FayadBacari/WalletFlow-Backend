# Guide du frontend

Ce guide dit ce que le site doit savoir pour appeler l'API de WalletFlow. Le détail de chaque route (ses champs, sa réponse réussie et tous ses refus) est dans la description OpenAPI (§10).

Il vise le site web : l'application mobile n'existe pas encore (`gestion-rules.md`, Alertes). « SS » désigne la stratégie de sécurité (`security-strategy.md`).


## 1. Même origine et HTTPS

**L'API n'a aucune règle CORS (SS §6).** Le site et l'API doivent donc être servis par la même origine, l'API sous `/api`. Un navigateur refuse de lire les réponses d'une autre origine.

**Les cookies de l'API sont `__Host-`, `Secure`, `HttpOnly` et `Path=/`.** Le navigateur ne les garde qu'en HTTPS, et le JavaScript du site ne peut pas les lire. Il n'a rien à en faire : le navigateur les envoie seul, et `fetch` le fait par défaut pour la même origine.

| Cookie                         | Rôle                                                       | SameSite | Durée dans le navigateur                                                                                  |
|--------------------------------|------------------------------------------------------------|----------|-----------------------------------------------------------------------------------------------------------|
| `__Host-walletflow-session`    | La session                                                 | Lax      | Jusqu'à la fermeture ; le serveur la ferme après 30 minutes sans requête, ou 12 heures après la connexion |
| `__Host-walletflow-device`     | Reconnaître le navigateur à la prochaine connexion         | Lax      | 365 jours                                                                                                 |
| `__Host-walletflow-two-factor` | La connexion qui attend le code de double authentification | Strict   | Jusqu'à la fermeture ; le serveur l'oublie après 5 minutes                                                |
| `__Host-walletflow-csrf`       | La moitié du jeton anti-CSRF gardée par le navigateur      | Strict   | Jusqu'à la fermeture                                                                                      |

**En développement :**
- Lance l'API avec son profil `https` : elle répond sur `https://localhost:7078`. Fais approuver son certificat de développement par ta machine, une seule fois : `dotnet dev-certs https --trust`.
- Le serveur de développement du site relaie `/api` vers `https://localhost:7078`, et sert lui-même le site en HTTPS : un navigateur peut refuser un cookie `Secure` reçu en HTTP.
- Le réglage `PublicWebsiteAddress` donne l'adresse du site dans les e-mails. En développement, il vaut `https://localhost:7078`, l'adresse de l'API. Remplace-le par celle du serveur du site, avec `dotnet user-secrets`, pour que les liens des e-mails ouvrent ses pages (§4).

**En production**, un proxy HTTPS sert le site et relaie `/api` vers l'API (étape 17 du déploiement).


## 2. Connexion et session

1. `POST /api/account/login` avec `{ email, password }`. La réponse donne `{ message, isTwoFactorCodeRequired, isDeletionCancelled }`.
2. Si `isTwoFactorCodeRequired` vaut `true`, la connexion attend le code : `POST /api/account/login/two-factor` avec `{ code }`, le code de l'application d'authentification ou un code de secours.
   - Un code faux répond 400, avec son message sous la clé `code`.
   - Un 401 veut dire que la connexion doit recommencer : le 5e code était faux, l'attente a dépassé 5 minutes, ou le mot de passe ou la double authentification ont changé pendant l'attente.
3. Si `isDeletionCancelled` vaut `true`, cette connexion a annulé une suppression du compte demandée plus tôt : affiche `message`.
4. Demande ensuite le jeton anti-CSRF (§3).

**Les refus de la connexion :**
- 401 : l'adresse e-mail ou le mot de passe est faux ;
- 403 : l'adresse n'est pas encore confirmée. Invite l'utilisateur à la confirmer (`gestion-rules.md`, Authentification).

**La session se ferme** après 30 minutes sans requête, 12 heures après la connexion, ou quand l'utilisateur change ou récupère son mot de passe sur un autre appareil. Toute route répond alors 401 : renvoie vers la page de connexion.

**Déconnexion** : `POST /api/account/logout`, avec le jeton anti-CSRF.


## 3. Jeton anti-CSRF

- Après chaque connexion, `GET /api/account/csrf-token` donne `{ csrfToken }`. Garde-le en mémoire.
- Envoie-le dans l'en-tête `X-CSRF-TOKEN` de chaque `POST`, `PUT` et `DELETE`. L'API ne le lit jamais dans un formulaire (SS §6).
- Le jeton est lié au compte connecté : celui d'un autre compte est refusé. Redemande-le après chaque connexion.
- Sans jeton valable, l'API répond 400, avec une seule erreur sous la clé `csrfToken`. Redemande un jeton, puis réessaie une seule fois. Si le refus revient, affiche son message.

**Les 10 routes appelées sans session n'ont pas besoin du jeton** : l'inscription, la confirmation de l'adresse, la connexion et son code, le mot de passe oublié, le nouveau mot de passe, le déblocage, la confirmation d'une nouvelle adresse, la désactivation de la double authentification par lien, et le formulaire de support des visiteurs.


## 4. Les pages du site que l'API désigne

Les e-mails et la banque renvoient l'utilisateur vers sept pages du site, à l'adresse de `PublicWebsiteAddress`.

| Page                    | Ouverte depuis                                                                                | Elle reçoit       | Elle appelle                                                                       |
|-------------------------|-----------------------------------------------------------------------------------------------|-------------------|------------------------------------------------------------------------------------|
| `/confirm-email`        | L'e-mail d'inscription                                                                        | `#token=…`        | `POST /api/account/confirm-email` avec `{ token, password }`                       |
| `/reset-password`       | L'e-mail de mot de passe oublié                                                               | `#token=…`        | `POST /api/account/reset-password` avec `{ token, newPassword }`                   |
| `/unlock-account`       | L'e-mail envoyé quand le compte est bloqué                                                    | `#token=…`        | `POST /api/account/unlock` avec `{ token }`                                        |
| `/confirm-email-change` | L'e-mail envoyé à la nouvelle adresse                                                         | `#token=…`        | `POST /api/account/confirm-email-change` avec `{ token, password }`                |
| `/disable-two-factor`   | L'e-mail envoyé par le support                                                                | `#token=…`        | `POST /api/account/disable-two-factor` avec `{ token, password }`                  |
| `/support`              | Le lien de support des alertes de sécurité, des réponses du support et des avis sur le compte | Rien              | `POST /api/support-requests/visitor`, ou `/api/support-requests` une fois connecté |
| `/bank-callback`        | La banque, à la fin de l'autorisation                                                         | `?code=…&state=…` | `POST /api/bank-connections/finish` avec `{ code, state }`                         |

**Le jeton d'un lien est après le `#`** : le navigateur ne l'envoie jamais à un serveur, et il n'apparaît dans aucun journal. La page le lit dans `location.hash`, puis l'envoie dans le corps de la requête.

**La connexion d'une banque :**
1. `GET /api/banks` donne la liste des banques.
2. `POST /api/bank-connections` avec `{ bankName }` donne `{ bankAddress }` : envoie le navigateur à cette adresse.
3. La banque le renvoie vers `/bank-callback`. Envoie à l'API les deux valeurs reçues, telles quelles, même s'il en manque une : l'API répond le refus à afficher.
4. Le renouvellement suit le même chemin : `POST /api/bank-connections/{connectionId}/renew` donne `{ bankAddress }`.


## 5. Les formats

- Les corps des requêtes sont en JSON, avec l'en-tête `Content-Type: application/json`. Tout autre format est refusé (415).
- Les champs s'écrivent en camelCase : `amountInCents`, `newPassword`.
- **Un montant est un nombre entier de centimes**, écrit comme un nombre JSON. L'API refuse `"3000"` écrit comme un texte (SS §5).
- Une date s'écrit `AAAA-MM-JJ`. Une date avec une heure est en UTC, au format ISO 8601 : convertis-la à l'heure de l'utilisateur pour l'afficher.
- Les valeurs des listes s'écrivent en texte, en camelCase : `inProgress`, `inError`, `notConfirmed`.
- Les opérations et l'historique d'un sac arrivent par pages de 50, numérotées de 1 à 10 000.
- Les valeurs passées dans l'adresse s'écrivent comme les champs du JSON, et OpenAPI les nomme ainsi : `?from=2026-11-01&to=2026-11-30&accountId=…`. Une valeur refusée revient sous ce même nom dans `errors`.


## 6. Les erreurs

Toute erreur d'une route `/api` suit le format « problem details » (`application/problem+json`) :
- `title` : le message à afficher, en français ;
- `status` : le code de la réponse ;
- `type` : un lien vers la définition du code ;
- `errors` : seulement pour un 400, les messages de chaque champ refusé.

Pour un 400, chaque clé de `errors` est le nom JSON du champ refusé : affiche ses messages sous ce champ. Deux clés ne visent aucun champ :
- `general` : la requête est illisible ;
- `csrfToken` : le jeton anti-CSRF manque ou n'est pas valable (§3).

| Code | Sens                                                                                    | Ce que fait le site                                                     |
|------|-----------------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| 400  | La requête est refusée                                                                  | Affiche les messages de `errors`                                        |
| 401  | La session manque ou est fermée ; à la connexion, les identifiants sont faux            | Renvoie vers la page de connexion, ou affiche `title`                   |
| 403  | L'adresse n'est pas confirmée (connexion) ; une action réservée à l'administration      | Affiche `title`                                                         |
| 404  | La ressource n'existe pas, ou elle appartient à un autre compte                         | Affiche `title`, puis recharge la liste                                 |
| 409  | La demande n'est pas possible dans l'état actuel, ou les données ont changé entre-temps | Affiche `title`, puis recharge les données                              |
| 413  | La requête est trop volumineuse                                                         | Affiche `title`                                                         |
| 415  | Le corps n'est pas du JSON                                                              | C'est une erreur du site                                                |
| 429  | Trop de demandes                                                                        | Attends le nombre de secondes de l'en-tête `Retry-After`, puis réessaie |
| 500  | Une erreur inattendue                                                                   | Affiche `title`                                                         |
| 503  | Le serveur est très demandé, ou la banque ne répond pas                                 | Réessaie après `Retry-After` s'il est présent, sinon plus tard          |

La description OpenAPI dit, route par route, lesquels de ces codes elle peut donner.


## 7. Les limites de débit

Au-delà d'une limite, l'API répond 429 avec l'en-tête `Retry-After`.

| Limite                 | Valeur                                 | Routes                                                                                                               |
|------------------------|----------------------------------------|----------------------------------------------------------------------------------------------------------------------|
| Toutes les requêtes    | 600 par minute et par adresse IP       | Toutes                                                                                                               |
| Lectures coûteuses     | 60 par minute et par session, ensemble | `GET /api/transactions`, `/api/charts/*`, `/api/bags`, `/api/bags/{bagId}/movements`, `/api/suggestions`             |
| Banque                 | 20 par heure et par session, ensemble  | `GET /api/banks`, `POST /api/bank-connections`, `…/renew`, `…/finish`, `DELETE /api/bank-connections/{connectionId}` |
| Mise à jour des soldes | 1 par minute et par utilisateur        | `POST /api/bank-connections/refresh`                                                                                 |

**À chaque ouverture de l'application**, appelle `POST /api/bank-connections/refresh` pour mettre les soldes à jour (`gestion-rules.md`, Connexion bancaire). Évite d'appeler deux fois la même lecture coûteuse, par exemple quand une page s'affiche deux fois.

Les autres limites, propres à une action (inscription, mot de passe oublié…), sont dans SS §6.


## 8. Ce que les règles de gestion confient au site

- La page d'accueil, ouverte aux visiteurs.
- Le type des graphiques : anneau ou camembert pour la répartition des dépenses, barres ou courbe pour l'évolution mois par mois.
- La date et l'heure de la dernière mise à jour des soldes.
- Une confirmation avant de déconnecter une banque, et avant de demander la suppression du compte.
- L'invitation à confirmer son adresse, quand la connexion répond qu'elle ne l'est pas.


## 9. Les versions de l'API

Les routes n'ont pas de préfixe de version : les routes actuelles sont la version 1. Elles ne changeront pas d'une façon qui casse le site. Si un changement incompatible devient nécessaire, il passera par de nouvelles routes `/api/v2/…`, à côté des anciennes, qui resteront en place.


## 10. La description OpenAPI

- Elle n'est publiée qu'en développement : `https://localhost:7078/openapi/v1.json`.
- Elle décrit chaque route : ses champs, sa réponse réussie avec son type, et chacun de ses refus avec sa description en français.
- Chaque test de l'API vérifie que ses réponses y sont décrites : un refus ajouté sans être décrit fait échouer les tests.
- Elle peut servir à générer un client typé pour le site.
