# Dictionnaire de données de WalletFlow

Ce dictionnaire recense toutes les données que WalletFlow doit stocker. Sa base est le fichier des règles de gestion (`gestion-rules.md`) : chaque donnée vient de l'une de ses règles, ou de la stratégie de sécurité (`security-strategy.md`) quand elle est indispensable pour sécuriser l'une d'elles. Il sert de base au MCD.

Les données sont regroupées par domaine. Chaque groupe correspond à une entité pressentie.


## 1. Conventions

### Types

| Code | Type                   | Exemple             |
|------|------------------------|---------------------|
| AN   | Alphanumérique (texte) | « Compte courant »  |
| N    | Numérique entier       | 12550               |
| D    | Date                   | 2026-11-02          |
| DH   | Date et heure          | 2026-11-02 14:30:00 |
| B    | Booléen (vrai / faux)  | vrai                |

### Règles de nommage et de format
- Les codes sont en minuscules, sans accent, avec des mots séparés par `_`.
- Chaque code est unique dans tout le dictionnaire : on écrit `nom_compte`, `nom_sac` et `nom_categorie`, jamais trois fois `nom`.
- Les identifiants sont des UUID de 36 caractères, pour qu'on ne puisse pas deviner ceux des autres utilisateurs (SS §4).
- Les montants sont des entiers en centimes : 125,50 € est stocké `12550` (SS §5). Leur taille est de 10 chiffres, soit 10 000 000 € au plus en valeur absolue.
- Les listes de valeurs sont stockées sous forme de codes courts, écrits entre accents graves (`email`, `telephone`…).
- Colonne Oblig. : Oui = obligatoire, Non = facultatif.
- Sous chaque tableau, la liste Contraintes précise les règles de chaque donnée : format, valeurs possibles, valeur par défaut.


### Sources

| Abréviation | Signification                                                          |
|-------------|------------------------------------------------------------------------|
| GR          | `gestion-rules.md`, suivi du nom court de la section (voir ci-dessous) |
| SS          | `security-strategy.md`, suivi du numéro de section                     |


| Nom court      | Section de `gestion-rules.md`    |
|----------------|----------------------------------|
| GR Cadre       | Cadre général                    |
| GR Auth        | Interface et Authentification    |
| GR Comptes     | Comptes Bancaires                |
| GR Sacs        | Sacs et Épargne                  |
| GR Dépenses    | Dépenses et graphiques           |
| GR Perso       | Compte et données personnelles   |
| GR Admin       | Administrateur et administration |
| GR Notif       | Notifications                    |
| GR Suppression | Suppression                      |


## 2. Vue d'ensemble

| Section | Domaine                           | Entité pressentie                                          |
|---------|-----------------------------------|------------------------------------------------------------|
| 3       | Utilisateur                       | UTILISATEUR                                                |
| 4       | Sécurité du compte                | CODE_SECOURS, JETON, APPAREIL, SESSION, EVENEMENT_SECURITE |
| 5       | Comptes bancaires et transactions | CONNEXION_BANCAIRE, COMPTE_BANCAIRE, TRANSACTION           |
| 6       | Catégories et classement          | CATEGORIE, REGLE_CLASSEMENT                                |
| 7       | Sacs                              | SAC, MOUVEMENT_SAC                                         |
| 8       | Suggestions d'épargne             | SUGGESTION                                                 |
| 9       | Notifications                     | TYPE_ALERTE                                                |
| 10      | Support                           | DEMANDE_SUPPORT, REPONSE_SUPPORT                           |

Les sections 11 à 13 complètent le dictionnaire : données calculées, paramètres et données volontairement non collectées.


## 3. Utilisateur

| Code                     | Désignation                       | Type | Taille | Oblig. | Source          |
|--------------------------|-----------------------------------|------|--------|--------|-----------------|
| id_utilisateur           | Identifiant de l'utilisateur      | AN   | 36     | Oui    | SS §4           |
| email_utilisateur        | Adresse e-mail de connexion       | AN   | 254    | Oui    | GR Auth, SS §5  |
| mot_de_passe_hash        | Empreinte du mot de passe         | AN   | 255    | Oui    | GR Auth, SS §2  |
| nom_utilisateur          | Nom de famille                    | AN   | 50     | Non    | GR Perso        |
| prenom_utilisateur       | Prénom                            | AN   | 50     | Non    | GR Perso        |
| role_utilisateur         | Rôle                              | AN   | 20     | Oui    | GR Cadre, SS §8 |
| email_confirme           | Adresse e-mail confirmée          | B    | —      | Oui    | GR Auth         |
| double_auth_activee      | Double authentification activée   | B    | —      | Oui    | GR Auth, SS §2  |
| secret_totp_chiffre      | Secret de double authentification | AN   | 255    | Non    | SS §2, §7       |
| canal_notification       | Canal de réception des alertes    | AN   | 10     | Oui    | GR Notif        |
| nb_tentatives_echouees   | Tentatives de connexion échouées  | N    | 1      | Oui    | GR Auth, SS §2  |
| nb_blocages              | Nombre de blocages successifs     | N    | 1      | Oui    | GR Auth         |
| date_fin_blocage         | Fin du blocage en cours           | DH   | —      | Non    | GR Auth         |
| date_demande_suppression | Date de la demande de suppression | DH   | —      | Non    | GR Suppression  |

**Contraintes**

- `id_utilisateur` : UUID, unique.
- `email_utilisateur` : unique, format valide, enregistrée en minuscules. Format valide : 254 caractères au plus ; avant le @, 1 à 64 lettres et chiffres sans accent ou signes `.`, `_`, `+`, `-`, `'`, sans point au début, à la fin ni en double ; après le @, au moins deux parties séparées par un point, chacune de 1 à 63 lettres, chiffres ou tirets, sans tiret au début ni à la fin ; la dernière partie a au moins 2 caractères et commence par une lettre.
- `mot_de_passe_hash` : hachage Argon2id (ou bcrypt), jamais en clair. Le mot de passe saisi, normalisé en Unicode NFC, fait 12 à 128 caractères, avec une majuscule, une minuscule, un chiffre et un caractère spécial, sans caractère invisible.
- `nom_utilisateur` : 1 à 50 caractères, sans caractère invisible. Vide si l'utilisateur l'efface.
- `prenom_utilisateur` : 1 à 50 caractères, sans caractère invisible. Vide si l'utilisateur l'efface.
- `role_utilisateur` : `utilisateur` ou `administrateur`, `utilisateur` par défaut. Le rôle `administrateur` ne s'attribue que depuis le serveur, jamais depuis l'application.
- `email_confirme` : faux par défaut. Le compte n'est actif que si cette valeur est vraie. Tant qu'elle est fausse, une nouvelle inscription remplace le mot de passe, et le compte est effacé une fois son lien expiré.
- `double_auth_activee` : faux par défaut. Si vrai, l'utilisateur se connecte aussi avec un code de son application d'authentification ou un code de secours.
- `secret_totp_chiffre` : chiffré individuellement. Rempli seulement si la double authentification est activée.
- `canal_notification` : `email`, `telephone` ou `les_deux`, `email` par défaut.
- `nb_tentatives_echouees` : de 0 à 5, 0 par défaut. Remis à 0 après une connexion réussie, un lien de déblocage, une récupération réussie, ou au premier essai qui suit la fin d'un blocage.
- `nb_blocages` : 0 par défaut. Sert à doubler la durée de chaque nouveau blocage. Il s'arrête à 8 : la durée maximale est alors atteinte. Remis à 0 après une connexion réussie.
- `date_fin_blocage` : vide si le compte n'est pas bloqué.
- `date_demande_suppression` : vide s'il n'y a pas de demande. Effacement définitif 30 jours après.


## 4. Sécurité du compte

Ces données sont techniques. Selon le niveau de détail attendu, elles figurent dans le MCD ou ne sont ajoutées qu'au moment du MPD.

### 4.1 Codes de secours

| Code                          | Désignation                    | Type | Taille | Oblig. | Source |
|-------------------------------|--------------------------------|------|--------|--------|--------|
| id_code_secours               | Identifiant du code de secours | AN   | 36     | Oui    | SS §2  |
| empreinte_code_secours        | Empreinte du code de secours   | AN   | 255    | Oui    | SS §2  |
| date_utilisation_code_secours | Date d'utilisation du code     | DH   | —      | Non    | SS §2  |

**Contraintes**

- `id_code_secours` : UUID.
- `empreinte_code_secours` : haché comme un mot de passe, jamais en clair. 10 codes à usage unique sont remis à l'activation de la double authentification.
- `date_utilisation_code_secours` : vide tant que le code n'a pas servi. Usage unique.

### 4.2 Jetons (liens envoyés par e-mail)

| Code                   | Désignation                  | Type | Taille | Oblig. | Source         |
|------------------------|------------------------------|------|--------|--------|----------------|
| id_jeton               | Identifiant du jeton         | AN   | 36     | Oui    | SS §2          |
| type_jeton             | Usage du jeton               | AN   | 30     | Oui    | GR Auth, SS §2 |
| empreinte_jeton        | Empreinte du jeton           | AN   | 64     | Oui    | SS §2          |
| date_expiration_jeton  | Date d'expiration du jeton   | DH   | —      | Oui    | GR Auth, SS §2 |
| date_utilisation_jeton | Date d'utilisation du jeton  | DH   | —      | Non    | SS §2          |
| nouvel_email           | Nouvelle adresse à confirmer | AN   | 254    | Non    | GR Perso       |

**Contraintes**

- `id_jeton` : UUID.
- `type_jeton` : `confirmation_inscription`, `recuperation_mdp`, `deblocage` ou `changement_email`.
- `empreinte_jeton` : SHA-256, unique. Le jeton lui-même n'est jamais stocké.
- `date_expiration_jeton` : création + 24 h (inscription, changement d'e-mail), + 30 min (récupération), + 1 h (déblocage).
- `date_utilisation_jeton` : vide tant que le jeton n'a pas servi. Usage unique.
- `nouvel_email` : seulement pour le type `changement_email`.

### 4.3 Appareils

| Code           | Désignation                 | Type | Taille | Oblig. | Source   |
|----------------|-----------------------------|------|--------|--------|----------|
| id_appareil    | Identifiant de l'appareil   | AN   | 36     | Oui    | SS §3    |
| jeton_appareil | Jeton d'appareil            | AN   | 64     | Oui    | SS §3    |
| jeton_push     | Identifiant de notification | AN   | 255    | Non    | GR Notif |

**Contraintes**

- `id_appareil` : UUID.
- `jeton_appareil` : aléatoire et durable, enregistré à la première connexion, dans un cookie sur le web et dans le stockage sécurisé du téléphone dans l'application mobile. Seule son empreinte SHA-256 est stockée. Le cookie dure 1 an, renouvelé à chaque connexion. Une connexion sans ce jeton déclenche l'alerte « nouvel appareil ».
- `jeton_push` : fourni par l'application mobile quand l'utilisateur accepte les notifications sur son téléphone. Vide sinon.

### 4.4 Sessions

| Code                   | Désignation                     | Type | Taille | Oblig. | Source         |
|------------------------|---------------------------------|------|--------|--------|----------------|
| id_session             | Identifiant de session          | AN   | 64     | Oui    | SS §3          |
| date_derniere_activite | Dernière activité de la session | DH   | —      | Oui    | GR Auth, SS §3 |

**Contraintes**

- `id_session` : aléatoire, au moins 128 bits. Nouveau à chaque connexion. Seule son empreinte SHA-256 est stockée.
- `date_derniere_activite` : la session expire après 30 min sans activité.

### 4.5 Journal de sécurité

| Code                 | Désignation                | Type | Taille | Oblig. | Source     |
|----------------------|----------------------------|------|--------|--------|------------|
| id_evenement         | Identifiant de l'événement | AN   | 36     | Oui    | SS §11     |
| type_evenement       | Nature de l'événement      | AN   | 30     | Oui    | SS §8, §11 |
| resultat_evenement   | Résultat de l'événement    | AN   | 10     | Oui    | SS §11     |
| date_evenement       | Date de l'événement        | DH   | —      | Oui    | SS §11     |
| adresse_ip_evenement | Adresse IP d'origine       | AN   | 45     | Non    | SS §7, §11 |

**Contraintes**

- `id_evenement` : UUID.
- `type_evenement` : `connexion`, `blocage`, `deblocage`, `recuperation_mdp`, `changement_mdp`, `changement_email`, `activation_double_auth`, `desactivation_double_auth`, `connexion_banque`, `deconnexion_banque`, `export_donnees`, `demande_suppression`, `action_admin`, `changement_role` ou `acces_refuse`. `connexion_banque` sert aussi au renouvellement de l'autorisation bancaire.
- `resultat_evenement` : `succes` ou `echec`.
- `date_evenement` : conservé de 6 mois à 1 an.
- `adresse_ip_evenement` : IPv4 ou IPv6. Effacée quand le compte de l'utilisateur est supprimé.

Un événement n'est pas toujours rattaché à un utilisateur : une connexion échouée avec une adresse inconnue est enregistrée quand même. Quand un compte est supprimé, ses événements restent, mais sans lien vers l'utilisateur ni adresse IP.


## 5. Comptes bancaires et transactions

### 5.1 Connexion bancaire

| Code                         | Désignation                     | Type | Taille | Oblig. | Source     |
|------------------------------|---------------------------------|------|--------|--------|------------|
| id_connexion                 | Identifiant de la connexion     | AN   | 36     | Oui    | SS §4      |
| id_connexion_externe         | Identifiant chez le prestataire | AN   | 100    | Oui    | GR Comptes |
| nom_banque                   | Nom de la banque                | AN   | 100    | Oui    | GR Comptes |
| statut_connexion             | État de la connexion            | AN   | 10     | Oui    | GR Comptes |
| date_expiration_autorisation | Fin de l'autorisation bancaire  | DH   | —      | Oui    | GR Comptes |

**Contraintes**

- `id_connexion` : UUID.
- `id_connexion_externe` : fourni par le prestataire, unique. Sert à retrouver la connexion à chaque mise à jour.
- `nom_banque` : fourni par le prestataire, 1 à 100 caractères.
- `statut_connexion` : `active`, `en_erreur` ou `expiree`, `active` par défaut. Quand la banque ne répond pas, la connexion passe `en_erreur` et l'application affiche le dernier solde connu.
- `date_expiration_autorisation` : au plus 180 jours après l'autorisation donnée à la banque, moins si la banque n'accepte pas 180 jours. L'utilisateur est prévenu 7 jours avant.

Une connexion regroupe les comptes d'une même banque. La déconnecter supprime ses comptes et leurs transactions, et révoque l'autorisation (GR Comptes).

### 5.2 Compte bancaire

| Code              | Désignation                     | Type | Taille | Oblig. | Source            |
|-------------------|---------------------------------|------|--------|--------|-------------------|
| id_compte         | Identifiant du compte bancaire  | AN   | 36     | Oui    | SS §4             |
| id_compte_externe | Identifiant tiré du prestataire | AN   | 100    | Oui    | GR Comptes        |
| nom_compte        | Nom du compte                   | AN   | 50     | Oui    | GR Comptes, SS §5 |
| solde_compte      | Solde donné par la banque       | N    | 10     | Oui    | GR Comptes        |
| date_solde        | Date de la dernière mise à jour | DH   | —      | Oui    | GR Comptes        |

**Contraintes**

- `id_compte` : UUID.
- `id_compte_externe` : empreinte de l'identifiant stable donné par le prestataire, la même d'une autorisation à l'autre, unique pour une même connexion. Sert à retrouver le compte à chaque mise à jour.
- `nom_compte` : fourni par la banque, 1 à 50 caractères, tronqué au-delà. Exemple : « Compte courant ».
- `solde_compte` : en centimes, peut être négatif. Remplacé à chaque mise à jour.
- `date_solde` : date et heure de la dernière mise à jour réussie, affichées avec le solde.

### 5.3 Transaction

| Code                   | Désignation                     | Type | Taille | Oblig. | Source            |
|------------------------|---------------------------------|------|--------|--------|-------------------|
| id_transaction         | Identifiant de la transaction   | AN   | 36     | Oui    | SS §4             |
| id_transaction_externe | Identifiant tiré du prestataire | AN   | 100    | Oui    | GR Comptes        |
| date_transaction       | Date comptable de l'opération   | D    | —      | Oui    | GR Comptes, SS §5 |
| libelle_transaction    | Libellé de l'opération          | AN   | 255    | Oui    | GR Comptes, SS §5 |
| montant_transaction    | Montant de l'opération          | N    | 10     | Oui    | GR Comptes, SS §5 |
| est_transfert_interne  | Transfert entre ses comptes     | B    | —      | Oui    | GR Comptes        |

**Contraintes**

- `id_transaction` : UUID.
- `id_transaction_externe` : tiré de ce que donne le prestataire : l'empreinte de la référence de la banque, de la date et du montant, et du libellé quand la banque ne donne pas de référence, suivie d'un rang pour les transactions identiques du même jour, unique pour un même compte. Une opération déjà reçue n'est jamais importée deux fois.
- `date_transaction` : date comptable fournie par la banque, à partir du 01/01/2000.
- `libelle_transaction` : fourni par la banque, 1 à 255 caractères. Toujours affiché comme du texte.
- `montant_transaction` : fourni par la banque, en centimes, non nul. Négatif pour une dépense, positif pour un revenu. Au plus 1 000 000 000 en valeur absolue.
- `est_transfert_interne` : faux par défaut. C'est l'utilisateur qui marque une transaction comme transfert entre deux de ses comptes. Si vrai, la transaction n'est comptée ni en dépense ni en revenu.

Une transaction appartient à un seul compte et à une seule catégorie (GR Dépenses). Ces liens deviendront des associations dans le MCD.


## 6. Catégories et classement

### 6.1 Catégorie

| Code                 | Désignation                    | Type | Taille | Oblig. | Source             |
|----------------------|--------------------------------|------|--------|--------|--------------------|
| id_categorie         | Identifiant de la catégorie    | AN   | 36     | Oui    | SS §4              |
| nom_categorie        | Nom de la catégorie            | AN   | 50     | Oui    | GR Dépenses, SS §5 |
| budget_mensuel       | Budget mensuel de la catégorie | N    | 10     | Non    | GR Dépenses        |
| est_categorie_autres | Catégorie « Autres »           | B    | —      | Oui    | GR Dépenses        |

**Contraintes**

- `id_categorie` : UUID.
- `nom_categorie` : 1 à 50 caractères, sans caractère invisible. Unique pour un même utilisateur, sans tenir compte des majuscules.
- `budget_mensuel` : en centimes, supérieur à 0. Alertes à 80 % puis à 100 %.
- `est_categorie_autres` : vrai uniquement pour « Autres ». Cette catégorie ne peut être ni renommée ni supprimée, puisqu'elle reçoit les transactions des catégories supprimées.

À l'inscription, l'application crée les 8 catégories par défaut : Logement, Alimentation, Transport, Loisirs, Santé, Shopping, Revenus et Autres (GR Dépenses). Elles se renomment et se suppriment comme les autres, sauf « Autres ».

### 6.2 Règle de classement

| Code                | Désignation                           | Type | Taille | Oblig. | Source      |
|---------------------|---------------------------------------|------|--------|--------|-------------|
| id_regle_classement | Identifiant de la règle de classement | AN   | 36     | Oui    | GR Dépenses |
| libelle_regle       | Libellé visé par la règle             | AN   | 255    | Oui    | GR Dépenses |

**Contraintes**

- `id_regle_classement` : UUID.
- `libelle_regle` : la règle s'applique aux transactions qui ont le même libellé, sans tenir compte des chiffres, de la ponctuation, des majuscules, des accents ni des espaces en trop. Le libellé est enregistré sous cette forme : « CB CARREFOUR » pour « CB Carrefour 12/11 ».

Une règle de l'utilisateur associe un libellé à l'une de ses catégories. Elle est créée quand il accepte d'appliquer un changement de catégorie aux prochaines transactions qui ont le même libellé.

Les règles communes, comme « CARREFOUR » → Alimentation, font partie de la configuration de l'application (§12) : elles ne sont pas stockées pour chaque utilisateur. Une règle commune désigne une catégorie par son nom ; si l'utilisateur l'a renommée ou supprimée, la transaction va dans « Autres ». Elle reconnaît un mot entier du libellé, sans tenir compte des majuscules ni des accents ; la plus longue l'emporte. Le nom de la catégorie est comparé sans tenir compte des majuscules. Quand une règle de l'utilisateur et une règle commune visent le même libellé, celle de l'utilisateur l'emporte.


## 7. Sacs

### 7.1 Sac

| Code             | Désignation                | Type | Taille | Oblig. | Source         |
|------------------|----------------------------|------|--------|--------|----------------|
| id_sac           | Identifiant du sac         | AN   | 36     | Oui    | SS §4          |
| nom_sac          | Nom du sac                 | AN   | 50     | Oui    | GR Sacs, SS §5 |
| montant_objectif | Montant à atteindre        | N    | 10     | Non    | GR Sacs        |
| date_objectif    | Date visée pour l'objectif | D    | —      | Non    | GR Sacs        |

**Contraintes**

- `id_sac` : UUID.
- `nom_sac` : 1 à 50 caractères.
- `montant_objectif` : en centimes, supérieur à 0.
- `date_objectif` : postérieure à la date du jour au moment de la saisie.

Si le solde total passe sous le total des sacs, l'application prévient l'utilisateur et lui demande de retirer de l'argent d'un ou plusieurs sacs (GR Sacs).

### 7.2 Mouvement de sac

| Code              | Désignation              | Type | Taille | Oblig. | Source         |
|-------------------|--------------------------|------|--------|--------|----------------|
| id_mouvement      | Identifiant du mouvement | AN   | 36     | Oui    | SS §4          |
| montant_mouvement | Montant déplacé          | N    | 10     | Oui    | GR Sacs, SS §5 |
| date_mouvement    | Date du mouvement        | DH   | —      | Oui    | GR Sacs        |

**Contraintes**

- `id_mouvement` : UUID.
- `montant_mouvement` : en centimes, supérieur à 0. Ne doit ni rendre le sac de départ négatif, ni porter le total des sacs au-dessus du solde total.
- `date_mouvement` : renseignée automatiquement.

Un mouvement part de l'argent disponible ou d'un sac, et arrive dans un sac ou dans l'argent disponible : au moins l'un des deux est un sac, et ils sont différents (GR Sacs). Un mouvement créé en acceptant une suggestion est lié à cette suggestion. Ces liens deviendront des associations dans le MCD.


## 8. Suggestions d'épargne

| Code                   | Désignation                         | Type | Taille | Oblig. | Source  |
|------------------------|-------------------------------------|------|--------|--------|---------|
| id_suggestion          | Identifiant de la suggestion        | AN   | 36     | Oui    | SS §4   |
| regle_suggestion       | Règle qui a déclenché la suggestion | AN   | 30     | Oui    | GR Sacs |
| economie_mensuelle     | Économie estimée par mois           | N    | 10     | Oui    | GR Sacs |
| statut_suggestion      | État de la suggestion               | AN   | 10     | Oui    | GR Sacs |
| date_statut_suggestion | Date du dernier changement d'état   | DH   | —      | Non    | GR Sacs |

**Contraintes**

- `id_suggestion` : UUID.
- `regle_suggestion` : `depassement_categorie`, `argent_disponible` ou `epargne_insuffisante`.
- `economie_mensuelle` : en centimes, 0 ou plus.
- `statut_suggestion` : `proposee`, `acceptee` ou `ignoree`, `proposee` par défaut.
- `date_statut_suggestion` : une suggestion ignorée ne réapparaît pas pendant 30 jours.


## 9. Notifications

### 9.1 Type d'alerte

| Code                | Désignation                              | Type | Taille | Oblig. | Source   |
|---------------------|------------------------------------------|------|--------|--------|----------|
| code_type_alerte    | Identifiant du type d'alerte             | AN   | 30     | Oui    | GR Notif |
| libelle_type_alerte | Nom affiché du type d'alerte             | AN   | 100    | Oui    | GR Notif |
| est_desactivable    | Alerte que l'utilisateur peut désactiver | B    | —      | Oui    | GR Notif |

**Contraintes**

- `code_type_alerte` : liste fixe, voir ci-dessous.
- `libelle_type_alerte` : 1 à 100 caractères.
- `est_desactivable` : faux pour les alertes de sécurité.

| code_type_alerte      | Libellé                             | Désactivable |
|-----------------------|-------------------------------------|--------------|
| budget_atteint        | Budget d'une catégorie atteint      | Oui          |
| nouvelle_suggestion   | Nouvelle suggestion d'épargne       | Oui          |
| objectif_sac_atteint  | Objectif d'un sac atteint           | Oui          |
| autorisation_bancaire | Autorisation bancaire à renouveler  | Oui          |
| nouvel_appareil       | Connexion depuis un nouvel appareil | Non          |
| compte_bloque         | Compte bloqué                       | Non          |
| changement_mdp        | Changement de mot de passe          | Non          |
| changement_email      | Changement d'adresse e-mail         | Non          |

### 9.2 Préférence d'alerte

| Code           | Désignation                      | Type | Taille | Oblig. | Source   |
|----------------|----------------------------------|------|--------|--------|----------|
| alerte_activee | Alerte activée par l'utilisateur | B    | —      | Oui    | GR Notif |

**Contraintes**

- `alerte_activee` : vrai par défaut. Toujours vrai si le type d'alerte n'est pas désactivable.

Cette donnée dépend à la fois de l'utilisateur et du type d'alerte : elle sera portée par l'association entre UTILISATEUR et TYPE_ALERTE dans le MCD.


## 10. Support

### 10.1 Demande de support

| Code            | Désignation               | Type | Taille | Oblig. | Source              |
|-----------------|---------------------------|------|--------|--------|---------------------|
| id_demande      | Identifiant de la demande | AN   | 36     | Oui    | SS §4               |
| message_demande | Message de la demande     | AN   | 5000   | Oui    | GR Admin, SS §5, §8 |
| email_contact   | Adresse où répondre       | AN   | 254    | Non    | GR Admin            |
| statut_demande  | État de la demande        | AN   | 10     | Oui    | GR Admin            |

**Contraintes**

- `id_demande` : UUID.
- `message_demande` : 1 à 5 000 caractères. Toujours affiché comme du texte, y compris dans l'interface d'administration.
- `email_contact` : obligatoire si la demande vient d'un visiteur.
- `statut_demande` : `envoyee`, `en_cours` ou `resolue`, `envoyee` par défaut.

### 10.2 Réponse au support

| Code            | Désignation               | Type | Taille | Oblig. | Source   |
|-----------------|---------------------------|------|--------|--------|----------|
| id_reponse      | Identifiant de la réponse | AN   | 36     | Oui    | SS §4    |
| contenu_reponse | Texte de la réponse       | AN   | 5000   | Oui    | GR Admin |

**Contraintes**

- `id_reponse` : UUID.
- `contenu_reponse` : 1 à 5 000 caractères.


## 11. Données calculées

Ces données ne sont **pas stockées** : elles sont recalculées à chaque affichage. Les stocker créerait un risque d'incohérence, par exemple un solde de sac qui ne correspondrait plus à ses mouvements.

| Donnée                   | Calcul                                                    | Source         |
|--------------------------|-----------------------------------------------------------|----------------|
| solde_total              | Somme des soldes de tous les comptes de l'utilisateur     | GR Comptes     |
| solde_sac                | Mouvements arrivés dans le sac − mouvements partis du sac | GR Sacs        |
| argent_disponible        | solde_total − somme des soldes des sacs                   | GR Sacs        |
| progression_sac          | solde_sac ÷ montant_objectif × 100                        | GR Sacs        |
| depenses_categorie_mois  | Dépenses de la catégorie sur le mois (hors transferts)    | GR Dépenses    |
| moyenne_categorie_3_mois | Moyenne mensuelle de la catégorie sur 3 mois              | GR Sacs        |
| taux_budget              | depenses_categorie_mois ÷ budget_mensuel × 100            | GR Dépenses    |
| epargne_mois             | Revenus du mois − dépenses du mois (hors transferts)      | GR Sacs        |
| economies_realisees      | Somme des mouvements liés aux suggestions acceptées       | GR Sacs        |
| duree_blocage            | 15 minutes × 2^(nb_blocages − 1), 24 heures au plus       | GR Auth        |
| etat_compte              | État déduit de trois données (voir ci-dessous)            | GR Auth, SS §8 |
| date_effacement          | date_demande_suppression + 30 jours                       | GR Suppression |
| statistiques_admin       | Nombre d'utilisateurs…                                    | GR Admin       |

**Précisions**

- `moyenne_categorie_3_mois` : sert aussi à comparer le mois en cours avec les 3 derniers mois, catégorie par catégorie (GR Dépenses).
- `etat_compte` : vaut `non_confirme`, `actif`, `bloque` ou `en_suppression`, d'après email_confirme, date_fin_blocage et date_demande_suppression.


## 12. Paramètres de l'application

Ces valeurs sont les mêmes pour tous les utilisateurs. Elles relèvent de la configuration de l'application, pas des données de chaque utilisateur.

| Paramètre                                    | Valeur                           | Source         |
|----------------------------------------------|----------------------------------|----------------|
| Tentatives de connexion avant blocage        | 5                                | GR Auth        |
| Durée du premier blocage                     | 15 min, doublée à chaque blocage | GR Auth        |
| Durée maximale d'un blocage                  | 24 heures                        | GR Auth        |
| Longueur du mot de passe                     | 12 à 128 caractères              | GR Auth, SS §2 |
| Inactivité avant fermeture de la session     | 30 minutes                       | GR Auth        |
| Validité du lien de récupération             | 30 minutes                       | GR Auth        |
| Validité des liens de confirmation           | 24 heures (inscription, e-mail)  | SS §2          |
| Validité du lien de déblocage                | 1 heure                          | SS §2          |
| Nombre de codes de secours                   | 10                               | SS §2          |
| Durée d'une autorisation bancaire            | 180 jours au plus                | GR Comptes     |
| Rappel avant la fin de l'autorisation        | 7 jours                          | GR Comptes     |
| Mises à jour en arrière-plan                 | 4 par jour au plus               | GR Comptes     |
| Catégories créées à l'inscription            | 8 (voir §6.1)                    | GR Dépenses    |
| Catégories par utilisateur                   | 100 au plus                      | SS §6          |
| Règles de classement communes                | Liste fournie par l'application  | GR Dépenses    |
| Seuil de dépassement d'une catégorie         | Moyenne sur 3 mois + 20 %        | GR Sacs        |
| Historique minimal (suggestions par moyenne) | 3 mois                           | GR Sacs        |
| Seuil d'épargne mensuelle                    | 20 % des revenus                 | GR Sacs        |
| Réapparition d'une suggestion ignorée        | Après 30 jours                   | GR Sacs        |
| Seuils d'alerte de budget                    | 80 % et 100 %                    | GR Dépenses    |
| Délai avant effacement d'un compte           | 30 jours                         | GR Suppression |
| Effacement d'un compte non confirmé          | À l'expiration de son lien       | GR Auth        |
| Conservation des sauvegardes                 | 30 jours                         | SS §7          |
| Conservation du journal de sécurité          | 6 mois à 1 an                    | SS §11         |


## 13. Données volontairement non collectées

- **IBAN et numéro de compte** : même si le prestataire les fournit, ils ne sont pas enregistrés (SS §7).
- **Identifiants et mot de passe de la banque** : l'utilisateur s'identifie sur la page de sa banque (GR Comptes, SS §7).
- **Mots de passe, jetons et codes de secours en clair** : seules leurs empreintes sont stockées (SS §2).
