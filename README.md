# WalletFlow — Backend

WalletFlow est une application de finances personnelles : elle connecte les comptes bancaires en lecture seule, range chaque opération dans une catégorie, suit les budgets du mois, et permet de mettre de l'argent de côté dans des « sacs » virtuels, avec des suggestions d'épargne tirées des dépenses. Ce dépôt contient son API REST.

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-4169E1?logo=postgresql&logoColor=white)
![EF Core](https://img.shields.io/badge/EF%20Core-10-512BD4?logo=dotnet&logoColor=white)


## Technologies et versions

Les versions ci-dessous viennent des fichiers du projet. Les versions exactes des paquets sont verrouillées dans le `packages.lock.json` de chaque projet : si ce tableau s'en écarte un jour, ce sont les fichiers de verrouillage qui font foi.

| Domaine                 | Technologie                                                        | Version (référence)                        |
|-------------------------|--------------------------------------------------------------------|--------------------------------------------|
| Exécution               | .NET                                                               | 10 (SDK 10.0.x)                            |
| Langage                 | C#                                                                 | 14, types de référence nullables activés   |
| Web                     | ASP.NET Core (contrôleurs)                                         | 10.0                                       |
| Accès aux données       | Entity Framework Core + Npgsql                                     | 10.0.3                                     |
| Base de données         | PostgreSQL                                                         | 13 ou plus (développé et testé sur la 18)  |
| Mots de passe           | Argon2id avec libsodium (`Sodium.Core`)                            | 1.4.1                                      |
| Double authentification | Codes TOTP (`Otp.NET`)                                             | 1.4.1                                      |
| Chiffrement             | AES-256-GCM (intégré à .NET)                                       | —                                          |
| E-mails                 | SMTP avec MailKit (TLS 1.2 ou plus)                                | 4.18.2                                     |
| Données bancaires       | Enable Banking (information sur les comptes, DSP2)                 | API REST avec signatures RS256             |
| Contrat de l'API        | OpenAPI (`Microsoft.AspNetCore.OpenApi`)                           | 10.0.15, publié en développement seulement |
| Tests                   | xUnit v3 (`xunit.v3.mtp-off`) + `Microsoft.AspNetCore.Mvc.Testing` | 4.0.3 / 10.0.15                            |
| Intégration continue    | GitHub Actions, gitleaks, CodeQL, Dependabot                       | gitleaks 8.30.1, action CodeQL 4.39.3      |


## Prérequis

- **SDK .NET 10.0** (`dotnet --version` doit afficher 10.0.x).
- **PostgreSQL 13 ou plus**, avec un compte administrateur et le client `psql`.
- **OpenSSL**, pour créer la clé de chiffrement (déjà installé sur macOS et la plupart des distributions Linux).
- Facultatif : une application **Enable Banking** de bac à sable (son identifiant et sa clé privée RSA), pour connecter des banques de test. Sans elle, les routes des banques répondent 503 en développement.


## Lancer en local

Toutes les commandes se lancent depuis la racine du dépôt.

**1. Créer la base** avec un compte administrateur de PostgreSQL. L'outil de migration crée les tables, les règles que la base fait respecter, et le rôle restreint `walletflow_app` qu'utilise l'API (seulement si le serveur ne l'a pas encore) :

```bash
createdb walletflow
PGDATABASE=walletflow database/migrate.sh
psql -d walletflow -c '\password walletflow_app'
```

Une base créée plus tôt avec le script du MPD s'adopte une seule fois avec `PGDATABASE=walletflow database/migrate.sh --baseline`.

**2. Ranger les secrets** dans les user-secrets de l'API. Ils ne vont jamais dans le code ni dans Git :

```bash
dotnet user-secrets set "ConnectionStrings:WalletFlowDatabase" "Host=localhost;Port=5432;Database=walletflow;Username=walletflow_app;Password=<le mot de passe choisi plus haut>" --project src/WalletFlow.Api
dotnet user-secrets set "TwoFactorSecretEncryptionKey" "$(openssl rand -base64 32)" --project src/WalletFlow.Api
```

Garder une copie de `TwoFactorSecretEncryptionKey` en lieu sûr : elle chiffre les secrets de la double authentification, et la perdre casse les codes d'application d'authentification de tous les comptes qui s'en servent.

Pour changer cette clé, donner la nouvelle comme `TwoFactorSecretEncryptionKey`, avec un nouveau `TwoFactorSecretEncryptionKeyId` (la première clé porte le 1), et garder l'ancienne comme `TwoFactorSecretPreviousKeys:<son identifiant>`. Une tâche quotidienne rechiffre chaque secret avec la nouvelle clé ; quand le journal technique ne signale plus aucun échec, retirer l'ancienne.

Pour connecter des banques de test, ajouter aussi les réglages d'Enable Banking :

```bash
dotnet user-secrets set "EnableBanking:ApplicationId" "<identifiant de l'application>" --project src/WalletFlow.Api
dotnet user-secrets set "EnableBanking:PrivateKeyPath" "<chemin absolu du fichier .pem de la clé privée>" --project src/WalletFlow.Api
```

Les autres réglages de développement (`PublicWebsiteAddress`, `LocalEmailsFolder`, `AllowedHosts`) sont déjà dans `src/WalletFlow.Api/appsettings.Development.json`.

**3. Démarrer l'API** en HTTPS :

```bash
dotnet dev-certs https --trust
dotnet run --project src/WalletFlow.Api --launch-profile https
```

- L'API écoute sur `https://localhost:7078`, et sa description OpenAPI est à `https://localhost:7078/openapi/v1.json`.
- En développement, les e-mails ne partent pas : chacun est écrit dans un fichier de `.local-emails/` (ignoré par Git), liens compris, même si des réglages SMTP sont présents. Hors du développement, aucun e-mail n'est jamais écrit sur le disque : ils partent par le serveur SMTP du fournisseur d'e-mails, et l'API refuse de démarrer sans `Smtp:Host`, `Smtp:Username`, `Smtp:Password` et `Smtp:SenderAddress` (facultatifs : `Smtp:Port`, 587 par défaut, et `Smtp:SenderName`, « WalletFlow » par défaut). En production, ils viennent de variables d'environnement, comme `Smtp__Password`. La connexion est toujours chiffrée (STARTTLS, ou TLS dès le début sur le port 465), et le certificat du serveur doit être valide.
- Les nouveaux mots de passe sont comparés à Pwned Passwords : seuls les 5 premiers caractères de leur empreinte SHA-1 quittent le serveur. Sans accès HTTPS sortant vers `api.pwnedpasswords.com`, ils sont acceptés, et le journal technique le dit.
- Le nombre de calculs Argon2id faits en même temps se règle avec `Argon2:ConcurrentComputations`, de 1 à 64 ; sans réglage, c'est le nombre de cœurs. Chaque calcul prend 32 Mio de mémoire, et au plus 4 attendent leur tour par calcul permis : au-delà, ou après 5 secondes d'attente, la requête reçoit « serveur très demandé » (503).
- Points de santé, ouverts sans session : `/health/live` (l'API tourne), `/health/ready` (la base répond aussi, en moins de 5 secondes) et `/health/tasks` (les tâches de fond vont bien) ; ils répondent `Healthy`, ou `Unhealthy` avec 503. Hors du développement, ils ne répondent qu'aux noms du site d'`AllowedHosts` : la sonde de l'hébergeur doit donc envoyer l'un d'eux.
- Hors du développement, `DataProtection:KeysFolder` doit donner un dossier absolu qui survit aux redéploiements, comme un volume monté : il garde les clés qui signent les jetons anti-CSRF. Ces clés sont chiffrées avec un certificat X.509 : `DataProtection:CertificatePath` et `DataProtection:CertificateKeyPath` donnent les chemins absolus de son certificat PEM et de sa clé privée RSA, par exemple créés avec `openssl req -x509 -newkey rsa:4096 -nodes -days 3650 -subj "/CN=Jetons anti-CSRF WalletFlow" -keyout cle-des-jetons.pem -out certificat-des-jetons.pem`. Garder la clé privée à part du dossier des clés, lisible par l'API seulement. Sans ces réglages, l'API refuse de démarrer. Un nouveau certificat rend les anciennes clés illisibles : l'API crée une nouvelle clé, et chaque jeton anti-CSRF en cours est refusé une fois, jusqu'à ce que le site en demande un nouveau.
- Derrière un proxy inverse, le déclarer avec `ReverseProxy:KnownProxies:0` (une adresse) ou `ReverseProxy:KnownNetworks:0` (un réseau comme `10.0.0.0/24`). Sinon, les limites de débit comptent l'adresse du proxy pour tout le monde.
- L'API refuse de démarrer quand un réglage obligatoire manque ou ne peut pas être lu, et elle dit lequel.

**4. Donner le rôle d'administrateur** (facultatif). Seul l'administrateur de PostgreSQL le peut, une fois le compte confirmé. Les routes d'administration demandent aussi que le compte ait activé la double authentification :

```bash
psql -d walletflow -c "SELECT fn_changer_role('<adresse e-mail du compte>', 'administrateur');"
```


## Sauvegardes

Les scripts de `database/backup` font une sauvegarde chiffrée de la base, et la restaurent dans une base neuve. Une sauvegarde est chiffrée pendant sa lecture, avec un certificat public : le serveur peut chiffrer, mais jamais déchiffrer. Garder la clé privée hors du serveur, protégée par une phrase secrète.

**1. Créer les clés des sauvegardes une seule fois**, sur sa propre machine (OpenSSL demande la phrase secrète de la clé privée) :

```bash
openssl req -x509 -newkey rsa:4096 -days 3650 -subj "/CN=Sauvegardes WalletFlow" -keyout cle-privee-des-sauvegardes.pem -out certificat-des-sauvegardes.pem
```

Seul `certificat-des-sauvegardes.pem` va sur le serveur. Garder `cle-privee-des-sauvegardes.pem` et sa phrase secrète en lieu sûr, à part des sauvegardes, avec `TwoFactorSecretEncryptionKey` : une base restaurée sans cette clé ne peut plus servir la double authentification.

**2. Sauvegarder**, une fois par jour (par exemple avec cron ou un minuteur systemd). Le script écrit `walletflow-YYYYMMDD-HHMMSS.dump.cms` et efface les sauvegardes de plus de `BACKUP_KEEP_DAYS` jours (30 par défaut) :

```bash
PGHOST=<serveur> PGDATABASE=walletflow PGUSER=<rôle qui peut lire toutes les tables> BACKUP_CERTIFICATE=certificat-des-sauvegardes.pem BACKUP_FOLDER=<dossier> database/backup/backup-database.sh
```

**3. Restaurer** dans une base neuve, jamais par-dessus celle qui sert. Le rôle `walletflow_app` doit déjà exister sur le serveur :

```bash
PGHOST=<serveur> PGUSER=<administrateur> TARGET_DATABASE=walletflow_restored BACKUP_FILE=<fichier de sauvegarde> BACKUP_CERTIFICATE=certificat-des-sauvegardes.pem BACKUP_PRIVATE_KEY=cle-privee-des-sauvegardes.pem database/backup/restore-database.sh
```

Vérifier la base restaurée, puis y diriger la chaîne de connexion de l'API. La tâche horaire de l'API efface de nouveau tout compte dont la suppression date de plus de 30 jours.

`database/backup/test-backup-round-trip.sh` prouve tout l'aller-retour sur un serveur de test (`PGHOST`, et `PGUSER` pour un administrateur), et la CI le lance à chaque envoi.


## Tests et vérifications

Les tests SQL vérifient les contraintes, les déclencheurs, les cascades et les droits du script de la base. Ils tournent dans une seule transaction, annulée à la fin :

```bash
psql -h localhost -U walletflow_app -d walletflow -v ON_ERROR_STOP=1 -f database/tests/test_001_create_walletflow_schema.sql
```

Les tests .NET comprennent des parcours de bout en bout et des courses de concurrence sur une vraie base PostgreSQL. Ils se connectent en `walletflow_app` à la base `walletflow` sur `localhost`, sans mot de passe (authentification `trust` en local). Pour une autre base ou avec un mot de passe, donner une chaîne de connexion complète :

```bash
export WALLETFLOW_TEST_DATABASE="Host=localhost;Port=5432;Database=walletflow;Username=walletflow_app;Password=<mot de passe>"
```

Puis lancer les mêmes vérifications que la CI :

```bash
dotnet restore --locked-mode
dotnet build --no-restore -warnaserror
dotnet format --verify-no-changes --no-restore
dotnet test --no-build
```

La CI construit aussi l'image Docker (`src/WalletFlow.Api/Dockerfile`) comme en production, vérifie qu'elle contient le fuseau horaire de Paris et qu'elle tourne avec un utilisateur sans droits, la scanne avec Trivy (failles connues et secrets oubliés), la démarre en mode production avec des secrets jetables, sonde `/health/live`, `/health/ready` et `/health/tasks`, puis attaque l'API en marche avec OWASP ZAP, route par route, d'après sa description OpenAPI. Le conteneur ne sert que du HTTP, sur le port 8080 : le HTTPS revient au proxy inverse placé devant lui.

Chaque test nettoie les lignes qu'il écrit. Ses événements du journal de sécurité restent, rendus anonymes, car le rôle de l'application ne peut qu'ajouter des événements au journal.


## Structure du dépôt et architecture (vue d'ensemble)

```
WalletFlow-Backend/
├── src/
│   ├── WalletFlow.Domain/           entités et règles métier, sans aucune dépendance
│   ├── WalletFlow.Application/      cas d'utilisation, et les interfaces dont ils ont besoin
│   ├── WalletFlow.Infrastructure/   PostgreSQL, outils de sécurité, prestataire bancaire, e-mails
│   └── WalletFlow.Api/              routes HTTP, configuration, tâches de fond
├── tests/WalletFlow.Tests/          tests unitaires, de base, de bout en bout et de concurrence
├── database/                        script du schéma et ses tests SQL
├── Docs/                            règles de gestion, dictionnaire des données, MCD, MLD, MPD, stratégie de sécurité, guide du frontend
└── .github/                         intégration continue et Dependabot
```

Le backend suit la Clean Architecture : les dépendances ne pointent que vers l'intérieur. Le Domain ne dépend de rien, l'Application seulement du Domain, l'Infrastructure met en œuvre les interfaces de l'Application, et l'Api relie le tout. Ni le Domain ni l'Application ne connaissent EF Core ou PostgreSQL.

Chaque requête suit le même chemin : un contrôleur lit la session et la requête, appelle un seul service de l'Application, et transforme son résultat fermé en réponse HTTP. Le service lit par les dépôts, applique les règles du Domain, prépare ses changements, et les enregistre en une seule transaction de la base. Quand une autre requête a changé les mêmes lignes entre-temps (versions de ligne `xmin` de PostgreSQL), le service relit tout et rejoue ses contrôles : des requêtes simultanées ne peuvent donc jamais contourner une règle.

Le dossier `Docs/` fait référence : les règles de gestion (`gestion-rules.md`) viennent d'abord, et le dictionnaire des données, le MCD, le MLD, le MPD et la stratégie de sécurité en découlent. Le script de la base est une copie exacte de celui du MPD.

Le guide du frontend (`Docs/frontend-guide.md`) explique comment le site appelle l'API : même origine et HTTPS, les cookies de session, le jeton anti-CSRF, les sept pages qu'ouvrent les e-mails et les banques, les formats, les erreurs et les limites de débit. Le détail de chaque route est dans la description OpenAPI, publiée en développement à `/openapi/v1.json`.


## Le backend en détail

- **Domain** (`src/WalletFlow.Domain/`) : utilisateurs et sécurité, comptes bancaires et catégories, sacs et suggestions d'épargne, notifications et support. Les entités gardent leur état privé, et ne le changent que par des méthodes de règle. Les montants sont des centimes entiers, et chaque jour vu par l'utilisateur commence à minuit, heure de Paris.
- **Application** (`src/WalletFlow.Application/`) : un service par cas d'utilisation (inscription, connexion, double authentification, opérations, graphiques, sacs, suggestions, alertes, support, administration, export, suppression), une unité de travail explicite, une aide qui rejoue en cas de conflit de concurrence, et le journal de sécurité.
- **Infrastructure** (`src/WalletFlow.Infrastructure/`) : dépôts EF Core, hachage Argon2id, chiffrement AES-256-GCM des secrets de la double authentification, jetons aléatoires gardés sous forme d'empreintes SHA-256, client d'Enable Banking, et textes des e-mails en français.
- **Api** (`src/WalletFlow.Api/`) : contrôleurs, sessions par cookie, jetons anti-CSRF, limites de débit (par route, par session, et 600 requêtes par minute par adresse IP ou bloc IPv6 /64), en-têtes de sécurité, messages d'erreur en français au format « problem details », et les tâches de fond : file des e-mails (gardée en base), mises à jour des banques, suggestions, alertes, effacement des comptes, rechiffrement des secrets de la double authentification et nettoyages.
- **Base de données** (`database/`) : contraintes et déclencheurs qui reprennent les règles métier, un rôle restreint pour l'API, et un journal de sécurité où l'API ne peut qu'ajouter.

Règles principales du backend :

- Les règles métier vivent dans le Domain. Les contrôleurs restent minces : ils ne décident jamais rien.
- L'identifiant de l'utilisateur vient toujours de la session, jamais de la requête.
- Chaque route demande une session et un jeton anti-CSRF, sauf si elle dit explicitement le contraire.
- Chaque route d'écriture déclare une limite de taille de requête, et chaque route qui reçoit un identifiant est testée avec l'identifiant d'un autre utilisateur : elle doit répondre exactement comme pour un identifiant inconnu.
- Un changement du schéma commence dans le MPD (`Docs/merise/mpd.md`), passe ensuite dans le script et ses tests SQL, puis dans une nouvelle migration numérotée de `database/migrations`, qui met à jour les bases existantes ; `database/tests/test-migrations.sh` prouve qu'une base migrée correspond au MPD.
- Aucun secret dans le code ni dans Git : user-secrets en développement, variables d'environnement en production.


## État du projet

L'API couvre toutes les règles de gestion du projet : comptes et double authentification, connexions bancaires, opérations et catégories, graphiques, sacs, suggestions d'épargne, alertes, support et administration, export des données et suppression du compte. Le site web, l'application mobile et la mise en production viennent ensuite. L'API garde une partie de son état de sécurité en mémoire, comme les connexions qui attendent leur code et les compteurs des limites de débit : elle tourne donc en une seule instance. Au démarrage, elle prend un verrou consultatif de PostgreSQL, et une deuxième instance sur la même base refuse de démarrer. En développement, elle écrit les e-mails dans un dossier local ; ailleurs, elle les envoie par le serveur SMTP de ses réglages.


## Licence

Logiciel propriétaire : tous droits réservés. Voir [LICENSE](LICENSE).
