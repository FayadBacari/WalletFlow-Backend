# Politique de confidentialité de WalletFlow

> **Brouillon** rédigé à partir des documents du projet (règles de gestion, stratégie de sécurité, dictionnaire de données) et du code. Ce n'est pas un conseil juridique : à relire, idéalement par un juriste. Les passages **[À COMPLÉTER]** attendent un choix fait au déploiement : hébergeur, prestataire d'e-mails, stockage des sauvegardes, date de mise en ligne.

Dernière mise à jour : [À COMPLÉTER à la mise en ligne : date]

WalletFlow t'aide à suivre ton argent : tes comptes bancaires, tes dépenses par catégorie, tes sacs d'épargne. Pour cela, il traite des données personnelles, dont des données financières. Cette page dit lesquelles, pourquoi, avec qui, combien de temps, et comment exercer tes droits.


## 1. Qui est responsable de tes données

Le responsable du traitement est l'éditeur de WalletFlow, un particulier : Fayad Idriss BACARI. Pour toute question sur tes données : fayadbacari-dev@outlook.com, ou la page de support.


## 2. Les données que WalletFlow traite

| Catégorie               | Données                                                                                                                                                                                                                                                          | D'où elles viennent               |
|-------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------------------------------|
| Compte                  | Adresse e-mail ; empreinte du mot de passe (Argon2id), jamais le mot de passe lui-même ; nom et prénom, facultatifs ; état du compte (adresse confirmée, blocages, demande de suppression) ; canal choisi pour les alertes                                       | Toi                               |
| Double authentification | Secret de ton application d'authentification, chiffré ; empreintes des codes de secours ; repère du dernier code utilisé, pour qu'un code ne serve qu'une fois                                                                                                   | Toi                               |
| Connexions et sécurité  | Sessions (empreinte du jeton, dates d'ouverture et de dernière activité) ; appareils reconnus (empreinte du jeton, date de dernière connexion) ; liens envoyés par e-mail (empreinte, date de fin) ; journal de sécurité (date, événement, résultat, adresse IP) | Toi et ton navigateur             |
| Banque                  | Nom de la banque, état et date de fin de l'autorisation ; pour chaque compte en euros : nom, solde et date du solde ; pour chaque opération comptabilisée : date, libellé, montant                                                                               | Ta banque, par Enable Banking     |
| Budget et épargne       | Catégories, budgets mensuels, règles de classement, catégorie de chaque opération, marque de transfert ; sacs, objectifs et mouvements ; suggestions et ta réponse                                                                                               | Toi, et les calculs de WalletFlow |
| Alertes et e-mails      | Préférences d'alerte ; alertes envoyées ; e-mails en attente d'envoi (adresse, et adresse IP pour une demande de récupération)                                                                                                                                   | WalletFlow                        |
| Support                 | Message, date, état, réponses ; adresse de contact si tu écris sans être connecté                                                                                                                                                                                | Toi                               |

**Ce que WalletFlow ne collecte jamais** :
- ni l'IBAN ni le numéro de tes comptes, même si le prestataire les transmet ;
- ni tes identifiants ni ton mot de passe bancaires : tu t'identifies sur la page de ta banque ;
- ni aucun mot de passe, jeton ou code de secours en clair : seules leurs empreintes sont gardées ;
- ni aucune donnée de publicité ou de mesure d'audience : aucun traceur, aucun outil d'analyse externe.


## 3. Pourquoi, et sur quelle base

| Finalité                                                                                                                                                       | Base légale (article 6 du RGPD)                                                                       |
|----------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------|
| Créer ton compte, confirmer ton adresse, te connecter, gérer la double authentification                                                                        | Exécution du contrat (conditions générales d'utilisation)                                             |
| Connecter tes banques, afficher tes comptes, soldes, opérations et graphiques                                                                                  | Exécution du contrat, à ta demande : c'est toi qui autorises chaque banque                            |
| Classer tes opérations, gérer tes sacs, te proposer des suggestions et t'envoyer des alertes                                                                   | Exécution du contrat                                                                                  |
| Protéger ton compte et le service : journal de sécurité, blocage après des échecs, limites de requêtes, alertes de sécurité, refus des mots de passe divulgués | Intérêt légitime de protéger ton compte et le service, et obligation de sécurité (article 32 du RGPD) |
| Répondre à tes demandes de support                                                                                                                             | Exécution du contrat ; intérêt légitime pour un visiteur non connecté                                 |
| Te permettre de télécharger tes données et d'effacer ton compte                                                                                                | Obligation légale (articles 17 et 20 du RGPD)                                                         |


## 4. Qui reçoit tes données

WalletFlow ne vend tes données à personne et n'affiche aucune publicité.

| Destinataire                                                                                                                                | Ce qu'il reçoit                                                                                                                                                                                                                  | Pourquoi                                                                                                                                                                              |
|---------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Hébergeur : [À COMPLÉTER au choix de l'hébergeur], dans l'Union européenne                                                                  | Toutes les données, sur ses serveurs                                                                                                                                                                                             | Faire tourner le service (sous-traitant)                                                                                                                                              |
| Prestataire d'e-mails : [À COMPLÉTER au choix du prestataire], dans l'Union européenne                                                      | Ton adresse et le texte des e-mails                                                                                                                                                                                              | Envoyer les e-mails de WalletFlow (sous-traitant)                                                                                                                                     |
| Enable Banking Oy (Finlande), prestataire de services d'information sur les comptes, enregistré auprès de l'autorité financière finlandaise | Les demandes de lecture de tes comptes ; quand tu es connecté, ton adresse IP et ton navigateur, que la DSP2 exige pour une lecture faite en ta présence. Il garde ton consentement, sans enregistrer les données de tes comptes | Lire tes comptes, en lecture seule, avec ton autorisation. Il est responsable de ses propres traitements : tu acceptes ses conditions quand tu connectes une banque                   |
| Ta banque                                                                                                                                   | Ce que tu lui donnes toi-même sur sa page                                                                                                                                                                                        | T'identifier et autoriser la lecture de tes comptes                                                                                                                                   |
| Service Pwned Passwords, exploité depuis l'Australie                                                                                        | Les 5 premiers caractères de l'empreinte SHA-1 d'un nouveau mot de passe, rien d'autre                                                                                                                                           | Refuser un mot de passe déjà divulgué : ni le mot de passe, ni son empreinte complète, ni ton adresse ne quittent WalletFlow, et la demande part du serveur, jamais de ton navigateur |
| L'administrateur de WalletFlow                                                                                                              | Ton adresse, l'état de ton compte et tes demandes de support                                                                                                                                                                     | Te répondre et débloquer ton compte. Il ne voit jamais tes comptes, opérations, soldes ni sacs                                                                                        |

Tes données restent dans l'Union européenne [À COMPLÉTER au choix de l'hébergeur et du prestataire d'e-mails : le confirmer]. Seuls partent hors de l'Union les 5 caractères d'empreinte envoyés à Pwned Passwords, qui ne désignent personne.


## 5. Combien de temps

| Donnée                                    | Durée de conservation                                                                                                                                                                                                 |
|-------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Ton compte et tout ce qu'il contient      | Tant que le compte existe. Après une demande de suppression, effacement définitif au bout de 30 jours ; te reconnecter avant annule la suppression                                                                    |
| Sauvegardes                               | Une donnée effacée disparaît des sauvegardes au plus tard 30 jours après : chaque sauvegarde est chiffrée, puis effacée au bout de 30 jours [À COMPLÉTER au déploiement : même durée sur le stockage hors du serveur] |
| Compte dont l'adresse n'est pas confirmée | Effacé une fois son lien expiré, au bout de 24 heures                                                                                                                                                                 |
| Sessions                                  | Fermées après 30 minutes d'inactivité, et au plus tard 12 heures après leur ouverture                                                                                                                                 |
| Appareils reconnus                        | Oubliés après un an sans connexion, sauf le plus récent                                                                                                                                                               |
| Liens envoyés par e-mail                  | Confirmation : 24 h ; récupération du mot de passe : 30 min ; déblocage : 1 h ; changement d'adresse : 24 h ; désactivation de la double authentification : 24 h                                                      |
| Journal de sécurité                       | 1 an. Quand ton compte est effacé, tes événements restent, mais sans ton identifiant ni ton adresse IP                                                                                                                |
| Comptes et opérations d'une banque        | Tant que la banque est connectée. La déconnecter les supprime                                                                                                                                                         |
| Alertes de budget et de suggestion        | 2 mois après leur mois ; les autres alertes partent avec ce qu'elles visent                                                                                                                                           |
| E-mails en attente                        | Effacés dès leur envoi, ou après le dernier des 5 essais                                                                                                                                                              |
| Demandes de support                       | 1 an après leur envoi, avec leurs réponses                                                                                                                                                                            |


## 6. Sécurité

Les mesures sont détaillées dans la stratégie de sécurité du projet. En résumé :
- connexion toujours chiffrée (HTTPS) ; e-mails envoyés par une connexion chiffrée ;
- mots de passe et codes de secours hachés avec Argon2id ; mots de passe déjà divulgués refusés ;
- double authentification proposée, avec un secret chiffré (AES-256-GCM) ;
- blocage temporaire après 5 échecs de connexion, et alerte à chaque connexion depuis un nouvel appareil ;
- sessions gardées sur le serveur, fermées automatiquement ;
- accès en lecture seule à tes banques : WalletFlow ne peut faire ni virement ni paiement ;
- base de données inaccessible depuis internet, avec un compte aux droits limités ;
- en cas de violation de tes données, la CNIL est prévenue dans les 72 heures, et toi aussi si le risque pour toi est élevé.


## 7. Cookies

WalletFlow n'utilise que des cookies strictement nécessaires au service. Ils ne demandent donc pas ton accord (article 82 de la loi Informatique et Libertés). Aucun cookie de publicité ni de mesure d'audience.

| Cookie                         | Rôle                                                                                 | Durée                                                                                                       |
|--------------------------------|--------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------|
| `__Host-walletflow-session`    | Garder ta session ouverte                                                            | Jusqu'à la fermeture du navigateur ; le serveur ferme la session après 30 minutes d'inactivité ou 12 heures |
| `__Host-walletflow-device`     | Reconnaître ton navigateur, pour t'alerter d'une connexion depuis un nouvel appareil | 1 an                                                                                                        |
| `__Host-walletflow-two-factor` | Garder la connexion qui attend ton code de double authentification                   | Jusqu'à la fermeture du navigateur ; 5 minutes côté serveur                                                 |
| `__Host-walletflow-csrf`       | Protéger tes actions contre les requêtes forcées par un autre site                   | Jusqu'à la fermeture du navigateur                                                                          |


## 8. Les suggestions d'épargne

Les suggestions viennent de règles fixes, appliquées à tes dépenses : une catégorie qui dépasse sa moyenne, de l'argent disponible en fin de mois, une épargne du mois sous 20 % des revenus. Chacune dit pourquoi elle apparaît. Elles n'ont aucun effet juridique, ne servent à aucun tiers, et ne recommandent aucun produit financier : tu peux les ignorer.


## 9. Tes droits

Tu as le droit d'accéder à tes données, de les corriger, de les effacer, de les recevoir dans un format réutilisable, de limiter leur traitement et de t'opposer à un traitement fondé sur l'intérêt légitime.

| Droit                                        | Comment                                                                                                                                                     |
|----------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Accès et portabilité                         | Dans l'application : téléchargement de toutes tes données dans un fichier JSON, avec ton mot de passe, et un code si la double authentification est activée |
| Rectification                                | Dans l'application : nom, prénom, adresse e-mail et mot de passe                                                                                            |
| Effacement                                   | Dans l'application : suppression du compte, effective 30 jours après la demande                                                                             |
| Limitation, opposition, toute autre question | L'adresse e-mail du §1, ou la page de support. Réponse sous un mois                                                                                         |

Si tu estimes que tes droits ne sont pas respectés, tu peux adresser une réclamation à la CNIL (www.cnil.fr).


## 10. Âge minimum

WalletFlow est réservé aux personnes d'au moins 18 ans.


## 11. Modifications

Cette politique peut évoluer avec le service. Une modification importante t'est annoncée sur le site et dans l'application 30 jours avant de s'appliquer.
