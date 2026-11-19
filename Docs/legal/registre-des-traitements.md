# Registre des traitements de WalletFlow

> **Brouillon** du registre des activités de traitement (article 30 du RGPD), rédigé à partir des documents du projet. Document interne, à tenir à jour et à présenter à la CNIL sur demande. Ce n'est pas un conseil juridique : à relire, idéalement par un juriste. Les passages **[À COMPLÉTER]** attendent un choix fait au déploiement (hébergeur, prestataire d'e-mails, date de mise en ligne), ou une action à mener avant l'ouverture au public (contrats).

Dernière mise à jour : [À COMPLÉTER à la mise en ligne : date]


## Responsable du traitement

| Information                           | Valeur                                                                                                                                                                                                                                                                                                |
|---------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Responsable                           | Fayad Idriss BACARI, particulier, éditeur à titre non professionnel                                                                                                                                                                                                                                   |
| Contact pour les données personnelles | fayadbacari-dev@outlook.com                                                                                                                                                                                                                                                                           |
| Délégué à la protection des données   | Non désigné : il n'est obligatoire (article 37) que pour un suivi régulier et systématique à grande échelle, ou des données sensibles (article 9) à grande échelle. WalletFlow ne traite aucune donnée sensible, et son volume reste modeste : à revoir si le nombre d'utilisateurs devient important |

Sous-traitants communs à tous les traitements : l'hébergeur [À COMPLÉTER au choix de l'hébergeur], dans l'Union européenne, et le prestataire d'e-mails [À COMPLÉTER au choix du prestataire], dans l'Union européenne. Chacun est lié par un contrat de sous-traitance (article 28 du RGPD) [À COMPLÉTER au choix des prestataires : contrats signés]. Les mesures de sécurité sont celles de la stratégie de sécurité du projet (`Docs/security-strategy.md`).


## 1. Comptes et authentification

| Rubrique                              | Contenu                                                                                                                                                                                                       |
|---------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Finalité                              | Créer et gérer les comptes, confirmer l'adresse, connecter l'utilisateur, gérer la double authentification, récupérer et débloquer un compte                                                                  |
| Base légale                           | Exécution du contrat                                                                                                                                                                                          |
| Personnes concernées                  | Utilisateurs inscrits, et personnes dont l'inscription n'est pas confirmée                                                                                                                                    |
| Données                               | Adresse e-mail, empreinte du mot de passe, nom et prénom facultatifs, rôle, état du compte, secret de double authentification chiffré, empreintes des codes de secours, liens envoyés par e-mail (empreintes) |
| Destinataires                         | L'utilisateur ; l'administrateur pour l'adresse et l'état du compte                                                                                                                                           |
| Transferts hors de l'Union européenne | Aucun                                                                                                                                                                                                         |
| Durée                                 | Tant que le compte existe, puis 30 jours après une demande de suppression ; compte non confirmé effacé au bout de 24 heures ; liens valables de 30 minutes à 24 heures                                        |


## 2. Connexion bancaire et suivi du budget

| Rubrique                              | Contenu                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
|---------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Finalité                              | Lire les comptes de l'utilisateur, à sa demande et en lecture seule ; afficher soldes, opérations et graphiques ; classer les opérations ; suivre les budgets                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Base légale                           | Exécution du contrat                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Personnes concernées                  | Utilisateurs qui connectent une banque                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Données                               | Nom de la banque, état et fin de l'autorisation ; comptes en euros (nom, solde, date du solde) ; opérations comptabilisées (date, libellé, montant, catégorie, marque de transfert) ; catégories, budgets et règles de classement. Jamais l'IBAN, le numéro de compte ni les identifiants bancaires                                                                                                                                                                                                                                                                                                                                                                                                       |
| Destinataires                         | L'utilisateur. Jamais l'administrateur                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Prestataire particulier               | Enable Banking Oy (Finlande, numéro d'entreprise 2988499-7), prestataire de services d'information sur les comptes enregistré auprès de l'autorité finlandaise de surveillance financière (FIN-FSA). Responsable de ses propres traitements, et non sous-traitant : l'utilisateur accepte directement ses conditions, et Enable Banking garde son consentement sans enregistrer les données des comptes. Reçoit aussi l'adresse IP et le navigateur de l'utilisateur présent (DSP2). Ses conditions gratuites ne couvrent que l'usage personnel du titulaire du compte développeur : l'ouverture au public demande un contrat écrit [À COMPLÉTER avant l'ouverture : contrat signé, qui confirme ce rôle] |
| Transferts hors de l'Union européenne | Aucun                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Durée                                 | Tant que la banque est connectée et que le compte existe ; la déconnexion d'une banque supprime ses comptes et opérations                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |


## 3. Sacs et suggestions d'épargne

| Rubrique             | Contenu                                                                                                                   |
|----------------------|---------------------------------------------------------------------------------------------------------------------------|
| Finalité             | Répartir l'argent dans des sacs virtuels, suivre les objectifs, proposer des suggestions d'épargne tirées de règles fixes |
| Base légale          | Exécution du contrat                                                                                                      |
| Personnes concernées | Utilisateurs inscrits                                                                                                     |
| Données              | Sacs, objectifs, mouvements ; suggestions (règle, mois, catégorie, économie mensuelle, réponse)                           |
| Destinataires        | L'utilisateur. Jamais l'administrateur                                                                                    |
| Décision automatisée | Aucune au sens de l'article 22 du RGPD : les suggestions n'ont aucun effet juridique, et l'utilisateur peut les ignorer   |
| Durée                | Tant que le compte existe                                                                                                 |


## 4. Alertes et e-mails

| Rubrique             | Contenu                                                                                                                                        |
|----------------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| Finalité             | Prévenir l'utilisateur (budget, objectif d'un sac, nouvelle suggestion, autorisation bancaire, sécurité) ; envoyer les e-mails du service      |
| Base légale          | Exécution du contrat ; intérêt légitime pour les alertes de sécurité, qui ne peuvent pas être désactivées                                      |
| Personnes concernées | Utilisateurs inscrits ; personnes qui demandent une récupération ou écrivent au support                                                        |
| Données              | Préférences d'alerte, alertes envoyées, e-mails en attente (adresse, et adresse IP pour une récupération). Les alertes ne citent aucun montant |
| Destinataires        | L'utilisateur ; le prestataire d'e-mails                                                                                                       |
| Durée                | Alertes de budget et de suggestion : 2 mois après leur mois ; e-mails en attente : jusqu'à leur envoi ou au dernier des 5 essais               |


## 5. Support

| Rubrique             | Contenu                                                          |
|----------------------|------------------------------------------------------------------|
| Finalité             | Répondre aux demandes des utilisateurs et des visiteurs          |
| Base légale          | Exécution du contrat ; intérêt légitime pour un visiteur         |
| Personnes concernées | Utilisateurs et visiteurs qui écrivent au support                |
| Données              | Message, date, état, réponses ; adresse de contact d'un visiteur |
| Destinataires        | L'administrateur ; le prestataire d'e-mails pour les réponses    |
| Durée                | 1 an après l'envoi de la demande, avec ses réponses              |


## 6. Sécurité du service

| Rubrique                              | Contenu                                                                                                                                                                                                     |
|---------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Finalité                              | Protéger les comptes et le service : journal de sécurité, blocage après des échecs, limites de requêtes, appareils reconnus, alertes de sécurité, refus des mots de passe divulgués                         |
| Base légale                           | Intérêt légitime ; obligation de sécurité (article 32 du RGPD)                                                                                                                                              |
| Personnes concernées                  | Utilisateurs, visiteurs, et toute personne qui tente de se connecter                                                                                                                                        |
| Données                               | Journal de sécurité (date, événement, résultat, adresse IP, compte visé par une action de l'administrateur) ; sessions et appareils (empreintes des jetons, dates) ; compteurs de tentatives et de blocages |
| Destinataires                         | L'administrateur, pour le journal ; le service Pwned Passwords ne reçoit que 5 caractères d'empreinte SHA-1, qui ne désignent personne                                                                      |
| Transferts hors de l'Union européenne | Aucune donnée personnelle : Pwned Passwords, exploité depuis l'Australie, ne reçoit que 5 caractères d'empreinte, envoyés par le serveur                                                                    |
| Durée                                 | Journal : 1 an, et anonymisé (identifiant et adresse IP retirés) à l'effacement du compte ; sessions : 30 minutes d'inactivité, 12 heures au plus ; appareils : un an sans connexion                        |


## 7. Administration

| Rubrique             | Contenu                                                                                                                                     |
|----------------------|---------------------------------------------------------------------------------------------------------------------------------------------|
| Finalité             | Débloquer un compte, envoyer le lien qui désactive la double authentification, répondre au support, consulter des statistiques anonymes     |
| Base légale          | Exécution du contrat ; intérêt légitime                                                                                                     |
| Personnes concernées | Utilisateurs                                                                                                                                |
| Données              | Adresse et état d'un compte trouvé par son adresse exacte ; statistiques sans aucun montant. Jamais les comptes, opérations, soldes ni sacs |
| Destinataires        | L'administrateur, dont chaque action et chaque recherche est notée au journal de sécurité                                                   |
| Durée                | Celle des données consultées ; les actions restent 1 an au journal                                                                          |


## Analyse d'impact (AIPD)

Obligatoire avant l'ouverture au public. Les lignes directrices européennes (WP248) la demandent, dans la plupart des cas, dès que deux de leurs critères se cumulent, et WalletFlow en réunit deux :
- des données à caractère hautement personnel : l'historique des opérations bancaires révèle la vie privée (achats, lieux, habitudes) ;
- une évaluation de la situation économique : les suggestions comparent les dépenses à leur moyenne et l'épargne aux revenus. C'est un profilage au sens de l'article 4 du RGPD, même sans décision automatisée au sens de l'article 22.

La liste de la CNIL des traitements soumis à une analyse d'impact ne vise pas ce cas directement : son entrée la plus proche, le profilage qui fait appel à des données provenant de sources externes, a pour exemples les courtiers en données et la publicité personnalisée. Elle est faite, appuyée sur la stratégie de sécurité, et validée le 18 novembre 2026 : `analyse-impact.md`.


## Violations de données

En cas de violation de données personnelles, la procédure de la stratégie de sécurité (§12) s'applique :
1. contenir ;
2. évaluer ;
3. notifier la CNIL dans les 72 heures (article 33 du RGPD), et les personnes concernées si le risque pour elles est élevé (article 34) ;
4. corriger et documenter.

Chaque violation est inscrite dans un registre des violations (article 33.5), même sans notification. Ce registre est tenu dans un document privé, hors du dépôt Git : une violation peut décrire une faille et les personnes touchées, que l'historique du dépôt garderait pour toujours. Pour chaque violation, il note :

| Rubrique                  | Contenu                                                                                                     |
|---------------------------|-------------------------------------------------------------------------------------------------------------|
| Faits                     | Date et heure de la découverte, nature de la violation (confidentialité, intégrité ou disponibilité), cause |
| Données et personnes      | Catégories de données et de personnes touchées, nombre approximatif                                         |
| Conséquences              | Conséquences probables pour les personnes                                                                   |
| Mesures                   | Ce qui a été fait pour contenir, puis corriger                                                              |
| Notification à la CNIL    | Date de la notification, ou raison de ne pas notifier                                                       |
| Information des personnes | Date de leur information, ou raison de ne pas les informer                                                  |
