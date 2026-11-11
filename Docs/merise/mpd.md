# MPD (Modèle physique de données)

Ce modèle physique de données (MPD) traduit le MLD (`mld.md`) pour PostgreSQL 13 ou une version plus récente. Il contient le script SQL de création de la base.

Le choix de PostgreSQL ne vient pas des règles de gestion : il a été fait au moment de rédiger ce MPD.


## 1. Choix physiques

### 1.1 Types

| Dictionnaire              | PostgreSQL         | Utilisation                                         |
|---------------------------|--------------------|-----------------------------------------------------|
| AN 36 (UUID)              | UUID               | Identifiants, générés par `gen_random_uuid()`       |
| AN n                      | VARCHAR(n)         | Textes                                              |
| AN 64 (empreinte SHA-256) | CHAR(64)           | `empreinte_jeton`, `jeton_appareil`, `id_session`   |
| AN 45 (adresse IP)        | INET               | `adresse_ip_evenement`, format IPv4 ou IPv6 vérifié |
| N (montant, 10 chiffres)  | INTEGER            | Montants en centimes                                |
| N 1                       | SMALLINT           | `nb_tentatives_echouees`, `nb_blocages`             |
| D                         | DATE               | `date_transaction`, `date_objectif`                 |
| DH                        | TIMESTAMPTZ        | Dates et heures, avec fuseau horaire                |
| B                         | BOOLEAN            | Booléens                                            |
| Liste de valeurs          | VARCHAR(n) + CHECK | Codes courts (`role_utilisateur`, statuts…)         |

Les montants tiennent dans un INTEGER : le dictionnaire les limite à 1 000 000 000 centimes en valeur absolue. Les sommes (solde total, totaux des sacs) sont calculées par PostgreSQL en BIGINT, sans risque de dépassement.

### 1.2 Nommage

- Tables et colonnes : les noms du MLD, en minuscules.
- Contraintes : `ck_` pour une vérification (CHECK), `ix_` pour un index, `ux_` pour un index unique, `fn_` et `tg_` pour les fonctions et les déclencheurs.

### 1.3 Suppressions

| Clé étrangère                     | ON DELETE           | Raison                                        |
|-----------------------------------|---------------------|-----------------------------------------------|
| Vers utilisateur, sauf le journal | CASCADE             | Effacement définitif du compte                |
| evenement_securite.id_utilisateur | SET NULL            | Événements gardés, sans identifiant           |
| compte_bancaire.id_connexion      | CASCADE             | Comptes supprimés avec leur connexion         |
| transaction.id_compte             | CASCADE             | Transactions supprimées avec leur compte      |
| transaction.id_categorie          | NO ACTION, différée | Transactions déplacées dans « Autres » avant  |
| regle_classement.id_categorie     | CASCADE             | Une règle n'a plus d'objet sans sa catégorie  |
| suggestion.id_categorie           | SET NULL            | Les économies déjà réalisées restent comptées |
| mouvement_sac (les deux sacs)     | SET NULL            | L'autre sac garde son solde                   |
| mouvement_sac.id_suggestion       | SET NULL            | Le mouvement reste dans l'historique du sac   |
| reponse_support.id_demande        | CASCADE             | Les réponses suivent leur demande             |
| parametrer.code_type_alerte       | NO ACTION           | Les types d'alerte forment une liste fixe     |

Ces choix appliquent les règles de gestion sur la suppression d'un compte, la déconnexion d'une banque, la suppression d'une catégorie et celle d'un sac (GR Suppression, GR Comptes, GR Dépenses, GR Sacs), ainsi que la liste des données effacées de la stratégie de sécurité (SS §7).

La clé `transaction.id_categorie` n'est vérifiée qu'à la fin de la transaction SQL (`DEFERRABLE INITIALLY DEFERRED`). Ainsi, la suppression d'un compte utilisateur, qui efface en même temps ses catégories et ses transactions, ne dépend pas de l'ordre dans lequel PostgreSQL les supprime.


## 2. Script SQL

```sql
-- WalletFlow : création de la base (PostgreSQL 13 ou plus)
-- À exécuter sur une base vide, avec un compte administrateur de PostgreSQL.

BEGIN;

-- 1. Utilisateur et sécurité du compte (GR Auth, GR Perso, SS §2, §3, §11)

CREATE TABLE utilisateur (
    id_utilisateur           UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    email_utilisateur        VARCHAR(254) NOT NULL UNIQUE,
    mot_de_passe_hash        VARCHAR(255) NOT NULL,
    nom_utilisateur          VARCHAR(50),
    prenom_utilisateur       VARCHAR(50),
    role_utilisateur         VARCHAR(20)  NOT NULL DEFAULT 'utilisateur',
    email_confirme           BOOLEAN      NOT NULL DEFAULT FALSE,
    double_auth_activee      BOOLEAN      NOT NULL DEFAULT FALSE,
    secret_totp_chiffre      VARCHAR(255),
    canal_notification       VARCHAR(10)  NOT NULL DEFAULT 'email',
    nb_tentatives_echouees   SMALLINT     NOT NULL DEFAULT 0,
    nb_blocages              SMALLINT     NOT NULL DEFAULT 0,
    date_fin_blocage         TIMESTAMPTZ,
    date_demande_suppression TIMESTAMPTZ,
    CONSTRAINT ck_utilisateur_email CHECK (email_utilisateur = lower(email_utilisateur)),
    CONSTRAINT ck_utilisateur_nom CHECK (char_length(nom_utilisateur) >= 1),
    CONSTRAINT ck_utilisateur_prenom CHECK (char_length(prenom_utilisateur) >= 1),
    CONSTRAINT ck_utilisateur_role CHECK (role_utilisateur IN ('utilisateur', 'administrateur')),
    CONSTRAINT ck_utilisateur_totp CHECK (double_auth_activee = (secret_totp_chiffre IS NOT NULL)),
    CONSTRAINT ck_utilisateur_canal CHECK (
        canal_notification IN ('email', 'telephone', 'les_deux')),
    CONSTRAINT ck_utilisateur_tentatives CHECK (nb_tentatives_echouees BETWEEN 0 AND 5),
    CONSTRAINT ck_utilisateur_blocages CHECK (nb_blocages BETWEEN 0 AND 8)
);

CREATE TABLE code_secours (
    id_code_secours               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    empreinte_code_secours        VARCHAR(255) NOT NULL,
    date_utilisation_code_secours TIMESTAMPTZ,
    id_utilisateur                UUID         NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE
);

CREATE TABLE jeton (
    id_jeton               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    type_jeton             VARCHAR(30)  NOT NULL,
    empreinte_jeton        CHAR(64)     NOT NULL UNIQUE,
    date_expiration_jeton  TIMESTAMPTZ  NOT NULL,
    date_utilisation_jeton TIMESTAMPTZ,
    nouvel_email           VARCHAR(254),
    id_utilisateur         UUID         NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE,
    CONSTRAINT ck_jeton_type CHECK (type_jeton IN (
        'confirmation_inscription', 'recuperation_mdp', 'deblocage', 'changement_email')),
    CONSTRAINT ck_jeton_nouvel_email CHECK (
        (type_jeton = 'changement_email') = (nouvel_email IS NOT NULL)),
    CONSTRAINT ck_jeton_nouvel_email_minuscules CHECK (nouvel_email = lower(nouvel_email))
);

CREATE TABLE appareil (
    id_appareil    UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    jeton_appareil CHAR(64)     NOT NULL UNIQUE,
    jeton_push     VARCHAR(255),
    id_utilisateur UUID         NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE
);

CREATE TABLE session (
    id_session             CHAR(64)     PRIMARY KEY,
    date_derniere_activite TIMESTAMPTZ  NOT NULL DEFAULT now(),
    id_utilisateur         UUID         NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE
);

CREATE TABLE evenement_securite (
    id_evenement         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    type_evenement       VARCHAR(30) NOT NULL,
    resultat_evenement   VARCHAR(10) NOT NULL,
    date_evenement       TIMESTAMPTZ NOT NULL DEFAULT now(),
    adresse_ip_evenement INET,
    id_utilisateur       UUID
        REFERENCES utilisateur (id_utilisateur) ON DELETE SET NULL,
    CONSTRAINT ck_evenement_type CHECK (type_evenement IN (
        'connexion', 'blocage', 'deblocage', 'recuperation_mdp', 'changement_mdp',
        'changement_email', 'activation_double_auth', 'desactivation_double_auth',
        'connexion_banque', 'deconnexion_banque', 'export_donnees', 'demande_suppression',
        'action_admin', 'changement_role', 'acces_refuse')),
    CONSTRAINT ck_evenement_resultat CHECK (resultat_evenement IN ('succes', 'echec'))
);

-- 2. Banques, comptes, transactions et catégories (GR Comptes, GR Dépenses)

CREATE TABLE connexion_bancaire (
    id_connexion                 UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    id_connexion_externe         VARCHAR(100) NOT NULL UNIQUE,
    nom_banque                   VARCHAR(100) NOT NULL,
    statut_connexion             VARCHAR(10)  NOT NULL DEFAULT 'active',
    date_expiration_autorisation TIMESTAMPTZ  NOT NULL,
    id_utilisateur               UUID         NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE,
    CONSTRAINT ck_connexion_banque CHECK (char_length(nom_banque) >= 1),
    CONSTRAINT ck_connexion_statut CHECK (statut_connexion IN ('active', 'en_erreur', 'expiree'))
);

CREATE TABLE compte_bancaire (
    id_compte         UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    id_compte_externe VARCHAR(100) NOT NULL,
    nom_compte        VARCHAR(50)  NOT NULL,
    solde_compte      INTEGER      NOT NULL,
    date_solde        TIMESTAMPTZ  NOT NULL,
    id_connexion      UUID         NOT NULL
        REFERENCES connexion_bancaire (id_connexion) ON DELETE CASCADE,
    CONSTRAINT ux_compte_externe UNIQUE (id_connexion, id_compte_externe),
    CONSTRAINT ck_compte_nom CHECK (char_length(nom_compte) >= 1),
    CONSTRAINT ck_compte_solde CHECK (solde_compte BETWEEN -1000000000 AND 1000000000)
);

CREATE TABLE categorie (
    id_categorie         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    nom_categorie        VARCHAR(50) NOT NULL,
    budget_mensuel       INTEGER,
    est_categorie_autres BOOLEAN     NOT NULL DEFAULT FALSE,
    id_utilisateur       UUID        NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE,
    CONSTRAINT ck_categorie_nom CHECK (char_length(nom_categorie) >= 1),
    CONSTRAINT ck_categorie_budget CHECK (budget_mensuel BETWEEN 1 AND 1000000000)
);

-- Un utilisateur n'a qu'une seule catégorie « Autres ».
CREATE UNIQUE INDEX ux_categorie_autres ON categorie (id_utilisateur) WHERE est_categorie_autres;

CREATE TABLE transaction (
    id_transaction         UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    id_transaction_externe VARCHAR(100) NOT NULL,
    date_transaction       DATE         NOT NULL,
    libelle_transaction    VARCHAR(255) NOT NULL,
    montant_transaction    INTEGER      NOT NULL,
    est_transfert_interne  BOOLEAN      NOT NULL DEFAULT FALSE,
    id_compte              UUID         NOT NULL
        REFERENCES compte_bancaire (id_compte) ON DELETE CASCADE,
    id_categorie           UUID         NOT NULL
        REFERENCES categorie (id_categorie) DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT ux_transaction_externe UNIQUE (id_compte, id_transaction_externe),
    CONSTRAINT ck_transaction_date CHECK (date_transaction >= DATE '2000-01-01'),
    CONSTRAINT ck_transaction_libelle CHECK (char_length(libelle_transaction) >= 1),
    CONSTRAINT ck_transaction_montant CHECK (
        montant_transaction <> 0 AND montant_transaction BETWEEN -1000000000 AND 1000000000)
);

CREATE TABLE regle_classement (
    id_regle_classement UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    libelle_regle       VARCHAR(255) NOT NULL,
    id_categorie        UUID         NOT NULL
        REFERENCES categorie (id_categorie) ON DELETE CASCADE,
    CONSTRAINT ck_regle_libelle CHECK (char_length(libelle_regle) >= 1)
);

-- 3. Sacs et suggestions (GR Sacs)

CREATE TABLE sac (
    id_sac           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    nom_sac          VARCHAR(50) NOT NULL,
    montant_objectif INTEGER,
    date_objectif    DATE,
    id_utilisateur   UUID        NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE,
    CONSTRAINT ck_sac_nom CHECK (char_length(nom_sac) >= 1),
    CONSTRAINT ck_sac_objectif CHECK (montant_objectif BETWEEN 1 AND 1000000000),
    CONSTRAINT ck_sac_date_objectif CHECK (date_objectif IS NULL OR montant_objectif IS NOT NULL)
);

CREATE TABLE suggestion (
    id_suggestion          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    regle_suggestion       VARCHAR(30) NOT NULL,
    economie_mensuelle     INTEGER     NOT NULL,
    statut_suggestion      VARCHAR(10) NOT NULL DEFAULT 'proposee',
    date_statut_suggestion TIMESTAMPTZ,
    id_utilisateur         UUID        NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE,
    id_categorie           UUID
        REFERENCES categorie (id_categorie) ON DELETE SET NULL,
    CONSTRAINT ck_suggestion_regle CHECK (regle_suggestion IN (
        'depassement_categorie', 'argent_disponible', 'epargne_insuffisante')),
    CONSTRAINT ck_suggestion_economie CHECK (economie_mensuelle BETWEEN 0 AND 1000000000),
    CONSTRAINT ck_suggestion_statut CHECK (
        statut_suggestion IN ('proposee', 'acceptee', 'ignoree')),
    CONSTRAINT ck_suggestion_categorie CHECK (
        id_categorie IS NULL OR regle_suggestion = 'depassement_categorie')
);

CREATE TABLE mouvement_sac (
    id_mouvement      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    montant_mouvement INTEGER     NOT NULL,
    date_mouvement    TIMESTAMPTZ NOT NULL DEFAULT now(),
    id_sac_depart     UUID        REFERENCES sac (id_sac) ON DELETE SET NULL,
    id_sac_arrivee    UUID        REFERENCES sac (id_sac) ON DELETE SET NULL,
    id_suggestion     UUID        UNIQUE
        REFERENCES suggestion (id_suggestion) ON DELETE SET NULL,
    CONSTRAINT ck_mouvement_montant CHECK (montant_mouvement BETWEEN 1 AND 1000000000),
    CONSTRAINT ck_mouvement_un_sac CHECK (id_sac_depart IS NOT NULL OR id_sac_arrivee IS NOT NULL),
    CONSTRAINT ck_mouvement_sacs_differents CHECK (id_sac_depart <> id_sac_arrivee)
);

-- 4. Notifications et support (GR Notif, GR Admin)

CREATE TABLE type_alerte (
    code_type_alerte    VARCHAR(30)  PRIMARY KEY,
    libelle_type_alerte VARCHAR(100) NOT NULL,
    est_desactivable    BOOLEAN      NOT NULL,
    CONSTRAINT ck_type_alerte_libelle CHECK (char_length(libelle_type_alerte) >= 1)
);

CREATE TABLE parametrer (
    id_utilisateur   UUID        NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE,
    code_type_alerte VARCHAR(30) NOT NULL
        REFERENCES type_alerte (code_type_alerte),
    alerte_activee   BOOLEAN     NOT NULL DEFAULT TRUE,
    PRIMARY KEY (id_utilisateur, code_type_alerte)
);

CREATE TABLE demande_support (
    id_demande      UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    message_demande VARCHAR(5000) NOT NULL,
    email_contact   VARCHAR(254),
    statut_demande  VARCHAR(10)   NOT NULL DEFAULT 'envoyee',
    id_utilisateur  UUID
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE,
    CONSTRAINT ck_demande_message CHECK (char_length(message_demande) >= 1),
    CONSTRAINT ck_demande_contact CHECK (id_utilisateur IS NOT NULL OR email_contact IS NOT NULL),
    CONSTRAINT ck_demande_statut CHECK (statut_demande IN ('envoyee', 'en_cours', 'resolue'))
);

CREATE TABLE reponse_support (
    id_reponse      UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    contenu_reponse VARCHAR(5000) NOT NULL,
    id_demande      UUID          NOT NULL
        REFERENCES demande_support (id_demande) ON DELETE CASCADE,
    CONSTRAINT ck_reponse_contenu CHECK (char_length(contenu_reponse) >= 1)
);

-- 5. Index sur les clés étrangères et les recherches fréquentes

CREATE INDEX ix_code_secours_utilisateur ON code_secours (id_utilisateur);
CREATE INDEX ix_jeton_utilisateur ON jeton (id_utilisateur);
CREATE INDEX ix_appareil_utilisateur ON appareil (id_utilisateur);
CREATE INDEX ix_session_utilisateur ON session (id_utilisateur);
CREATE INDEX ix_evenement_utilisateur ON evenement_securite (id_utilisateur);
CREATE INDEX ix_evenement_date ON evenement_securite (date_evenement);
CREATE INDEX ix_connexion_utilisateur ON connexion_bancaire (id_utilisateur);
CREATE INDEX ix_categorie_utilisateur ON categorie (id_utilisateur);
CREATE INDEX ix_transaction_compte_date ON transaction (id_compte, date_transaction);
CREATE INDEX ix_transaction_categorie ON transaction (id_categorie);
CREATE INDEX ix_regle_categorie ON regle_classement (id_categorie);
CREATE INDEX ix_sac_utilisateur ON sac (id_utilisateur);
CREATE INDEX ix_suggestion_utilisateur ON suggestion (id_utilisateur);
CREATE INDEX ix_suggestion_categorie ON suggestion (id_categorie);
CREATE INDEX ix_mouvement_depart ON mouvement_sac (id_sac_depart);
CREATE INDEX ix_mouvement_arrivee ON mouvement_sac (id_sac_arrivee);
CREATE INDEX ix_demande_utilisateur ON demande_support (id_utilisateur);
CREATE INDEX ix_reponse_demande ON reponse_support (id_demande);

-- 6. Données de référence : les 8 types d'alerte (GR Notif)

INSERT INTO type_alerte (code_type_alerte, libelle_type_alerte, est_desactivable) VALUES
    ('budget_atteint',        'Budget d''une catégorie atteint',      TRUE),
    ('nouvelle_suggestion',   'Nouvelle suggestion d''épargne',       TRUE),
    ('objectif_sac_atteint',  'Objectif d''un sac atteint',           TRUE),
    ('autorisation_bancaire', 'Autorisation bancaire à renouveler',   TRUE),
    ('nouvel_appareil',       'Connexion depuis un nouvel appareil',  FALSE),
    ('compte_bloque',         'Compte bloqué',                        FALSE),
    ('changement_mdp',        'Changement de mot de passe',           FALSE),
    ('changement_email',      'Changement d''adresse e-mail',         FALSE);

-- 7. Déclencheurs

-- GR Sacs : supprimer un sac remet son montant dans l'argent disponible.
-- Ses mouvements avec l'argent disponible disparaissent ; ceux avec un autre sac
-- gardent l'autre sac (ON DELETE SET NULL), dont le solde ne change donc pas.
CREATE FUNCTION fn_sac_avant_suppression() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    DELETE FROM mouvement_sac
    WHERE (id_sac_depart = OLD.id_sac AND id_sac_arrivee IS NULL)
       OR (id_sac_arrivee = OLD.id_sac AND id_sac_depart IS NULL);
    RETURN OLD;
END;
$$;

CREATE TRIGGER tg_sac_avant_suppression
    BEFORE DELETE ON sac
    FOR EACH ROW EXECUTE FUNCTION fn_sac_avant_suppression();

-- GR Suppression : effacement définitif d'un compte utilisateur.
-- Les mouvements de ses sacs sont supprimés avant les sacs eux-mêmes, et ses
-- événements du journal perdent l'adresse IP (l'identifiant part avec ON DELETE SET NULL).
-- SECURITY DEFINER : la fonction a les droits du propriétaire du script, car l'application
-- ne peut pas modifier le journal elle-même (section 8).
CREATE FUNCTION fn_utilisateur_avant_suppression() RETURNS trigger LANGUAGE plpgsql
    SECURITY DEFINER SET search_path = public AS $$
BEGIN
    DELETE FROM mouvement_sac m
    USING sac s
    WHERE s.id_utilisateur = OLD.id_utilisateur
      AND s.id_sac IN (m.id_sac_depart, m.id_sac_arrivee);

    UPDATE evenement_securite
    SET adresse_ip_evenement = NULL
    WHERE id_utilisateur = OLD.id_utilisateur;

    RETURN OLD;
END;
$$;

CREATE TRIGGER tg_utilisateur_avant_suppression
    BEFORE DELETE ON utilisateur
    FOR EACH ROW EXECUTE FUNCTION fn_utilisateur_avant_suppression();

-- GR Notif : les alertes de sécurité ne peuvent pas être désactivées.
CREATE FUNCTION fn_parametrer_controle() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT NEW.alerte_activee AND EXISTS (
        SELECT 1 FROM type_alerte
        WHERE code_type_alerte = NEW.code_type_alerte AND NOT est_desactivable
    ) THEN
        RAISE EXCEPTION 'L''alerte % ne peut pas être désactivée', NEW.code_type_alerte;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER tg_parametrer_controle
    BEFORE INSERT OR UPDATE ON parametrer
    FOR EACH ROW EXECUTE FUNCTION fn_parametrer_controle();

-- 8. Utilisateur de base de données de l'application (SS §7)
-- Il lit et écrit les données, mais ne peut ni supprimer de table ni modifier
-- les types d'alerte. Son mot de passe se définit à part, jamais dans ce script.
-- Il ne peut ni choisir ni changer le rôle d'un utilisateur : le rôle administrateur
-- ne s'attribue que depuis le serveur, avec un compte administrateur de PostgreSQL (SS §8 et §14).
-- Il ne peut qu'ajouter des événements au journal de sécurité, jamais les modifier
-- ni les effacer (SS §11).

CREATE ROLE walletflow_app LOGIN;
GRANT USAGE ON SCHEMA public TO walletflow_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO walletflow_app;
REVOKE INSERT, UPDATE, DELETE ON type_alerte FROM walletflow_app;
REVOKE UPDATE, DELETE ON evenement_securite FROM walletflow_app;
REVOKE INSERT, UPDATE ON utilisateur FROM walletflow_app;
GRANT INSERT (
    id_utilisateur, email_utilisateur, mot_de_passe_hash, nom_utilisateur, prenom_utilisateur,
    email_confirme, double_auth_activee, secret_totp_chiffre, canal_notification,
    nb_tentatives_echouees, nb_blocages, date_fin_blocage, date_demande_suppression)
    ON utilisateur TO walletflow_app;
GRANT UPDATE (
    email_utilisateur, mot_de_passe_hash, nom_utilisateur, prenom_utilisateur,
    email_confirme, double_auth_activee, secret_totp_chiffre, canal_notification,
    nb_tentatives_echouees, nb_blocages, date_fin_blocage, date_demande_suppression)
    ON utilisateur TO walletflow_app;

COMMIT;
```


## 3. Qui vérifie quoi

**Vérifié par la base de données**
- Les valeurs possibles, les longueurs minimales et les bornes des montants (contraintes `ck_`).
- Le secret TOTP rempli seulement quand la double authentification est activée ; la nouvelle adresse seulement pour un jeton de changement d'e-mail.
- La date d'objectif seulement avec un montant ; un ou deux sacs, différents, pour chaque mouvement.
- Une seule catégorie « Autres » par utilisateur (`ux_categorie_autres`).
- Chaque compte et chaque opération reçus de la banque, enregistrés une seule fois (`ux_compte_externe`, `ux_transaction_externe`).
- Une alerte non désactivable toujours activée (`tg_parametrer_controle`).
- La suppression d'un sac et l'effacement d'un compte utilisateur (`tg_sac_avant_suppression`, `tg_utilisateur_avant_suppression` et les règles ON DELETE).
- Le rôle d'un utilisateur, que l'application ne peut ni choisir ni changer, et le journal de sécurité, auquel elle ne peut qu'ajouter des événements (droits de `walletflow_app`, section 8 du script).

**Vérifié par l'application**
- L'appartenance au même utilisateur : la catégorie d'une transaction et son compte, les deux sacs d'un mouvement (SS §4).
- Un sac jamais négatif et un total des sacs qui ne dépasse pas le solde total. Le contrôle se fait dans une transaction SQL qui verrouille les lignes concernées (`SELECT … FOR UPDATE`), pour que deux déplacements simultanés ne le contournent pas (SS §5).
- La borne qui dépend de la date du jour : une date d'objectif dans le futur. Une contrainte CHECK ne peut pas la vérifier de façon fiable.
- La catégorie « Autres » ni renommée ni supprimée, et les transactions d'une catégorie déplacées dans « Autres » avant sa suppression (GR Dépenses).
- La création des 8 catégories par défaut, dans la même transaction SQL que l'utilisateur (cardinalité 1,n de DISPOSER).
- Le hachage des mots de passe et des codes de secours, et le chiffrement du secret TOTP, avant l'enregistrement (SS §2).
- Les durées : expiration des jetons, fermeture des sessions après 30 minutes d'inactivité, mise à jour des comptes jusqu'à 4 fois par jour, rappel 7 jours avant la fin d'une autorisation bancaire, effacement des comptes 30 jours après la demande, effacement des comptes non confirmés une fois leur lien expiré, conservation du journal de 6 mois à 1 an, réapparition d'une suggestion ignorée après 30 jours. Ce sont des tâches planifiées.
- La révocation de l'autorisation chez le prestataire, quand l'utilisateur déconnecte une banque ou demande la suppression de son compte (GR Comptes, GR Suppression).


## 4. Mise en place

1. Créer une base vide, puis y exécuter le script de la section 2 avec un compte administrateur de PostgreSQL.
2. Définir le mot de passe de `walletflow_app` à part, par exemple avec `ALTER ROLE walletflow_app PASSWORD '…'`, et le ranger dans les secrets de l'application (SS §7).
3. Configurer l'application pour qu'elle se connecte avec `walletflow_app`, jamais avec le compte administrateur.
