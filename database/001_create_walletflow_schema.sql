-- Copie exacte du script de la section 2 du MPD (Docs/merise/mpd.md).
-- Toute modification du schéma se fait d'abord dans le MPD, puis ici.

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
    CONSTRAINT ck_utilisateur_blocages CHECK (nb_blocages BETWEEN 0 AND 999)
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
    jeton_appareil VARCHAR(64)  NOT NULL UNIQUE,
    jeton_push     VARCHAR(255),
    id_utilisateur UUID         NOT NULL
        REFERENCES utilisateur (id_utilisateur) ON DELETE CASCADE
);

CREATE TABLE session (
    id_session             VARCHAR(128) PRIMARY KEY,
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
        'changement_email', 'activation_double_auth', 'connexion_banque', 'deconnexion_banque',
        'export_donnees', 'demande_suppression', 'action_admin', 'changement_role',
        'acces_refuse')),
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
CREATE FUNCTION fn_utilisateur_avant_suppression() RETURNS trigger LANGUAGE plpgsql AS $$
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

CREATE ROLE walletflow_app LOGIN;
GRANT USAGE ON SCHEMA public TO walletflow_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO walletflow_app;
REVOKE INSERT, UPDATE, DELETE ON type_alerte FROM walletflow_app;

COMMIT;
