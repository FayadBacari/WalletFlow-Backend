# Stratégie de sécurité de WalletFlow

Ce document décrit comment WalletFlow se protège, en s'appuyant sur les règles de gestion (`gestion-rules.md`).

L'objectif est de fermer tous les axes d'attaque réalistes pour une application qui manipule des données financières, pour un projet de cette taille. La référence suivie est l'OWASP Top 10.


## 1. Ce qu'on protège et contre qui

**Ce qu'on protège :**
- les comptes des utilisateurs (accès, identité) ;
- leurs données financières (comptes, transactions, sacs, suggestions) ;
- l'accès en lecture à leurs comptes bancaires, que chacun autorise auprès de sa banque ;
- l'intégrité des montants (un sac ne doit jamais devenir faux) ;
- la confiance dans les e-mails envoyés par WalletFlow.

**Contre qui :**
- des robots qui testent des mots de passe en masse ;
- un utilisateur curieux qui essaie de voir les données d'un autre ;
- quelqu'un qui envoie du contenu piégé (libellé, nom de sac, message au support) ;
- un compte administrateur compromis ;
- une fuite accidentelle (sauvegarde, journal, dépendance vulnérable).

### Carte des menaces

| Menace                                    | Exemple dans WalletFlow                                                          | Parade   |
|-------------------------------------------|----------------------------------------------------------------------------------|----------|
| Mots de passe testés en masse             | Un robot essaie des milliers de couples e-mail / mot de passe                    | §2 et §6 |
| Accès aux données d'un autre              | Changer `/sacs/12` en `/sacs/13` dans l'adresse                                  | §4       |
| Injection SQL                             | La recherche par libellé                                                         | §5       |
| Script injecté (XSS)                      | Un libellé ou un message au support qui contient du code                         | §5       |
| Action forcée (CSRF)                      | Un site piégé qui déclenche un déplacement d'argent ou une suppression de compte | §6       |
| Vol de session                            | Un cookie de session récupéré                                                    | §3       |
| Détournement des liens envoyés par e-mail | Rejouer un lien de récupération du mot de passe                                  | §2       |
| Compte administrateur compromis           | Accès à tous les comptes utilisateurs                                            | §8       |
| Connexion bancaire détournée              | Un lien piégé fait autoriser l'accès à sa banque au profit d'un autre compte     | §7 et §9 |
| Clé du prestataire bancaire volée         | La clé d'API, qui ouvre les données bancaires de tous les utilisateurs           | §7       |
| Fuite de données                          | Une base, une sauvegarde ou un journal exposé                                    | §7       |
| Montants incohérents                      | Deux déplacements simultanés qui rendent un sac négatif                          | §5       |
| Dépendance vulnérable                     | Une bibliothèque avec une faille connue                                          | §10      |
| Faux e-mails au nom de WalletFlow         | Hameçonnage                                                                      | §9       |
| Spam et saturation                        | Le formulaire de support ouvert aux visiteurs                                    | §6       |


## 2. Authentification

### Mots de passe
- Au moins 12 caractères, dont une majuscule, une minuscule, un chiffre et un caractère spécial (règle de gestion, conforme à la recommandation de la CNIL).
- 128 caractères au maximum, pour éviter qu'un mot de passe géant ne surcharge le serveur au moment du hachage.
- Le mot de passe est stocké haché avec **Argon2id**, ou **bcrypt** (coût d'au moins 12) si Argon2id n'est pas disponible. Il n'est jamais stocké en clair ni chiffré de façon réversible.
- Le mot de passe est normalisé en Unicode NFC avant les règles et le hachage : tapé sur deux appareils différents, il donne le même résultat.
- Un mot de passe qui figure dans une fuite de données connue est refusé, à l'inscription, au changement et à la récupération, comme le demande le NIST (SP 800-63B) : les attaquants essaient ces mots de passe en premier, sur tous les sites. La recherche passe par Pwned Passwords sans que le mot de passe quitte le serveur : seuls les 5 premiers caractères de son empreinte SHA-1 partent (k-anonymat), et le service complète sa réponse de fausses lignes, pour que sa taille ne trahisse rien. S'il ne répond pas dans les 3 secondes, le mot de passe est accepté, et la panne est notée dans le journal technique.
- Les calculs Argon2id passent par une file d'attente bornée : quand elle est pleine, ou après 5 secondes d'attente, le serveur répond « très demandé » (503, avec `Retry-After`), sans rien calculer ni compter d'essai. Une rafale de requêtes ne peut ni saturer le serveur, ni bloquer de vrais comptes.

### Ne pas révéler quels comptes existent
- Connexion : « Identifiants incorrects », que l'adresse existe ou non (règle de gestion). Le message qui invite à confirmer l'adresse n'apparaît qu'avec le bon mot de passe.
- Mot de passe oublié : « Si un compte existe avec cette adresse, un e-mail vient d'être envoyé. »
- Pour la récupération et le déblocage, une panne d'envoi n'est notée que dans le journal technique : l'annoncer révélerait que le compte existe.
- Inscription : le même message de succès s'affiche, que l'adresse soit libre ou déjà utilisée. Si elle est déjà confirmée, un e-mail prévient son propriétaire. Sinon, l'e-mail contient un nouveau lien de confirmation. Une panne d'envoi donne le même message d'erreur pour toutes les adresses.
- Changement d'adresse : le même message s'affiche, que la nouvelle adresse soit libre ou déjà utilisée. Si elle est déjà utilisée, un e-mail prévient son propriétaire. Les e-mails partent en arrière-plan : une panne d'envoi n'est notée que dans le journal technique.
- Blocage : le message affiché reste générique. L'information « compte bloqué » n'est envoyée qu'au vrai propriétaire : par l'e-mail de déblocage, et par l'alerte de sécurité sur le canal qu'il a choisi.

### Blocage après échecs
- 5 tentatives par compte, puis blocage de 15 minutes, dont la durée double à chaque nouveau blocage, sans dépasser 24 heures (règle de gestion).
- Un compte non confirmé n'est jamais bloqué : ses essais ne comptent pas, et aucun e-mail de déblocage ne part.
- Le lien de déblocage reçu par e-mail remet seulement le compteur à zéro : il ne connecte pas l'utilisateur.
- Un seul e-mail de déblocage est envoyé par blocage, pour qu'un attaquant ne puisse pas inonder la boîte de la victime.
- Ce blocage par compte est complété par une limitation par adresse IP (§6), qui arrête les robots avant qu'ils ne bloquent les comptes de vrais utilisateurs.

### Double authentification
- La double authentification se fait avec une **application d'authentification** (codes TOTP). Le code par e-mail n'est pas proposé : si la boîte mail était piratée, l'attaquant recevrait à la fois le lien de récupération et le code.
- À l'activation, l'utilisateur reçoit **10 codes de secours** à usage unique. Sans eux, la perte du téléphone bloque le compte. Ils sont stockés hachés, comme les mots de passe.
- Le secret TOTP de chaque utilisateur est chiffré dans la base de données.
- Un code vaut pour la période de 30 secondes en cours, la précédente et la suivante, et ne sert qu'une seule fois : le pas de temps du dernier code accepté est gardé en base, et noté dans la même transaction que l'action qu'il autorise. Un code déjà utilisé, ou plus ancien, est refusé, même après un redémarrage ou sur un autre serveur ; de deux requêtes avec le même code, une seule passe. Le code de l'activation compte comme déjà utilisé.
- Après le bon mot de passe, la connexion attend le code 5 minutes au plus, sans ouvrir de session. 5 mauvais codes obligent à recommencer la connexion.
- Le secret est chiffré en AES-256-GCM. Sa clé est rangée avec les autres secrets, et peut être changée sans perdre les comptes (§7).
- Si l'utilisateur a perdu son téléphone et ses codes de secours, seul l'administrateur peut faire envoyer à l'adresse du compte un lien qui désactive la double authentification (règle de gestion). Ce lien demande le mot de passe, et ne marche que si celui-ci n'a pas changé depuis au moins 7 jours, d'après le journal (§11) : un voleur de la boîte mail qui vient de réinitialiser le mot de passe ne peut pas s'en servir. Chaque essai du mot de passe compte avant sa vérification : après 5 mauvais mots de passe, le lien est annulé, et il faut en redemander un au support. Un voleur de la boîte mail ne peut donc pas essayer plus de 5 mots de passe par lien, même depuis plusieurs adresses IP à la fois. La désactivation ferme toutes les sessions du compte, et une alerte de sécurité part (§9).

### Liens envoyés par e-mail (jetons)
Tous les liens (confirmation, récupération, déblocage, changement d'e-mail, désactivation de la double authentification) suivent les mêmes règles :
- jeton aléatoire d'au moins 128 bits, généré par un générateur cryptographique ;
- seule son empreinte (SHA-256) est stockée en base, jamais le jeton lui-même ;
- usage unique, et invalidé dès qu'un nouveau jeton du même type est demandé ;
- le lien ouvre une page avec un bouton de confirmation : l'action n'a lieu qu'au clic. Sinon, les antivirus de messagerie qui ouvrent les liens automatiquement « consomment » le jeton à la place de l'utilisateur ;
- la page de confirmation de l'inscription demande aussi le mot de passe : ouvrir le lien sans le connaître n'active rien ;
- la page de confirmation d'un changement d'adresse demande aussi le mot de passe : une faute de frappe ne donne pas le compte à un inconnu ;
- la page de désactivation de la double authentification demande aussi le mot de passe : la boîte mail seule ne suffit pas ;
- confirmer une nouvelle adresse annule les liens de récupération, de déblocage et de désactivation de la double authentification encore valables : ils sont partis vers l'ancienne.

| Lien                                  | Durée de validité             |
|---------------------------------------|-------------------------------|
| Confirmation de l'inscription         | 24 heures                     |
| Récupération du mot de passe          | 30 minutes (règle de gestion) |
| Déblocage du compte                   | 1 heure                       |
| Confirmation d'un changement d'e-mail | 24 heures                     |
| Désactivation de la double auth.      | 24 heures                     |

### Actions sensibles
Ces actions redemandent le mot de passe, même si l'utilisateur est déjà connecté :
- changer de mot de passe ou d'adresse e-mail (règle de gestion) ;
- supprimer son compte (règle de gestion) ;
- télécharger ses données (règle de gestion) ;
- activer ou désactiver la double authentification (règle de gestion).

Si la double authentification est activée, changer de mot de passe ou d'adresse e-mail, télécharger ses données ou supprimer son compte demande aussi un code.

Les mauvais mots de passe saisis ici ne comptent pas pour le blocage du compte : un voleur de session pourrait sinon le bloquer.

À la désactivation de la double authentification, un mauvais mot de passe et un mauvais code reçoivent le même message.


## 3. Sessions

- Les sessions sont gérées côté serveur. L'identifiant de session est stocké dans un cookie, jamais dans le `localStorage` du navigateur, où un script injecté pourrait le lire.
- Le cookie porte les attributs `HttpOnly`, `Secure` et `SameSite=Lax`, avec le préfixe `__Host-`.
- Un nouvel identifiant de session est généré à chaque connexion, pour empêcher qu'un attaquant n'impose le sien à la victime.
- Les identifiants de session et les jetons d'appareil ne sont stockés en base que sous forme d'empreinte (SHA-256).
- La session expire après 30 minutes d'inactivité (règle de gestion).
- Elle expire aussi 12 heures après la connexion, même active (OWASP ASVS, niveau 2) : un cookie volé ne sert pas indéfiniment.
- Un compte garde au plus 10 sessions : la connexion de trop ferme la moins récemment active. Chaque jour, une tâche efface les sessions expirées.
- La déconnexion détruit la session côté serveur, pas seulement le cookie.
- Après un changement **ou une récupération** de mot de passe, toutes les autres sessions sont fermées.
- Après un changement de mot de passe, la session en cours reçoit aussi un nouvel identifiant : une copie volée de son cookie ne sert plus.
- « Nouvel appareil » : un jeton d'appareil aléatoire et durable est enregistré à la première connexion, dans un cookie sur le web et dans le stockage sécurisé du téléphone dans l'application mobile. Une connexion sans ce jeton déclenche l'alerte de sécurité.
- Un compte garde au plus 20 appareils : un nouvel appareil de trop fait oublier le moins récemment utilisé. Chaque jour, les appareils inutilisés depuis un an, dont le cookie a expiré, sont oubliés, sauf le plus récent de chaque compte : sans lui, la connexion suivante passerait pour la première, et l'alerte ne partirait pas.


## 4. Contrôle d'accès

C'est **le risque numéro un** de WalletFlow : chaque utilisateur ne doit voir et modifier que ses propres données.

- L'identifiant de l'utilisateur vient **toujours de la session**, jamais d'un paramètre envoyé par le navigateur (adresse, formulaire, corps de requête).
- Chaque accès à une ressource vérifie qu'elle appartient à l'utilisateur connecté. Cela concerne les connexions bancaires, les comptes, les transactions, les catégories, les sacs, les mouvements, les suggestions et les demandes de support.
- La vérification porte aussi sur **tous les identifiants d'une même requête**. Par exemple :
  - un déplacement d'argent vérifie que le sac de départ **et** le sac d'arrivée appartiennent à l'utilisateur ;
  - changer la catégorie d'une transaction vérifie que la transaction **et** la catégorie lui appartiennent.
- Une ressource qui appartient à quelqu'un d'autre renvoie « introuvable » (404), et non « interdit » (403), pour ne pas confirmer qu'elle existe.
- Seuls les champs prévus peuvent être modifiés (liste blanche). Un utilisateur ne doit jamais pouvoir modifier lui-même son rôle, l'état de confirmation de son e-mail ou son compteur de tentatives, en ajoutant un champ à sa requête.
- Le rôle administrateur est vérifié côté serveur sur chaque route d'administration. Masquer un bouton dans l'interface ne suffit pas.
- Les identifiants visibles dans les adresses sont des UUID plutôt que des numéros qui se suivent. Cela ne remplace pas les vérifications ci-dessus, mais rend les essais au hasard inutiles.
- Un test automatique vérifie, pour chaque route, qu'un utilisateur B ne peut ni lire ni modifier les données d'un utilisateur A.


## 5. Données entrantes et intégrité

### Validation
Toute donnée est validée **côté serveur**, même si l'interface la valide déjà. Les données reçues du prestataire bancaire sont validées de la même façon.

| Donnée                                | Règle                                                                                                        |
|---------------------------------------|--------------------------------------------------------------------------------------------------------------|
| Montant                               | Nombre entier en **centimes** (jamais de nombre à virgule), au plus 1 000 000 000 centimes en valeur absolue |
| Montant d'une transaction             | Jamais nul                                                                                                   |
| Libellé de transaction                | 1 à 255 caractères                                                                                           |
| Nom de compte, de sac ou de catégorie | 1 à 50 caractères                                                                                            |
| Date d'une transaction                | À partir du 01/01/2000                                                                                       |
| Adresse e-mail                        | Format valide, 254 caractères au plus                                                                        |
| Message au support ou réponse         | 1 à 5 000 caractères, sans caractère invisible autre que le retour à la ligne                                |

### Injections
- **SQL** : uniquement des requêtes paramétrées ou un ORM, jamais de requête construite en collant des morceaux de texte. Pour la recherche par libellé, les caractères spéciaux `%` et `_` saisis par l'utilisateur sont neutralisés.
- **Scripts (XSS)** : les libellés et les noms de comptes reçus de la banque, les noms de sacs et de catégories, et les messages au support sont du contenu non fiable.
  - Ils sont toujours affichés comme du texte, grâce à l'échappement automatique du framework.
  - On n'injecte jamais de HTML brut (`innerHTML`, `v-html`, `dangerouslySetInnerHTML`…).
  - Les infobulles des graphiques sont configurées en texte et non en HTML : beaucoup de bibliothèques de graphiques acceptent le HTML par défaut.

### Intégrité des montants
- Les règles sur les sacs (jamais négatif, total ≤ solde total) sont appliquées **par le serveur**, dans la transaction de base de données du déplacement. Elle réécrit la ligne de l'utilisateur en vérifiant sa version : de deux déplacements lancés en même temps, le second échoue et refait ses contrôles. Deux déplacements ne peuvent donc pas contourner la règle.
- Accepter une suggestion ne crée qu'un seul mouvement, même en cas de double clic.

### Export de données
- L'export est un fichier JSON. Il ne contient aucun secret : ni empreinte de mot de passe, ni secret de double authentification, ni code de secours, ni jeton, ni identifiant de session. Il ne contient pas non plus le journal de sécurité, qui peut garder l'adresse IP d'un inconnu.
- L'export se télécharge directement depuis l'application. Il n'est jamais envoyé par e-mail.
- Un mauvais mot de passe compte aussi : une session volée ne peut pas en essayer plus de 3 par heure.


## 6. Protection des requêtes

### Limitation de débit
Les valeurs ci-dessous sont des points de départ, à ajuster selon l'usage réel.

| Point d'entrée                                         | Limite                                                                                   |
|--------------------------------------------------------|------------------------------------------------------------------------------------------|
| Connexion                                              | 10 essais par minute et par adresse IP, en plus des 5 tentatives par compte              |
| Inscription                                            | 5 par heure et par adresse IP, 3 par heure et par adresse e-mail                         |
| Confirmation de l'inscription                          | 10 essais par heure et par adresse IP                                                    |
| Mot de passe oublié                                    | 3 par heure et par adresse e-mail, 10 par heure et par adresse IP                        |
| Lien de récupération                                   | 10 essais par heure et par adresse IP                                                    |
| Lien de déblocage                                      | 10 essais par heure et par adresse IP                                                    |
| Code de double authentification                        | 5 essais, puis il faut recommencer la connexion ; 10 essais par minute et par adresse IP |
| Réglage de la double auth.                             | 10 par heure et par adresse IP                                                           |
| Changement de mot de passe                             | 10 par heure et par adresse IP                                                           |
| Changement d'adresse e-mail                            | 3 par heure et par nouvelle adresse, 10 par heure et par adresse IP                      |
| Suppression du compte                                  | 10 par heure et par adresse IP                                                           |
| Lien de changement d'adresse                           | 10 essais par heure et par adresse IP                                                    |
| Lien de désactivation de la double auth.               | 10 essais par heure et par adresse IP                                                    |
| Formulaire de support (visiteur)                       | 3 par heure et par adresse IP                                                            |
| Demande de support (utilisateur)                       | 10 sur 24 heures glissantes, par utilisateur                                             |
| Téléchargement des données                             | 3 essais par heure et par utilisateur, bons ou mauvais                                   |
| Mise à jour des comptes                                | 1 par minute et par utilisateur                                                          |
| Recherche, graphiques, sacs et suggestions             | 60 par minute et par session, toutes ces lectures ensemble                               |
| Banque : liste, connexion, renouvellement, déconnexion | 20 par heure et par session, toutes ensemble                                             |
| Toutes les requêtes                                    | 600 par minute et par adresse IP, en plus des limites ci-dessus                          |

Au-delà de la limite, le serveur répond « trop de requêtes » (429).

Une adresse IPv6 compte pour tout son bloc /64 : un abonné en reçoit un entier, et pourrait changer d'adresse à chaque requête pour échapper aux limites. Une adresse IPv4 vue en IPv6 redevient une adresse IPv4.

Derrière un proxy inverse, l'adresse du client et le protocole d'origine ne sont lus dans les en-têtes `X-Forwarded-For` et `X-Forwarded-Proto` que si la requête vient d'un proxy déclaré (réglage `ReverseProxy` : des adresses ou des réseaux) ou de la même machine. Sinon, n'importe qui choisirait l'adresse sous laquelle il est compté par les limites et noté au journal. Une déclaration illisible empêche le serveur de démarrer.

Un utilisateur a au plus 100 catégories, les 8 par défaut comprises : un script ne peut pas remplir la base en créant des catégories sans fin.

Un utilisateur a au plus 100 sacs, et fait au plus 1 000 déplacements d'argent sur les dernières 24 heures : un script ne peut pas remplir la base en créant des sacs ou des mouvements sans fin. Au-delà des 1 000 déplacements, le serveur répond « trop de requêtes » (429).

Une période de graphique couvre au plus 10 ans : une requête ne peut pas faire calculer des milliers de mois.

### Requêtes abandonnées
Une requête abandonnée par son client, ou interrompue par l'arrêt du serveur, arrête son travail : lectures en base, calculs et appels aux prestataires. Seules vont jusqu'au bout les écritures qui ne doivent pas se perdre : le journal de sécurité, les essais comptés contre le blocage, et ce qui suit un effet déjà produit chez un prestataire ou un e-mail déjà parti. L'arrêt du serveur n'est pas noté comme une panne des tâches de fond.

### Actions forcées (CSRF)
- Les cookies en `SameSite=Lax`, plus un jeton anti-CSRF sur toutes les requêtes qui modifient des données. Les frameworks le fournissent.
- Les clés qui signent ce jeton sont gardées dans un dossier qui survit aux redéploiements (réglage `DataProtection:KeysFolder`), sur le disque chiffré du serveur, lisible seulement par l'API. Elles y sont aussi chiffrées avec un certificat X.509 (réglages `DataProtection:CertificatePath` et `DataProtection:CertificateKeyPath`) : un fichier de clés copié ne sert à rien sans la clé privée du certificat, gardée à part. Sans ces réglages, l'API refuse de démarrer hors du développement : chaque redéploiement perdrait les clés, et refuserait tous les jetons en cours.
- Sur les routes d'administration, le jeton anti-CSRF est vérifié avant le rôle : une écriture sans jeton est refusée sans lire le compte ni rien noter au journal. L'adresse d'un compte cherché passe dans le corps de la requête, jamais dans son adresse (§7).
- Les routes appelées sans session (inscription, confirmation, connexion) n'ont pas besoin de ce jeton : elles n'agissent au nom de personne, et n'acceptent que du JSON, qu'un site piégé ne peut pas envoyer sans autorisation.
- Aucune requête `GET` ne modifie de données. Les suggestions, par exemple, sont calculées par la tâche horaire, jamais par leur lecture.

### Origines (CORS)
- Si l'interface et le serveur sont sur le même domaine, aucune règle CORS n'est nécessaire.
- Sinon, seule l'origine exacte de l'interface est autorisée, jamais `*`.
- En production, le serveur ne répond qu'aux noms du site (réglage `AllowedHosts`) : une requête sous un autre nom est refusée.

### En-têtes de sécurité

| En-tête                        | Valeur                                                                                                                  |
|--------------------------------|-------------------------------------------------------------------------------------------------------------------------|
| `Strict-Transport-Security`    | `max-age=31536000; includeSubDomains`                                                                                   |
| `Content-Security-Policy`      | `default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'` |
| `X-Content-Type-Options`       | `nosniff`                                                                                                               |
| `Referrer-Policy`              | `strict-origin-when-cross-origin`                                                                                       |
| `Permissions-Policy`           | `camera=(), microphone=(), geolocation=()`                                                                              |
| `Cross-Origin-Resource-Policy` | `same-origin`                                                                                                           |
| `Cache-Control`                | `no-store`                                                                                                              |

Aucune réponse n'est gardée par un navigateur ou un intermédiaire : toutes contiennent des données personnelles, des données financières, un jeton ou une erreur. Les réponses ne nomment pas non plus le serveur web : aucun en-tête `Server`.

Si une bibliothèque (de graphiques, par exemple) a besoin de styles en ligne, on peut ajouter `style-src 'self' 'unsafe-inline'`. C'est acceptable pour les styles, mais jamais pour les scripts.

### Scripts tiers
Aucun script tiers (publicité, traceur, outil d'analyse externe) sur les pages où l'utilisateur est connecté : chacun d'eux pourrait lire ses données financières. Les bibliothèques sont hébergées avec l'application, pas chargées depuis un CDN.


## 7. Protection des données

### Minimisation
- WalletFlow n'enregistre **ni IBAN ni numéro de compte**, même si le prestataire les fournit : un compte n'a besoin que de son nom et de son solde. Une donnée qu'on ne stocke pas ne peut pas fuiter.
- WalletFlow ne voit jamais les identifiants ni le mot de passe de la banque : l'utilisateur s'identifie sur la page de sa banque (règle de gestion).
- Une alerte envoyée n'est gardée que pour ne pas l'envoyer deux fois : une alerte de budget ou de suggestion est effacée deux mois après son mois, une alerte de sac ou de connexion bancaire part avec eux.
- Une demande de la file d'e-mails est effacée dès que l'e-mail est parti, ou après son dernier essai raté.
- Une demande de support est effacée un an après son envoi, avec ses réponses (règle de gestion).

### Connexion bancaire
- Le prestataire, Enable Banking, est enregistré auprès de l'autorité financière finlandaise pour lire les comptes (DSP2), et établi dans l'Union européenne. Il est responsable de ses propres traitements, et non sous-traitant : l'utilisateur accepte ses conditions et lui donne son consentement (registre des traitements, §2). Ses conditions gratuites ne couvrent que l'usage personnel du titulaire du compte développeur : ouvrir WalletFlow au public demande un contrat écrit avec lui.
- Seul l'accès en lecture est demandé : WalletFlow ne peut lancer ni virement ni paiement (règle de gestion).
- La connexion se lance toujours depuis WalletFlow. Au retour de la banque, un paramètre `state` aléatoire, à usage unique et lié au compte qui a lancé la connexion, est vérifié, et l'adresse de retour est fixe.
- La clé d'API du prestataire reste sur le serveur, avec les autres secrets. Elle n'est jamais intégrée à l'application mobile.
- Si le prestataire fournit un jeton d'accès pour chaque utilisateur, ce jeton est chiffré en base, comme le secret de double authentification.
- Le prestataire n'appelle jamais WalletFlow (aucun webhook) : WalletFlow lit lui-même les comptes. Aucune route publique ne lui est ouverte.
- Les données reçues sont validées comme des données saisies (§5).
- Déconnecter une banque supprime ses comptes et leurs transactions, et révoque l'autorisation chez le prestataire (règle de gestion).

### Chiffrement
- **En transit** : HTTPS partout (TLS 1.2 minimum), avec redirection automatique de HTTP vers HTTPS.
- **Au repos** : base de données et sauvegardes sur des disques chiffrés par l'hébergeur. Les sauvegardes sont en plus chiffrées avant d'être envoyées.
- **Champ par champ** : seuls les secrets de double authentification, et le jeton d'accès du prestataire s'il y en a un, sont chiffrés individuellement. Chiffrer chaque montant ou chaque libellé compliquerait beaucoup l'application pour un gain faible.
- **Changement de clé** : chaque secret de double authentification chiffré porte le numéro de sa clé. La nouvelle clé devient la clé actuelle ; l'ancienne reste dans la configuration, pour déchiffrer seulement. Au démarrage puis chaque jour, une tâche rechiffre les secrets avec la clé actuelle. Quand le journal technique ne compte plus d'échec, l'ancienne clé est retirée : volée, elle ne déchiffre alors plus rien.

### Hébergement et base de données
- Hébergement dans l'Union européenne.
- La base de données n'est pas accessible depuis internet, seulement depuis le serveur de l'application.
- L'application se connecte à la base avec un utilisateur aux droits limités : il peut lire et écrire les données, mais pas supprimer des tables ni administrer la base.

### Secrets
- Les clés, mots de passe de base de données et clés d'API sont dans des variables d'environnement ou un gestionnaire de secrets, **jamais dans le code** ni dans Git.
- Le fichier `.env` est ignoré par Git.
- Chaque environnement (développement, production) a ses propres secrets.
- En cas de doute sur une fuite, tous les secrets sont changés.

### Sauvegardes
- Une sauvegarde par jour, conservée 30 jours.
- La restauration est testée avant la mise en production, puis une fois par trimestre. Une sauvegarde jamais testée n'est pas une sauvegarde.
- La sauvegarde est chiffrée au fil de sa lecture, avec le certificat public des sauvegardes (CMS, AES-256-GCM) : elle n'existe jamais en clair sur le disque. Le serveur n'a que ce certificat : il chiffre, mais ne peut pas déchiffrer. La clé privée reste hors du serveur, protégée par une phrase secrète, et n'est apportée que le temps d'une restauration (scripts de `database/backup`).
- La clé du secret de double authentification et la clé privée du prestataire bancaire sont gardées à part, dans le gestionnaire de secrets, jamais avec les sauvegardes : une base restaurée sans la première ne permet plus d'utiliser la double authentification.
- Une restauration se fait toujours dans une base neuve, jamais par-dessus la base en service, et en une seule transaction. Un fichier modifié, même d'un seul bit, est refusé avant toute restauration.
- Restaurer une sauvegarde ancienne ne ressuscite pas durablement un compte effacé depuis : sa demande de suppression y figure, et la tâche horaire de l'API l'efface de nouveau.
- L'intégration continue fait l'aller-retour complet à chaque envoi : sauvegarde, restauration dans une base neuve, comparaison avec la source et tests SQL, puis chaque refus de la restauration.

### Suppression de compte
- Pendant les 30 jours de délai (règle de gestion), le compte est inaccessible, sauf pour annuler la suppression : la demande ferme toutes ses sessions et annule ses liens en attente, et seule une connexion réussie le rouvre, en annulant la suppression. La récupération du mot de passe et le déblocage restent possibles, pour pouvoir revenir. Les tâches de fond ne touchent plus au compte.
- Dès la demande, l'autorisation donnée à la banque est révoquée chez le prestataire (règle de gestion).
- Si le prestataire ne répond pas, la demande est acceptée quand même : la révocation est réessayée en arrière-plan, puis une dernière fois avant l'effacement. Une connexion qui annule la suppression arrête ces essais : la banque reste connectée.
- Passé ce délai, sont effacés : l'utilisateur, ses connexions bancaires, ses comptes bancaires et leurs transactions, ses catégories et ses règles de classement, ses sacs et leurs mouvements, ses suggestions, ses préférences d'alerte, ses alertes envoyées, ses e-mails en attente, ses demandes de support et leurs réponses, ses appareils, ses sessions, ses jetons et ses codes de secours. Dans le journal de sécurité, ses événements restent, mais sans son identifiant ni son adresse IP, et les actions d'un administrateur sur son compte ne le désignent plus.
- Les données disparaissent des sauvegardes au plus tard 30 jours plus tard, quand la dernière sauvegarde qui les contient expire. C'est à indiquer dans la politique de confidentialité.

### Journaux
Les journaux techniques ne contiennent jamais de mot de passe, de jeton, de code de double authentification, d'identifiant de session, de montant ni de libellé. Ils désignent l'utilisateur par son identifiant, pas par son adresse e-mail.


## 8. Administration et support

- L'administrateur **ne voit ni les comptes, ni les transactions, ni les soldes, ni les sacs** des utilisateurs. Il voit uniquement :
  - l'adresse e-mail ;
  - l'état du compte (non confirmé, actif, bloqué, en cours de suppression), et si la double authentification est activée ;
  - les demandes de support ;
  - des statistiques globales et anonymes : les comptes par état, les connexions bancaires actives et les demandes de support par état, sans aucun chiffre sur l'argent.
- L'administrateur retrouve un compte par son adresse exacte : il ne peut pas parcourir la liste des utilisateurs.
- L'administrateur doit avoir activé la double authentification : sans elle, les routes d'administration répondent « interdit » (403). Un utilisateur qui n'est pas administrateur reçoit aussi « interdit », et son essai est noté comme un accès refusé. Un administrateur sans double authentification n'est pas noté. Le rôle et la double authentification sont relus en base à chaque requête.
- L'administrateur peut débloquer un compte, et faire envoyer à l'adresse du compte le lien qui désactive la double authentification (§2), sans voir ce lien ni le mot de passe. Il ne peut pas le faire envoyer si le mot de passe a changé il y a moins de 7 jours : il en est prévenu, et la règle est vérifiée de nouveau quand le lien sert. Il ne peut ni changer le mot de passe ou l'e-mail d'un utilisateur, ni se connecter à sa place.
- Un compte administrateur ne se crée pas depuis l'application. Seul le responsable du projet attribue ou retire le rôle, avec la fonction `fn_changer_role` de la base, lancée sur le serveur avec le compte administrateur de PostgreSQL, à un compte dont l'adresse e-mail est confirmée. Le changement est écrit au journal (§11).
- Chaque action d'un administrateur qui change quelque chose, et chaque recherche d'un compte, même sans résultat, est enregistrée dans le journal, avec le compte qu'elle vise (§11). Lire les demandes et les statistiques n'y est pas noté. Un déblocage par l'administrateur est noté deux fois : le déblocage du compte et l'action de l'administrateur.
- Les messages au support, surtout ceux des visiteurs, sont du contenu non fiable. Ils sont affichés comme du texte dans l'interface d'administration (§5) : un script caché dans un message viserait directement l'administrateur.


## 9. E-mails et notifications

- Le domaine d'envoi est configuré avec **SPF, DKIM et DMARC**. Il devient alors beaucoup plus difficile d'envoyer de faux e-mails au nom de WalletFlow.
- Les e-mails ne contiennent jamais de mot de passe, ni de solde, ni de liste de transactions.
- Les liens pointent uniquement vers le domaine de WalletFlow, en HTTPS.
- L'e-mail qui rappelle de renouveler l'autorisation bancaire ne contient aucun lien vers une banque : il invite seulement à ouvrir WalletFlow. Un e-mail qui propose de reconnecter sa banque en un clic est exactement ce qu'imiterait un hameçonnage.
- Chaque e-mail de sécurité (connexion depuis un nouvel appareil, changement de mot de passe ou d'e-mail, blocage, désactivation de la double authentification, utilisation d'un code de secours, annulation d'une suppression) indique quoi faire si ce n'est pas l'utilisateur qui a agi. Quand il invite à écrire au support, il donne le lien de la page de support.
- L'e-mail qui prévient l'ancienne adresse ne cite pas la nouvelle, et invite à écrire au support, avec le lien de la page de support, si ce n'est pas l'utilisateur qui a agi.
- Une réponse du support part par e-mail avec son texte, à l'adresse du compte ou à l'adresse de contact du visiteur (règle de gestion). Elle part en texte brut : un message piégé qu'elle citerait ne devient pas du HTML.
- La demande de suppression, son annulation et l'effacement sont confirmés par e-mail (règle de gestion). L'e-mail d'effacement garde l'adresse du compte effacé jusqu'à son envoi, et rien d'autre.
- Le contenu des notifications sur téléphone reste général (par exemple « Ton budget Loisirs est presque atteint ») : il peut s'afficher sur un écran verrouillé.
- Une alerte ne cite aucun montant, par e-mail comme sur le téléphone (règle de gestion).
- Tant que l'application mobile n'existe pas, toutes les alertes partent par e-mail (règle de gestion).
- En développement seulement, les e-mails sont écrits dans un dossier local, ignoré par Git, même si un serveur SMTP est configuré. Ailleurs, aucun e-mail n'est jamais écrit sur le disque : leurs liens ouvrent un compte. Ils partent par le serveur SMTP d'un prestataire hébergé dans l'Union européenne, lié à WalletFlow par un contrat de sous-traitance. Sans réglages SMTP complets, le serveur refuse de démarrer hors du développement.
- La connexion au serveur SMTP est toujours chiffrée, en TLS 1.2 au moins : dès la connexion sur le port 465, par STARTTLS sur les autres. Un serveur qui ne propose pas STARTTLS, ou dont le certificat n'est pas reconnu, est refusé avant que le mot de passe du compte d'envoi parte. Ce mot de passe est un secret, comme les autres (§7).
- La file d'e-mails est gardée en base, pour qu'un redémarrage du serveur ne perde rien. Elle ne contient aucun secret : chaque lien est créé au moment de l'envoi. Un e-mail part en 5 essais au plus : après un échec, l'essai suivant attend 1 min, 5 min, 30 min, puis 2 h. Le dernier échec est noté dans le journal technique. Les limites du §6 bornent ce qu'un visiteur peut y mettre.


## 10. Développement et dépendances

- Le fichier de verrouillage des dépendances (`package-lock.json`, `composer.lock`…) est versionné.
- Les dépendances sont analysées automatiquement (Dependabot, Renovate ou l'audit du gestionnaire de paquets). Une faille critique est corrigée dans la semaine.
- Un outil de détection de secrets (par exemple gitleaks) tourne avant chaque commit ou dans l'intégration continue.
- Un outil d'analyse du code (par exemple CodeQL) cherche les failles à chaque envoi, dans l'intégration continue. Une alerte est corrigée, ou expliquée si c'est une fausse alerte, avant la mise en production.
- L'image Docker est scannée par Trivy, et l'API en marche attaquée par OWASP ZAP, à chaque envoi dans l'intégration continue. Une alerte est corrigée, ou écartée avec sa raison si c'est une fausse alerte, avant la mise en production.
- Les données de production ne sont jamais copiées en développement : on utilise de fausses données.
- En production, le mode débogage est désactivé. Les erreurs affichées à l'utilisateur restent génériques ; le détail ne va que dans les journaux.
- Chaque modification de l'authentification ou du contrôle d'accès est relue en vérifiant les §2 à §4.

### Application mobile
- Le jeton de session est stocké dans le trousseau sécurisé du téléphone (Keychain sur iOS, Keystore sur Android), pas dans un stockage simple.
- Aucun secret n'est intégré dans l'application : tout ce qui est dans l'application peut être extrait.
- Le serveur applique exactement les mêmes contrôles qu'en version web.


## 11. Journalisation et détection

**Événements enregistrés :**
- connexions réussies et échouées, blocages et déblocages ;
- demandes de récupération et changements de mot de passe ;
- changements d'adresse e-mail, activation et désactivation de la double authentification ;
- connexions, renouvellements et déconnexions d'une banque ;
- exports de données et demandes de suppression ;
- actions des administrateurs, avec le compte qu'elles visent, et attribution ou retrait du rôle d'administrateur ;
- tentatives d'accès à une ressource d'un autre utilisateur.

Pour chaque événement, le journal note la date, l'identifiant de l'utilisateur, l'adresse IP, l'événement et son résultat ; pour une action d'un administrateur, aussi le compte qu'elle vise.

**Conservation** : 1 an, dans la fourchette de 6 mois à 1 an que recommande la CNIL. Chaque jour, les événements plus anciens sont effacés par une fonction de la base, la seule à pouvoir effacer le journal. Quand un compte est supprimé, ses événements restent, mais sans son identifiant ni son adresse IP, et les actions qui le visaient ne le désignent plus (§7).

**Santé** : trois points de santé, ouverts sans session. `/health/live` dit seulement que l'API tourne ; `/health/ready` dit aussi que la base répond, en 5 secondes au plus : l'hébergeur s'en sert pour surveiller l'API et la redémarrer si besoin. `/health/tasks` dit si les tâches de fond vont bien : un service de surveillance qui l'interroge alerte quand l'une échoue 3 passages de suite, ou ne termine plus aucun passage. Ils répondent juste « Healthy » ou « Unhealthy » (503), sans aucun détail technique (§10). Hors du développement, ils ne répondent qu'aux noms du site, comme toutes les routes (§6) : la sonde de l'hébergeur envoie l'un de ces noms.


## 12. En cas d'incident

1. **Contenir** : fermer toutes les sessions, changer les secrets, désactiver la fonctionnalité touchée si nécessaire.
2. **Évaluer** : quelles données, combien d'utilisateurs, depuis quand. Les journaux du §11 servent à ça.
3. **Notifier** :
   - la CNIL dans les **72 heures** s'il s'agit d'une violation de données personnelles (article 33 du RGPD) ;
   - les utilisateurs concernés si le risque pour eux est élevé (article 34).
4. **Corriger et documenter** : la cause, le correctif, et un test qui empêche le problème de revenir.


## 13. Ce qu'on ne fait volontairement pas

Pour rester proportionné à la taille du projet :
- pas d'expiration périodique des mots de passe ;
- pas de questions secrètes ;
- pas de cryptographie maison : uniquement des bibliothèques reconnues ;
- pas de chiffrement de chaque champ de la base (§7) ;
- pas de CAPTCHA par défaut, seulement si du spam apparaît sur l'inscription ou le support ;
- pas de pare-feu applicatif payant ni de test d'intrusion externe dans la première version : une auto-évaluation avec la liste du §14 suffit ;
- pas d'épinglage de certificat dans l'application mobile.


## 14. Liste de contrôle avant la mise en production

Chaque point porte son état et sa preuve : le test ou le travail de l'intégration continue qui le démontre à chaque envoi. Un point « Fait » reste prouvé tant que l'intégration continue passe. Un point « Code prêt : à confirmer au déploiement » est prêt dans le code, mais dépend aussi du serveur : il se vérifie pendant la répétition de la mise en production. Un point « Au déploiement » ne dépend pas du code : il se règle avec l'hébergement ou un prestataire.

| Point                                                                                                                                                | État                                   | Preuve                                                                                                                                                                                |
|------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| HTTPS et HSTS actifs, HTTP redirigé vers HTTPS                                                                                                       | Code prêt : à confirmer au déploiement | HSTS et redirection : `ApiInProductionModeTests`. Derrière le proxy, la redirection se fait au proxy, ou avec le réglage `https_port`                                                 |
| En-têtes de sécurité vérifiés (par exemple avec Mozilla Observatory)                                                                                 | Code prêt : à confirmer au déploiement | `SecurityHeadersAssertions` dans les tests des routes, et `AnswersOfTheRealServerNeverNameItAndAreNeverStored` sur le vrai serveur ; reste Mozilla Observatory sur l'adresse publique |
| Mots de passe et codes de secours hachés avec Argon2id                                                                                               | Fait                                   | `LibsodiumPasswordHasherTests`, `LibsodiumBackupCodeHasherTests`                                                                                                                      |
| Limitation de débit active sur la connexion, l'inscription et sa confirmation, la récupération du mot de passe et le support                         | Fait                                   | Les tests « …IsLimitedTo… » de chaque route, `GlobalRateLimitTests`, `CostlyReadRateLimitTests`, `BankRateLimitTests`                                                                 |
| Proxy inverse déclaré dans le réglage `ReverseProxy` : sinon, toutes les limites par adresse IP compteraient l'adresse du proxy                      | Code prêt : à confirmer au déploiement | `ReverseProxyTests` ; l'adresse du proxy se déclare au déploiement                                                                                                                    |
| Envoi réel des e-mails branché : hors du développement, le serveur refuse de démarrer sans lui                                                       | Code prêt : à confirmer au déploiement | `SmtpEmailSenderTests`, `EmailSenderChoiceTests` ; restent le prestataire et un premier envoi réel                                                                                    |
| Accès sortant en HTTPS vers `api.pwnedpasswords.com` ouvert : sinon, les mots de passe divulgués passent, avec un avertissement au journal technique | Code prêt : à confirmer au déploiement | `PwnedPasswordsCheckerTests` ; le pare-feu du serveur doit l'ouvrir                                                                                                                   |
| Test d'accès croisé (utilisateur B vers les données de A) sur chaque route                                                                           | Fait                                   | `ForeignIdRequestTests`, et les tests « …OfAnotherUser… » de chaque route                                                                                                             |
| Aucune requête SQL construite par concaténation de texte                                                                                             | Fait                                   | Requêtes d'EF Core, et `SqlQuery` paramétrées ; CodeQL dans l'intégration continue                                                                                                    |
| Mode débogage désactivé, messages d'erreur génériques                                                                                                | Fait                                   | `ApiInProductionModeTests` : erreur générique, description OpenAPI non publiée ; l'image démarre en mode `Production`                                                                 |
| Aucun secret dans le code, `.env` ignoré par Git, détection de secrets passée                                                                        | Fait                                   | Travail « Recherche de secrets » (gitleaks) de l'intégration continue ; `.env` dans `.gitignore`                                                                                      |
| Clé d'API du prestataire bancaire sur le serveur seulement, absente de l'application mobile                                                          | Fait                                   | Clé privée lue dans un fichier des secrets (`BankProviderSettings`) ; l'application mobile n'existe pas encore                                                                        |
| Paramètre `state` vérifié au retour de la banque                                                                                                     | Fait                                   | `StateOfAnotherUserIsRefusedWithoutUsingTheCode`, `UnknownOrMissingStateIsRefused`, `StateServesOnlyOnce`, `StateExpiresAfterFifteenMinutes`                                          |
| Révocation chez le prestataire testée (déconnexion d'une banque, suppression d'un compte)                                                            | Code prêt : à confirmer au déploiement | Avec le faux prestataire : `DisconnectionRevokesThenDeletesTheBank`, `PassRevokesLateBanksThenErasesDueAccounts` ; reste une révocation réelle dans le bac à sable d'Enable Banking   |
| Contrat écrit avec Enable Banking, qui permet de lire les comptes d'autres personnes que le titulaire du compte développeur                          | Au déploiement                         | Ses conditions gratuites ne couvrent que l'usage personnel de ce titulaire ; le contrat confirme aussi son rôle de responsable distinct et le lieu de ses traitements                 |
| Aucune dépendance avec une faille critique connue                                                                                                    | Fait                                   | Étape « Refuser les paquets qui ont une faille connue » de l'intégration continue ; Dependabot                                                                                        |
| Base de données inaccessible depuis internet, utilisateur de base aux droits limités                                                                 | Code prêt : à confirmer au déploiement | Droits : sections 10, 19 et 23 des tests SQL ; l'isolement du réseau relève de l'hébergement                                                                                          |
| Sauvegardes chiffrées et restauration testée                                                                                                         | Code prêt : à confirmer au déploiement | Travail « Sauvegarde et restauration » de l'intégration continue ; restent la sauvegarde quotidienne planifiée, son stockage hors du serveur et la première restauration réelle       |
| SPF, DKIM et DMARC configurés                                                                                                                        | Au déploiement                         | Enregistrements DNS du domaine d'envoi                                                                                                                                                |
| Journalisation des événements de sécurité active                                                                                                     | Fait                                   | Les 15 types d'événement sont écrits par les cas d'utilisation et vérifiés par leurs tests ; `ForeignAccessJournalServiceTests`, `SecurityJournalCleanupBackgroundServiceTests`       |
| Aucun moyen de devenir administrateur depuis l'application                                                                                           | Fait                                   | Sections 10 et 19 des tests SQL : l'application ne peut ni écrire le rôle, ni lancer `fn_changer_role`                                                                                |
| Comptes et données de test absents de la production                                                                                                  | Au déploiement                         | La base de production se crée neuve avec le script du MPD, qui n'ajoute que les types d'alerte                                                                                        |

Bilan : 11 points faits, 8 à confirmer au déploiement, 3 à faire au déploiement.
