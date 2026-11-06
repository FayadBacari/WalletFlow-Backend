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
- Un code vaut pour la période de 30 secondes en cours, la précédente et la suivante, et ne sert qu'une seule fois.
- Après le bon mot de passe, la connexion attend le code 5 minutes au plus, sans ouvrir de session. 5 mauvais codes obligent à recommencer la connexion.
- Le secret est chiffré en AES-256-GCM. Sa clé est rangée avec les autres secrets (§7).

### Liens envoyés par e-mail (jetons)
Tous les liens (confirmation, récupération, déblocage, changement d'e-mail) suivent les mêmes règles :
- jeton aléatoire d'au moins 128 bits, généré par un générateur cryptographique ;
- seule son empreinte (SHA-256) est stockée en base, jamais le jeton lui-même ;
- usage unique, et invalidé dès qu'un nouveau jeton du même type est demandé ;
- le lien ouvre une page avec un bouton de confirmation : l'action n'a lieu qu'au clic. Sinon, les antivirus de messagerie qui ouvrent les liens automatiquement « consomment » le jeton à la place de l'utilisateur ;
- la page de confirmation de l'inscription demande aussi le mot de passe : ouvrir le lien sans le connaître n'active rien ;
- la page de confirmation d'un changement d'adresse demande aussi le mot de passe : une faute de frappe ne donne pas le compte à un inconnu ;
- confirmer une nouvelle adresse annule les liens de récupération et de déblocage encore valables : ils sont partis vers l'ancienne.

| Lien                                  | Durée de validité             |
|---------------------------------------|-------------------------------|
| Confirmation de l'inscription         | 24 heures                     |
| Récupération du mot de passe          | 30 minutes (règle de gestion) |
| Déblocage du compte                   | 1 heure                       |
| Confirmation d'un changement d'e-mail | 24 heures                     |

### Actions sensibles
Ces actions redemandent le mot de passe, même si l'utilisateur est déjà connecté :
- changer de mot de passe ou d'adresse e-mail (règle de gestion) ;
- supprimer son compte (règle de gestion) ;
- activer ou désactiver la double authentification (règle de gestion).

Si la double authentification est activée, changer de mot de passe ou d'adresse e-mail demande aussi un code.

Les mauvais mots de passe saisis ici ne comptent pas pour le blocage du compte : un voleur de session pourrait sinon le bloquer.

À la désactivation de la double authentification, un mauvais mot de passe et un mauvais code reçoivent le même message.


## 3. Sessions

- Les sessions sont gérées côté serveur. L'identifiant de session est stocké dans un cookie, jamais dans le `localStorage` du navigateur, où un script injecté pourrait le lire.
- Le cookie porte les attributs `HttpOnly`, `Secure` et `SameSite=Lax`, avec le préfixe `__Host-`.
- Un nouvel identifiant de session est généré à chaque connexion, pour empêcher qu'un attaquant n'impose le sien à la victime.
- Les identifiants de session et les jetons d'appareil ne sont stockés en base que sous forme d'empreinte (SHA-256).
- La session expire après 30 minutes d'inactivité (règle de gestion).
- La déconnexion détruit la session côté serveur, pas seulement le cookie.
- Après un changement **ou une récupération** de mot de passe, toutes les autres sessions sont fermées.
- Après un changement de mot de passe, la session en cours reçoit aussi un nouvel identifiant : une copie volée de son cookie ne sert plus.
- « Nouvel appareil » : un jeton d'appareil aléatoire et durable est enregistré à la première connexion, dans un cookie sur le web et dans le stockage sécurisé du téléphone dans l'application mobile. Une connexion sans ce jeton déclenche l'alerte de sécurité.


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
| Message au support                    | 1 à 5 000 caractères                                                                                         |

### Injections
- **SQL** : uniquement des requêtes paramétrées ou un ORM, jamais de requête construite en collant des morceaux de texte. Pour la recherche par libellé, les caractères spéciaux `%` et `_` saisis par l'utilisateur sont neutralisés.
- **Scripts (XSS)** : les libellés et les noms de comptes reçus de la banque, les noms de sacs et de catégories, et les messages au support sont du contenu non fiable.
  - Ils sont toujours affichés comme du texte, grâce à l'échappement automatique du framework.
  - On n'injecte jamais de HTML brut (`innerHTML`, `v-html`, `dangerouslySetInnerHTML`…).
  - Les infobulles des graphiques sont configurées en texte et non en HTML : beaucoup de bibliothèques de graphiques acceptent le HTML par défaut.

### Intégrité des montants
- Les règles sur les sacs (jamais négatif, total ≤ solde total) sont appliquées **par le serveur**, dans une transaction de base de données qui verrouille les lignes concernées. Deux déplacements lancés en même temps ne doivent pas pouvoir contourner la règle.
- Accepter une suggestion ne crée qu'un seul mouvement, même en cas de double clic.

### Export de données
- Si l'export est en CSV, les cellules qui commencent par `=`, `+`, `-` ou `@` sont précédées d'une apostrophe. Sinon, un libellé piégé peut exécuter une formule à l'ouverture dans un tableur.
- L'export se télécharge directement depuis l'application. Il n'est jamais envoyé par e-mail.


## 6. Protection des requêtes

### Limitation de débit
Les valeurs ci-dessous sont des points de départ, à ajuster selon l'usage réel.

| Point d'entrée                   | Limite                                                                                   |
|----------------------------------|------------------------------------------------------------------------------------------|
| Connexion                        | 10 essais par minute et par adresse IP, en plus des 5 tentatives par compte              |
| Inscription                      | 5 par heure et par adresse IP, 3 par heure et par adresse e-mail                         |
| Confirmation de l'inscription    | 10 essais par heure et par adresse IP                                                    |
| Mot de passe oublié              | 3 par heure et par adresse e-mail, 10 par heure et par adresse IP                        |
| Lien de récupération             | 10 essais par heure et par adresse IP                                                    |
| Lien de déblocage                | 10 essais par heure et par adresse IP                                                    |
| Code de double authentification  | 5 essais, puis il faut recommencer la connexion ; 10 essais par minute et par adresse IP |
| Réglage de la double auth.       | 10 par heure et par adresse IP                                                           |
| Changement de mot de passe       | 10 par heure et par adresse IP                                                           |
| Changement d'adresse e-mail      | 3 par heure et par nouvelle adresse, 10 par heure et par adresse IP                      |
| Lien de changement d'adresse     | 10 essais par heure et par adresse IP                                                    |
| Formulaire de support (visiteur) | 3 par heure et par adresse IP                                                            |
| Mise à jour des comptes          | 1 par minute et par utilisateur                                                          |

Au-delà de la limite, le serveur répond « trop de requêtes » (429).

### Actions forcées (CSRF)
- Les cookies en `SameSite=Lax`, plus un jeton anti-CSRF sur toutes les requêtes qui modifient des données. Les frameworks le fournissent.
- Les routes appelées sans session (inscription, confirmation, connexion) n'ont pas besoin de ce jeton : elles n'agissent au nom de personne, et n'acceptent que du JSON, qu'un site piégé ne peut pas envoyer sans autorisation.
- Aucune requête `GET` ne modifie de données.

### Origines (CORS)
- Si l'interface et le serveur sont sur le même domaine, aucune règle CORS n'est nécessaire.
- Sinon, seule l'origine exacte de l'interface est autorisée, jamais `*`.

### En-têtes de sécurité

| En-tête                     | Valeur                                                                                                                  |
|-----------------------------|-------------------------------------------------------------------------------------------------------------------------|
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains`                                                                                   |
| `Content-Security-Policy`   | `default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'` |
| `X-Content-Type-Options`    | `nosniff`                                                                                                               |
| `Referrer-Policy`           | `strict-origin-when-cross-origin`                                                                                       |
| `Permissions-Policy`        | `camera=(), microphone=(), geolocation=()`                                                                              |

Si une bibliothèque (de graphiques, par exemple) a besoin de styles en ligne, on peut ajouter `style-src 'self' 'unsafe-inline'`. C'est acceptable pour les styles, mais jamais pour les scripts.

### Scripts tiers
Aucun script tiers (publicité, traceur, outil d'analyse externe) sur les pages où l'utilisateur est connecté : chacun d'eux pourrait lire ses données financières. Les bibliothèques sont hébergées avec l'application, pas chargées depuis un CDN.


## 7. Protection des données

### Minimisation
- WalletFlow n'enregistre **ni IBAN ni numéro de compte**, même si le prestataire les fournit : un compte n'a besoin que de son nom et de son solde. Une donnée qu'on ne stocke pas ne peut pas fuiter.
- WalletFlow ne voit jamais les identifiants ni le mot de passe de la banque : l'utilisateur s'identifie sur la page de sa banque (règle de gestion).

### Connexion bancaire
- Le prestataire est agréé pour lire les comptes (DSP2), hébergé dans l'Union européenne, et lié à WalletFlow par un contrat de sous-traitance (article 28 du RGPD).
- Seul l'accès en lecture est demandé : WalletFlow ne peut lancer ni virement ni paiement (règle de gestion).
- La connexion se lance toujours depuis WalletFlow. Au retour de la banque, un paramètre `state` aléatoire, lié à la session, est vérifié, et l'adresse de retour est fixe.
- La clé d'API du prestataire reste sur le serveur, avec les autres secrets. Elle n'est jamais intégrée à l'application mobile.
- Si le prestataire fournit un jeton d'accès pour chaque utilisateur, ce jeton est chiffré en base, comme le secret de double authentification.
- Les notifications du prestataire (webhooks) ne sont acceptées qu'en HTTPS, avec une signature valide.
- Les données reçues sont validées comme des données saisies (§5).
- Déconnecter une banque supprime ses comptes et leurs transactions, et révoque l'autorisation chez le prestataire (règle de gestion).

### Chiffrement
- **En transit** : HTTPS partout (TLS 1.2 minimum), avec redirection automatique de HTTP vers HTTPS.
- **Au repos** : base de données et sauvegardes sur des disques chiffrés par l'hébergeur. Les sauvegardes sont en plus chiffrées avant d'être envoyées.
- **Champ par champ** : seuls les secrets de double authentification, et le jeton d'accès du prestataire s'il y en a un, sont chiffrés individuellement. Chiffrer chaque montant ou chaque libellé compliquerait beaucoup l'application pour un gain faible.

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

### Suppression de compte
- Pendant les 30 jours de délai (règle de gestion), le compte est inaccessible, sauf pour annuler la suppression.
- Dès la demande, l'autorisation donnée à la banque est révoquée chez le prestataire (règle de gestion).
- Passé ce délai, sont effacés : l'utilisateur, ses connexions bancaires, ses comptes bancaires et leurs transactions, ses catégories et ses règles de classement, ses sacs et leurs mouvements, ses suggestions, ses préférences d'alerte, ses demandes de support et leurs réponses, ses appareils, ses sessions, ses jetons et ses codes de secours. Dans le journal de sécurité, ses événements restent, mais sans son identifiant ni son adresse IP.
- Les données disparaissent des sauvegardes au plus tard 30 jours plus tard, quand la dernière sauvegarde qui les contient expire. C'est à indiquer dans la politique de confidentialité.

### Journaux
Les journaux techniques ne contiennent jamais de mot de passe, de jeton, de code de double authentification, d'identifiant de session, de montant ni de libellé. Ils désignent l'utilisateur par son identifiant, pas par son adresse e-mail.


## 8. Administration et support

- L'administrateur **ne voit ni les comptes, ni les transactions, ni les soldes, ni les sacs** des utilisateurs. Il voit uniquement :
  - l'adresse e-mail ;
  - l'état du compte (non confirmé, actif, bloqué, en cours de suppression) ;
  - les demandes de support ;
  - des statistiques globales.
- L'administrateur peut débloquer un compte. Il ne peut ni changer le mot de passe ou l'e-mail d'un utilisateur, ni se connecter à sa place.
- Un compte administrateur ne se crée pas depuis l'application. Seul le responsable du projet attribue ou retire le rôle, avec une commande exécutée sur le serveur, à un compte dont l'adresse e-mail est confirmée.
- Chaque action d'un administrateur est enregistrée dans le journal (§11).
- Les messages au support, surtout ceux des visiteurs, sont du contenu non fiable. Ils sont affichés comme du texte dans l'interface d'administration (§5) : un script caché dans un message viserait directement l'administrateur.


## 9. E-mails et notifications

- Le domaine d'envoi est configuré avec **SPF, DKIM et DMARC**. Il devient alors beaucoup plus difficile d'envoyer de faux e-mails au nom de WalletFlow.
- Les e-mails ne contiennent jamais de mot de passe, ni de solde, ni de liste de transactions.
- Les liens pointent uniquement vers le domaine de WalletFlow, en HTTPS.
- L'e-mail qui rappelle de renouveler l'autorisation bancaire ne contient aucun lien vers une banque : il invite seulement à ouvrir WalletFlow. Un e-mail qui propose de reconnecter sa banque en un clic est exactement ce qu'imiterait un hameçonnage.
- Chaque e-mail de sécurité (connexion depuis un nouvel appareil, changement de mot de passe ou d'e-mail, blocage) indique quoi faire si ce n'est pas l'utilisateur qui a agi.
- L'e-mail qui prévient l'ancienne adresse ne cite pas la nouvelle, et invite à écrire au support si ce n'est pas l'utilisateur qui a agi.
- Le contenu des notifications sur téléphone reste général (par exemple « Ton budget Loisirs est presque atteint ») : il peut s'afficher sur un écran verrouillé.


## 10. Développement et dépendances

- Le fichier de verrouillage des dépendances (`package-lock.json`, `composer.lock`…) est versionné.
- Les dépendances sont analysées automatiquement (Dependabot, Renovate ou l'audit du gestionnaire de paquets). Une faille critique est corrigée dans la semaine.
- Un outil de détection de secrets (par exemple gitleaks) tourne avant chaque commit ou dans l'intégration continue.
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
- actions des administrateurs, et attribution ou retrait du rôle d'administrateur ;
- tentatives d'accès à une ressource d'un autre utilisateur.

Pour chaque événement, le journal note la date, l'identifiant de l'utilisateur, l'adresse IP, l'événement et son résultat.

**Conservation** : de 6 mois à 1 an, comme le recommande la CNIL. Quand un compte est supprimé, ses événements restent, mais sans son identifiant ni son adresse IP (§7).


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

- [ ] HTTPS et HSTS actifs, HTTP redirigé vers HTTPS
- [ ] En-têtes de sécurité vérifiés (par exemple avec Mozilla Observatory)
- [ ] Mots de passe et codes de secours hachés avec Argon2id (ou bcrypt)
- [ ] Limitation de débit active sur la connexion, l'inscription et sa confirmation, la récupération du mot de passe et le support
- [ ] Test d'accès croisé (utilisateur B vers les données de A) sur chaque route
- [ ] Aucune requête SQL construite par concaténation de texte
- [ ] Mode débogage désactivé, messages d'erreur génériques
- [ ] Aucun secret dans le code, `.env` ignoré par Git, détection de secrets passée
- [ ] Clé d'API du prestataire bancaire sur le serveur seulement, absente de l'application mobile
- [ ] Paramètre `state` vérifié au retour de la banque, signature des webhooks vérifiée
- [ ] Révocation chez le prestataire testée (déconnexion d'une banque, suppression d'un compte)
- [ ] Aucune dépendance avec une faille critique connue
- [ ] Base de données inaccessible depuis internet, utilisateur de base aux droits limités
- [ ] Sauvegardes chiffrées et restauration testée
- [ ] SPF, DKIM et DMARC configurés
- [ ] Journalisation des événements de sécurité active
- [ ] Aucun moyen de devenir administrateur depuis l'application
- [ ] Comptes et données de test absents de la production
