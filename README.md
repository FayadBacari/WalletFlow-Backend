# WalletFlow — Backend

WalletFlow is a personal finance application: it connects your bank accounts in read-only mode, sorts every transaction into a category, follows your monthly budgets, and lets you set money aside in virtual "bags" with savings suggestions based on your spending. This repository holds its REST API.

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-4169E1?logo=postgresql&logoColor=white)
![EF Core](https://img.shields.io/badge/EF%20Core-10-512BD4?logo=dotnet&logoColor=white)


## Stack and versions

The versions below come from the project files. The exact package versions are locked in each project's `packages.lock.json`: if this table ever drifts, the lock files are the source of truth.

| Area         | Technology                                                         | Version (reference)                      |
|--------------|--------------------------------------------------------------------|------------------------------------------|
| Runtime      | .NET                                                               | 10 (SDK 10.0.x)                          |
| Language     | C#                                                                 | 14, nullable reference types enabled     |
| Web          | ASP.NET Core (controllers)                                         | 10.0                                     |
| Data access  | Entity Framework Core + Npgsql                                     | 10.0.3                                   |
| Database     | PostgreSQL                                                         | 13 or later (developed and tested on 18) |
| Passwords    | Argon2id through libsodium (`Sodium.Core`)                         | 1.4.1                                    |
| Two-factor   | TOTP codes (`Otp.NET`)                                             | 1.4.1                                    |
| Encryption   | AES-256-GCM (built into .NET)                                      | —                                        |
| E-mails      | SMTP through MailKit (TLS 1.2+)                                    | 4.18.2                                   |
| Bank data    | Enable Banking (PSD2 account information)                          | REST API with RS256 signatures           |
| API contract | OpenAPI (`Microsoft.AspNetCore.OpenApi`)                           | 10.0.15, published in development only   |
| Tests        | xUnit v3 (`xunit.v3.mtp-off`) + `Microsoft.AspNetCore.Mvc.Testing` | 4.0.3 / 10.0.15                          |
| CI           | GitHub Actions, gitleaks, CodeQL, Dependabot                       | gitleaks 8.30.1, CodeQL action 4.39.3    |


## Prerequisites

- **.NET SDK 10.0** (`dotnet --version` should print 10.0.x).
- **PostgreSQL 13 or later**, with an administrator account and the `psql` client.
- **OpenSSL**, to generate the encryption key (preinstalled on macOS and most Linux distributions).
- Optional: an **Enable Banking** sandbox application (its id and its RSA private key) to connect test banks. Without it, the bank routes answer 503 in development.


## Run locally

All commands run from the repository root.

**1. Create the database** with a PostgreSQL administrator account. The migration tool creates the tables, the rules the database enforces, and the restricted role `walletflow_app` that the API uses (only if the server does not have it yet):

```bash
createdb walletflow
PGDATABASE=walletflow database/migrate.sh
psql -d walletflow -c '\password walletflow_app'
```

A database created earlier with the MPD script is adopted once with `PGDATABASE=walletflow database/migrate.sh --baseline`.

**2. Store the secrets** in the API's user-secrets. They never go into the code or Git:

```bash
dotnet user-secrets set "ConnectionStrings:WalletFlowDatabase" "Host=localhost;Port=5432;Database=walletflow;Username=walletflow_app;Password=<the password chosen above>" --project src/WalletFlow.Api
dotnet user-secrets set "TwoFactorSecretEncryptionKey" "$(openssl rand -base64 32)" --project src/WalletFlow.Api
```

Keep a copy of `TwoFactorSecretEncryptionKey` somewhere safe: it encrypts the two-factor secrets, and losing it breaks the authenticator codes of every account that uses them.

To change this key, set the new one as `TwoFactorSecretEncryptionKey` with a new `TwoFactorSecretEncryptionKeyId` (the first key is 1), and keep the old one as `TwoFactorSecretPreviousKeys:<its id>`. A daily task re-encrypts every secret with the new key; once the technical log reports no failure, remove the old key.

To connect test banks, add the Enable Banking settings too:

```bash
dotnet user-secrets set "EnableBanking:ApplicationId" "<application id>" --project src/WalletFlow.Api
dotnet user-secrets set "EnableBanking:PrivateKeyPath" "<absolute path to the private key .pem file>" --project src/WalletFlow.Api
```

The other development settings (`PublicWebsiteAddress`, `LocalEmailsFolder`, `AllowedHosts`) are already in `src/WalletFlow.Api/appsettings.Development.json`.

**3. Start the API** over HTTPS:

```bash
dotnet dev-certs https --trust
dotnet run --project src/WalletFlow.Api --launch-profile https
```

- The API listens on `https://localhost:7078`, and its OpenAPI description is at `https://localhost:7078/openapi/v1.json`.
- In development, e-mails are not sent: each one is written as a file in `.local-emails/` (ignored by Git), links included, even when SMTP settings are present. Outside development, no e-mail is ever written to disk: they leave through the e-mail provider's SMTP server, and the API refuses to start without `Smtp:Host`, `Smtp:Username`, `Smtp:Password` and `Smtp:SenderAddress` (optional: `Smtp:Port`, 587 by default, and `Smtp:SenderName`, "WalletFlow" by default). In production these come from environment variables, such as `Smtp__Password`. The connection is always encrypted (STARTTLS, or TLS from the start on port 465) and the server certificate must be valid.
- New passwords are checked against Pwned Passwords: only the first 5 characters of their SHA-1 hash leave the server. Without outbound HTTPS to `api.pwnedpasswords.com`, they are accepted and the technical log says so.
- Health endpoints, open without a session: `/health/live` (the API runs), `/health/ready` (the database answers too, within 5 seconds) and `/health/tasks` (the background tasks are well); they answer `Healthy`, or `Unhealthy` with 503. Outside development they only answer the site names of `AllowedHosts`, so the host's probe must send one of them.
- Outside development, `DataProtection:KeysFolder` must give an absolute folder that survives redeployments, such as a mounted volume: it keeps the keys that sign the anti-CSRF tokens. These keys are encrypted with an X.509 certificate: `DataProtection:CertificatePath` and `DataProtection:CertificateKeyPath` give the absolute paths of its PEM certificate and RSA private key, for example made with `openssl req -x509 -newkey rsa:4096 -nodes -days 3650 -subj "/CN=WalletFlow keys" -keyout keys-key.pem -out keys-certificate.pem`. Keep the private key apart from the keys folder, readable only by the API. Without these settings, the API refuses to start. A new certificate makes the old keys unreadable: the API creates a new key, and each anti-CSRF token in use is refused once, until the website asks for a new one.
- Behind a reverse proxy, declare it with `ReverseProxy:KnownProxies:0` (an address) or `ReverseProxy:KnownNetworks:0` (a network such as `10.0.0.0/24`). Otherwise the rate limits count the proxy's address for everyone.
- The API refuses to start when a required setting is missing or unreadable, and says which one.

**4. Give the administrator role** (optional). Only the PostgreSQL administrator can, once the account is confirmed. The administration routes also require the account to have two-factor authentication on:

```bash
psql -d walletflow -c "SELECT fn_changer_role('<account e-mail>', 'administrateur');"
```


## Backups

The scripts of `database/backup` make one encrypted backup of the database, and restore it into a new database. A backup is encrypted while it is read, with a public certificate: the server can encrypt, but never decrypt. Keep the private key off the server, protected by a passphrase.

**1. Create the backup keys once**, on your own machine (OpenSSL asks for the passphrase of the private key):

```bash
openssl req -x509 -newkey rsa:4096 -days 3650 -subj "/CN=WalletFlow backups" -keyout backup-private-key.pem -out backup-certificate.pem
```

Only `backup-certificate.pem` goes on the server. Keep `backup-private-key.pem` and its passphrase somewhere safe, apart from the backups, with `TwoFactorSecretEncryptionKey`: a restored database without that key cannot use two-factor authentication anymore.

**2. Back up**, once a day (for example with cron or a systemd timer). The script writes `walletflow-YYYYMMDD-HHMMSS.dump.cms` and erases the backups older than `BACKUP_KEEP_DAYS` (30 by default):

```bash
PGHOST=<server> PGDATABASE=walletflow PGUSER=<role that can read every table> BACKUP_CERTIFICATE=backup-certificate.pem BACKUP_FOLDER=<folder> database/backup/backup-database.sh
```

**3. Restore** into a new database, never over the one in use. The `walletflow_app` role must already exist on the server:

```bash
PGHOST=<server> PGUSER=<administrator> TARGET_DATABASE=walletflow_restored BACKUP_FILE=<backup file> BACKUP_CERTIFICATE=backup-certificate.pem BACKUP_PRIVATE_KEY=backup-private-key.pem database/backup/restore-database.sh
```

Check the restored database, then point the API's connection string at it. The hourly task of the API erases again any account whose deletion is more than 30 days old.

`database/backup/test-backup-round-trip.sh` proves the whole round trip on a test server (`PGHOST`, `PGUSER` for an administrator), and the CI runs it on every push.


## Tests and checks

The SQL tests check the constraints, triggers, cascades and rights of the database script. They run in one transaction, rolled back at the end:

```bash
psql -h localhost -U walletflow_app -d walletflow -v ON_ERROR_STOP=1 -f database/tests/test_001_create_walletflow_schema.sql
```

The .NET tests include end-to-end journeys and concurrency races on a real PostgreSQL database. They connect as `walletflow_app` to the `walletflow` database on `localhost`, without a password (local `trust` authentication). To use another database or a password, give a full connection string:

```bash
export WALLETFLOW_TEST_DATABASE="Host=localhost;Port=5432;Database=walletflow;Username=walletflow_app;Password=<password>"
```

Then run the same checks as the CI:

```bash
dotnet restore --locked-mode
dotnet build --no-restore -warnaserror
dotnet format --verify-no-changes --no-restore
dotnet test --no-build
```

The CI also builds the Docker image (`src/WalletFlow.Api/Dockerfile`) as in production, checks it holds the Paris time zone and runs as a non-root user, scans it with Trivy (known vulnerabilities and forgotten secrets), starts it in production mode with throwaway secrets, probes `/health/live`, `/health/ready` and `/health/tasks`, then attacks the running API with OWASP ZAP, route by route from its OpenAPI description. The container only serves HTTP on port 8080: HTTPS belongs to the reverse proxy in front of it.

Each test cleans up the rows it writes. Their security journal events stay, anonymized, because the application role can only add events to the journal.


## Repository structure and architecture (high-level)

```
WalletFlow-Backend/
├── src/
│   ├── WalletFlow.Domain/           entities and business rules, no dependency
│   ├── WalletFlow.Application/      use cases, and the interfaces they need
│   ├── WalletFlow.Infrastructure/   PostgreSQL, security tools, bank provider, e-mails
│   └── WalletFlow.Api/              HTTP routes, configuration, background tasks
├── tests/WalletFlow.Tests/          unit, database, end-to-end and concurrency tests
├── database/                        schema script and its SQL tests
├── Docs/                            business rules, data dictionary, MCD, MLD, MPD, security strategy, frontend guide
└── .github/                         CI workflow and Dependabot
```

The backend follows Clean Architecture: dependencies only point inwards. The Domain depends on nothing, the Application only on the Domain, the Infrastructure implements the Application's interfaces, and the Api wires everything together. Neither the Domain nor the Application knows about EF Core or PostgreSQL.

Every request follows the same path: a controller reads the session and the request, calls one Application service, and turns its closed result into an HTTP answer. The service reads through repositories, applies the Domain rules, prepares its changes, and saves them in a single database transaction. When another request changed the same rows in the meantime (PostgreSQL `xmin` row versions), the service reads everything again and replays its checks, so simultaneous requests can never bypass a rule.

The `Docs/` folder is the reference: the business rules (`gestion-rules.md`) come first, and the data dictionary, the MCD, the MLD, the MPD and the security strategy follow from them. The database script is an exact copy of the MPD's script.

The frontend guide (`Docs/frontend-guide.md`) explains how the website calls the API: same origin and HTTPS, the session cookies, the anti-CSRF token, the seven pages that e-mails and banks open, the formats, the errors and the rate limits. The detail of every route is in the OpenAPI description, published in development at `/openapi/v1.json`.


## Backend in detail

- **Domain** (`src/WalletFlow.Domain/`): users and security, bank accounts and categories, bags and savings suggestions, notifications and support. Entities keep their state private and change it only through rule methods. Amounts are integer cents, and every user-facing day starts at midnight in Europe/Paris.
- **Application** (`src/WalletFlow.Application/`): one service per use case (sign-up, login, two-factor, transactions, charts, bags, suggestions, alerts, support, administration, export, deletion), an explicit unit of work, a retry helper for concurrency conflicts, and the security journal.
- **Infrastructure** (`src/WalletFlow.Infrastructure/`): EF Core repositories, Argon2id hashing, AES-256-GCM encryption of the two-factor secrets, random tokens stored as SHA-256 hashes, the Enable Banking client, and the French texts of the e-mails.
- **Api** (`src/WalletFlow.Api/`): controllers, cookie sessions, anti-CSRF tokens, rate limits (per route, per session, and 600 requests per minute per IP address or IPv6 /64 block), security headers, French error messages in the "problem details" format, and the background tasks: e-mail queue (kept in the database), bank updates, suggestions, alerts, account erasure, re-encryption of the two-factor secrets and clean-ups.
- **Database** (`database/`): constraints and triggers that mirror the business rules, a restricted role for the API, and a security journal the API can only append to.

Main backend rules:

- Business rules live in the Domain. Controllers stay thin: they never decide anything.
- The user id always comes from the session, never from the request.
- Every route requires a session and an anti-CSRF token unless it explicitly says otherwise.
- Every write route declares a request size limit, and every route that receives an id is tested against another user's id: it must answer exactly like an unknown id.
- A schema change starts in the MPD (`Docs/merise/mpd.md`), then goes into the script and its SQL tests, and into a new numbered migration in `database/migrations` that brings existing databases up to date; `database/tests/test-migrations.sh` proves a migrated database matches the MPD.
- No secret in the code or in Git: user-secrets in development, environment variables in production.


## Project status

The API covers every business rule of the project: accounts and two-factor authentication, bank connections, transactions and categories, charts, bags, savings suggestions, alerts, support and administration, data export and account deletion. The website, the mobile app and the production setup come next. The API keeps part of its security state in memory, such as the logins waiting for their code and the rate limit counters, so it runs as a single instance: at startup it takes a PostgreSQL advisory lock, and a second instance on the same database refuses to start. In development it writes e-mails to a local folder; elsewhere it sends them through the SMTP server of its settings.


## License

Proprietary software: all rights reserved. See [LICENSE](LICENSE).
