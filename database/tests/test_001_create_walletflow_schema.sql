-- Tests du script de création de la base (database/001_create_walletflow_schema.sql).
-- Ils vérifient les contraintes, les déclencheurs et les suppressions en cascade du MPD.
-- Tout se passe dans une seule transaction, annulée à la fin : la base reste vide.
--
-- Lancement, depuis le dossier WalletFlow-Backend :
--   psql -h localhost -U walletflow_app -d walletflow -v ON_ERROR_STOP=1 -f database/tests/test_001_create_walletflow_schema.sql

\set QUIET on
\set VERBOSITY terse
BEGIN;


-- Outil des tests : exécute une instruction qui doit être refusée, puis vérifie que la base
-- l'a refusée avec le code d'erreur attendu (23514 : CHECK, 23505 : unicité,
-- P0001 : refus d'un déclencheur, 42501 : droit refusé à l'application).
-- L'instruction refusée ne laisse aucune trace.
CREATE FUNCTION pg_temp.verifier_que_la_base_refuse_cette_instruction(
    instruction_sql_qui_doit_etre_refusee text,
    code_d_erreur_attendu_par_ce_test text,
    regle_verifiee_par_cette_instruction text)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    BEGIN
        EXECUTE instruction_sql_qui_doit_etre_refusee;
    EXCEPTION WHEN OTHERS THEN
        IF SQLSTATE = code_d_erreur_attendu_par_ce_test THEN
            RAISE NOTICE 'OK : %', regle_verifiee_par_cette_instruction;
            RETURN;
        END IF;
        RAISE EXCEPTION 'ÉCHEC : % (erreur % au lieu de %)',
            regle_verifiee_par_cette_instruction, SQLSTATE, code_d_erreur_attendu_par_ce_test;
    END;
    RAISE EXCEPTION 'ÉCHEC : % (la base a accepté l''instruction)', regle_verifiee_par_cette_instruction;
END;
$$;


-- Données communes aux tests de contraintes : un utilisateur, sa catégorie « Autres », un compte
-- avec une opération déjà reçue, un sac, et une suggestion acceptée qui a déjà créé son mouvement.
CREATE TEMP TABLE donnees_communes_aux_tests_de_contraintes (nom text PRIMARY KEY, identifiant uuid NOT NULL);

DO $$
DECLARE
    identifiant_de_l_utilisateur_des_contraintes uuid;
    identifiant_de_la_categorie_autres_commune uuid;
    identifiant_de_la_connexion_bancaire_commune uuid;
    identifiant_du_compte_bancaire_commun_aux_tests uuid;
    identifiant_du_sac_commun_aux_tests_contraintes uuid;
    identifiant_de_la_suggestion_commune_aux_tests uuid;
BEGIN
    INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash)
    VALUES ('test-contraintes@exemple.test', 'empreinte-de-test')
    RETURNING id_utilisateur INTO identifiant_de_l_utilisateur_des_contraintes;

    INSERT INTO categorie (nom_categorie, est_categorie_autres, id_utilisateur)
    VALUES ('Autres', TRUE, identifiant_de_l_utilisateur_des_contraintes)
    RETURNING id_categorie INTO identifiant_de_la_categorie_autres_commune;

    INSERT INTO connexion_bancaire (id_connexion_externe, nom_banque, date_expiration_autorisation, id_utilisateur)
    VALUES ('connexion-contraintes', 'Banque de test', now() + interval '180 days', identifiant_de_l_utilisateur_des_contraintes)
    RETURNING id_connexion INTO identifiant_de_la_connexion_bancaire_commune;

    INSERT INTO compte_bancaire (id_compte_externe, nom_compte, solde_compte, date_solde, id_connexion)
    VALUES ('compte-contraintes', 'Compte courant', 150000, now(), identifiant_de_la_connexion_bancaire_commune)
    RETURNING id_compte INTO identifiant_du_compte_bancaire_commun_aux_tests;

    INSERT INTO transaction (id_transaction_externe, date_transaction, libelle_transaction,
                             montant_transaction, id_compte, id_categorie)
    VALUES ('operation-deja-recue', current_date, 'CARREFOUR', -4250,
            identifiant_du_compte_bancaire_commun_aux_tests, identifiant_de_la_categorie_autres_commune);

    INSERT INTO sac (nom_sac, id_utilisateur)
    VALUES ('Vacances', identifiant_de_l_utilisateur_des_contraintes)
    RETURNING id_sac INTO identifiant_du_sac_commun_aux_tests_contraintes;

    INSERT INTO suggestion (regle_suggestion, economie_mensuelle, statut_suggestion, id_utilisateur)
    VALUES ('argent_disponible', 3000, 'acceptee', identifiant_de_l_utilisateur_des_contraintes)
    RETURNING id_suggestion INTO identifiant_de_la_suggestion_commune_aux_tests;

    INSERT INTO mouvement_sac (montant_mouvement, id_sac_arrivee, id_suggestion)
    VALUES (3000, identifiant_du_sac_commun_aux_tests_contraintes, identifiant_de_la_suggestion_commune_aux_tests);

    INSERT INTO donnees_communes_aux_tests_de_contraintes VALUES
        ('utilisateur', identifiant_de_l_utilisateur_des_contraintes),
        ('categorie_autres', identifiant_de_la_categorie_autres_commune),
        ('compte', identifiant_du_compte_bancaire_commun_aux_tests),
        ('sac', identifiant_du_sac_commun_aux_tests_contraintes),
        ('suggestion', identifiant_de_la_suggestion_commune_aux_tests);
END;
$$;


-- 1. Contraintes : chaque instruction ci-dessous enfreint une règle du MPD et doit être refusée.
DO $$
BEGIN
    -- Utilisateur (DD §3)
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash)
             VALUES ('Majuscules@Exemple.test', 'empreinte')$sql$,
        '23514', 'une adresse e-mail est enregistrée en minuscules');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash)
             VALUES ('test-contraintes@exemple.test', 'empreinte')$sql$,
        '23505', 'deux comptes ne partagent jamais la même adresse e-mail');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash, role_utilisateur)
             VALUES ('role@exemple.test', 'empreinte', 'administrateur')$sql$,
        '42501', 'l''application ne peut pas choisir le rôle d''un nouveau compte');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash, nb_blocages)
             VALUES ('blocages@exemple.test', 'empreinte', 9)$sql$,
        '23514', 'le nombre de blocages s''arrête à 8');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash, secret_totp_chiffre)
             VALUES ('totp@exemple.test', 'empreinte', 'secret-chiffre')$sql$,
        '23514', 'un secret TOTP n''existe que si la double authentification est activée');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash, double_auth_activee)
             VALUES ('totp-manquant@exemple.test', 'empreinte', TRUE)$sql$,
        '23514', 'la double authentification activée a toujours un secret TOTP');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash, nb_tentatives_echouees)
             VALUES ('tentatives@exemple.test', 'empreinte', 6)$sql$,
        '23514', 'il y a au plus 5 tentatives de connexion échouées');

    -- Liens envoyés par e-mail (DD §4.2)
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO jeton (type_jeton, empreinte_jeton, date_expiration_jeton, id_utilisateur)
             SELECT 'changement_email', repeat('b', 64), now(), identifiant
             FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'utilisateur'$sql$,
        '23514', 'un lien de changement d''e-mail porte toujours la nouvelle adresse');

    -- Transactions et catégories (DD §5.3 et §6.1)
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO transaction (id_transaction_externe, date_transaction, libelle_transaction,
                                      montant_transaction, id_compte, id_categorie)
             SELECT 'operation-nulle', current_date, 'ESSAI', 0,
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'compte'),
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'categorie_autres')$sql$,
        '23514', 'une transaction n''a jamais un montant nul');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO transaction (id_transaction_externe, date_transaction, libelle_transaction,
                                      montant_transaction, id_compte, id_categorie)
             SELECT 'operation-ancienne', DATE '1999-12-31', 'ESSAI', -100,
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'compte'),
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'categorie_autres')$sql$,
        '23514', 'une transaction date au plus tôt du 01/01/2000');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO transaction (id_transaction_externe, date_transaction, libelle_transaction,
                                      montant_transaction, id_compte, id_categorie)
             SELECT 'operation-deja-recue', current_date, 'CARREFOUR', -4250,
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'compte'),
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'categorie_autres')$sql$,
        '23505', 'une opération reçue de la banque n''est enregistrée qu''une fois');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO categorie (nom_categorie, est_categorie_autres, id_utilisateur)
             SELECT 'Autres bis', TRUE, identifiant
             FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'utilisateur'$sql$,
        '23505', 'un utilisateur n''a qu''une seule catégorie « Autres »');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO categorie (nom_categorie, id_utilisateur)
             SELECT 'AUTRES', identifiant
             FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'utilisateur'$sql$,
        '23505', 'deux catégories d''un même utilisateur ne portent pas le même nom, majuscules ignorées');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO categorie (nom_categorie, id_utilisateur)
             SELECT noms.nom_choisi, identifiant
             FROM (VALUES ('Santé'), ('SANTÉ')) AS noms (nom_choisi)
             CROSS JOIN donnees_communes_aux_tests_de_contraintes WHERE nom = 'utilisateur'$sql$,
        '23505', 'une majuscule accentuée compte aussi comme la même lettre');

    -- Sacs, mouvements et suggestions (DD §7 et §8)
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO mouvement_sac (montant_mouvement) VALUES (100)$sql$,
        '23514', 'un mouvement a au moins un sac');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO mouvement_sac (montant_mouvement, id_sac_depart, id_sac_arrivee)
             SELECT 100, identifiant, identifiant
             FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'sac'$sql$,
        '23514', 'un mouvement relie deux sacs différents');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO mouvement_sac (montant_mouvement, id_sac_arrivee)
             SELECT 0, identifiant
             FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'sac'$sql$,
        '23514', 'un mouvement déplace un montant supérieur à 0');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO mouvement_sac (montant_mouvement, id_sac_arrivee, id_suggestion)
             SELECT 100,
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'sac'),
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'suggestion')$sql$,
        '23505', 'une suggestion acceptée crée au plus un mouvement');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO sac (nom_sac, date_objectif, id_utilisateur)
             SELECT 'Voyage', DATE '2030-01-01', identifiant
             FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'utilisateur'$sql$,
        '23514', 'un sac n''a une date d''objectif que s''il a un montant à atteindre');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO suggestion (regle_suggestion, economie_mensuelle, id_utilisateur, id_categorie)
             SELECT 'argent_disponible', 1000,
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'utilisateur'),
                    (SELECT identifiant FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'categorie_autres')$sql$,
        '23514', 'seule la règle « dépassement de catégorie » vise une catégorie');

    -- Support et alertes (DD §9 et §10)
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO demande_support (message_demande) VALUES ('Besoin d''aide')$sql$,
        '23514', 'une demande sans utilisateur a toujours une adresse de contact');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO parametrer (id_utilisateur, code_type_alerte, alerte_activee)
             SELECT identifiant, 'nouvel_appareil', FALSE
             FROM donnees_communes_aux_tests_de_contraintes WHERE nom = 'utilisateur'$sql$,
        'P0001', 'une alerte de sécurité ne peut pas être désactivée');
END;
$$;


-- 2. GR Notif : une alerte désactivable peut être désactivée, mais une alerte de sécurité
--    ne peut pas l'être par une modification (tg_parametrer_controle).
DO $$
DECLARE
    identifiant_de_l_utilisateur_des_alertes uuid;
BEGIN
    INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash)
    VALUES ('test-alertes@exemple.test', 'empreinte-de-test')
    RETURNING id_utilisateur INTO identifiant_de_l_utilisateur_des_alertes;

    INSERT INTO parametrer (id_utilisateur, code_type_alerte, alerte_activee)
    VALUES (identifiant_de_l_utilisateur_des_alertes, 'budget_atteint', FALSE),
           (identifiant_de_l_utilisateur_des_alertes, 'compte_bloque', TRUE);
    RAISE NOTICE 'OK : une alerte désactivable peut être désactivée';

    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        format('UPDATE parametrer SET alerte_activee = FALSE WHERE id_utilisateur = %L AND code_type_alerte = %L',
               identifiant_de_l_utilisateur_des_alertes, 'compte_bloque'),
        'P0001', 'une alerte de sécurité ne peut pas non plus être désactivée par une modification');
END;
$$;


-- 3. GR Sacs : supprimer un sac remet son montant dans l'argent disponible, sans changer le solde
--    des autres sacs (tg_sac_avant_suppression et ON DELETE SET NULL). Si le déclencheur ne
--    supprimait pas les mouvements avec l'argent disponible, la suppression du sac échouerait.
DO $$
DECLARE
    identifiant_de_l_utilisateur_des_sacs uuid;
    identifiant_du_sac_que_le_test_supprime uuid;
    identifiant_du_sac_que_le_test_garde uuid;
    solde_du_sac_garde_avant_la_suppression bigint;
    solde_du_sac_garde_apres_la_suppression bigint;
    mouvements_restants_du_sac_garde_apres_suppression integer;
BEGIN
    INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash)
    VALUES ('test-sacs@exemple.test', 'empreinte-de-test')
    RETURNING id_utilisateur INTO identifiant_de_l_utilisateur_des_sacs;

    INSERT INTO sac (nom_sac, id_utilisateur) VALUES ('Vacances', identifiant_de_l_utilisateur_des_sacs)
    RETURNING id_sac INTO identifiant_du_sac_que_le_test_supprime;
    INSERT INTO sac (nom_sac, id_utilisateur) VALUES ('Épargne', identifiant_de_l_utilisateur_des_sacs)
    RETURNING id_sac INTO identifiant_du_sac_que_le_test_garde;

    -- Argent disponible → Vacances (100), Vacances → argent disponible (30),
    -- Vacances → Épargne (20), Épargne → Vacances (10). Épargne vaut donc 20 − 10 = 10.
    INSERT INTO mouvement_sac (montant_mouvement, id_sac_depart, id_sac_arrivee) VALUES
        (100, NULL, identifiant_du_sac_que_le_test_supprime),
        (30, identifiant_du_sac_que_le_test_supprime, NULL),
        (20, identifiant_du_sac_que_le_test_supprime, identifiant_du_sac_que_le_test_garde),
        (10, identifiant_du_sac_que_le_test_garde, identifiant_du_sac_que_le_test_supprime);

    SELECT sum(CASE WHEN id_sac_arrivee = identifiant_du_sac_que_le_test_garde
                    THEN montant_mouvement ELSE -montant_mouvement END)
    INTO solde_du_sac_garde_avant_la_suppression
    FROM mouvement_sac
    WHERE identifiant_du_sac_que_le_test_garde IN (id_sac_depart, id_sac_arrivee);

    DELETE FROM sac WHERE id_sac = identifiant_du_sac_que_le_test_supprime;

    SELECT sum(CASE WHEN id_sac_arrivee = identifiant_du_sac_que_le_test_garde
                    THEN montant_mouvement ELSE -montant_mouvement END),
           count(*)
    INTO solde_du_sac_garde_apres_la_suppression, mouvements_restants_du_sac_garde_apres_suppression
    FROM mouvement_sac
    WHERE identifiant_du_sac_que_le_test_garde IN (id_sac_depart, id_sac_arrivee);

    ASSERT solde_du_sac_garde_avant_la_suppression = 10, 'Épargne devrait valoir 10 avant la suppression';
    ASSERT solde_du_sac_garde_apres_la_suppression = 10, 'Le solde d''Épargne a changé avec la suppression de Vacances';
    ASSERT mouvements_restants_du_sac_garde_apres_suppression = 2, 'Épargne devrait garder ses 2 mouvements';
    RAISE NOTICE 'OK : supprimer un sac garde le solde des autres sacs';
END;
$$;


-- 4. GR Comptes : déconnecter une banque supprime ses comptes et leurs transactions,
--    mais pas l'utilisateur (ON DELETE CASCADE).
DO $$
DECLARE
    identifiant_de_l_utilisateur_de_la_banque uuid;
    identifiant_de_la_categorie_autres_de_la_banque uuid;
    identifiant_de_la_connexion_que_le_test_supprime uuid;
    identifiant_du_compte_courant_de_la_connexion uuid;
    identifiant_du_livret_de_la_meme_connexion uuid;
    comptes_et_transactions_restants_apres_deconnexion integer;
BEGIN
    INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash)
    VALUES ('test-banque@exemple.test', 'empreinte-de-test')
    RETURNING id_utilisateur INTO identifiant_de_l_utilisateur_de_la_banque;

    INSERT INTO categorie (nom_categorie, est_categorie_autres, id_utilisateur)
    VALUES ('Autres', TRUE, identifiant_de_l_utilisateur_de_la_banque)
    RETURNING id_categorie INTO identifiant_de_la_categorie_autres_de_la_banque;

    INSERT INTO connexion_bancaire (id_connexion_externe, nom_banque, date_expiration_autorisation, id_utilisateur)
    VALUES ('connexion-banque', 'Banque de test', now() + interval '180 days', identifiant_de_l_utilisateur_de_la_banque)
    RETURNING id_connexion INTO identifiant_de_la_connexion_que_le_test_supprime;

    INSERT INTO compte_bancaire (id_compte_externe, nom_compte, solde_compte, date_solde, id_connexion)
    VALUES ('compte-courant', 'Compte courant', 120000, now(), identifiant_de_la_connexion_que_le_test_supprime)
    RETURNING id_compte INTO identifiant_du_compte_courant_de_la_connexion;
    INSERT INTO compte_bancaire (id_compte_externe, nom_compte, solde_compte, date_solde, id_connexion)
    VALUES ('livret', 'Livret', 500000, now(), identifiant_de_la_connexion_que_le_test_supprime)
    RETURNING id_compte INTO identifiant_du_livret_de_la_meme_connexion;

    INSERT INTO transaction (id_transaction_externe, date_transaction, libelle_transaction,
                             montant_transaction, id_compte, id_categorie) VALUES
        ('operation-1', current_date, 'LOYER', -65000,
         identifiant_du_compte_courant_de_la_connexion, identifiant_de_la_categorie_autres_de_la_banque),
        ('operation-2', current_date, 'VIREMENT', 10000,
         identifiant_du_livret_de_la_meme_connexion, identifiant_de_la_categorie_autres_de_la_banque);

    DELETE FROM connexion_bancaire WHERE id_connexion = identifiant_de_la_connexion_que_le_test_supprime;

    SELECT (SELECT count(*) FROM compte_bancaire WHERE id_connexion = identifiant_de_la_connexion_que_le_test_supprime)
         + (SELECT count(*) FROM transaction
            WHERE id_compte IN (identifiant_du_compte_courant_de_la_connexion, identifiant_du_livret_de_la_meme_connexion))
    INTO comptes_et_transactions_restants_apres_deconnexion;

    ASSERT comptes_et_transactions_restants_apres_deconnexion = 0,
        'Des comptes ou des transactions restent après la déconnexion de la banque';
    ASSERT EXISTS (SELECT 1 FROM utilisateur WHERE id_utilisateur = identifiant_de_l_utilisateur_de_la_banque),
        'L''utilisateur a disparu avec sa banque';
    RAISE NOTICE 'OK : déconnecter une banque supprime ses comptes et leurs transactions, pas l''utilisateur';
END;
$$;


-- 5. GR Dépenses : une catégorie ne disparaît qu'après le déplacement de ses transactions dans
--    « Autres » (clé différée). Elle emporte alors ses règles, et la suggestion qui la visait
--    reste, sans catégorie (ON DELETE CASCADE et SET NULL).
DO $$
DECLARE
    identifiant_de_l_utilisateur_des_categories uuid;
    identifiant_de_la_categorie_autres_du_test uuid;
    identifiant_de_la_categorie_loisirs_supprimee uuid;
    identifiant_de_la_connexion_des_categories uuid;
    identifiant_du_compte_des_categories_testees uuid;
    identifiant_de_la_suggestion_sur_les_loisirs uuid;
    la_suppression_a_ete_refusee_par_la_base boolean := FALSE;
BEGIN
    INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash)
    VALUES ('test-categories@exemple.test', 'empreinte-de-test')
    RETURNING id_utilisateur INTO identifiant_de_l_utilisateur_des_categories;

    INSERT INTO categorie (nom_categorie, est_categorie_autres, id_utilisateur)
    VALUES ('Autres', TRUE, identifiant_de_l_utilisateur_des_categories)
    RETURNING id_categorie INTO identifiant_de_la_categorie_autres_du_test;
    INSERT INTO categorie (nom_categorie, id_utilisateur)
    VALUES ('Loisirs', identifiant_de_l_utilisateur_des_categories)
    RETURNING id_categorie INTO identifiant_de_la_categorie_loisirs_supprimee;

    -- « Autres » existe aussi pour l'utilisateur des contraintes : deux utilisateurs peuvent porter le même nom.
    RAISE NOTICE 'OK : deux utilisateurs peuvent avoir une catégorie du même nom';

    -- Une catégorie peut changer les majuscules de son propre nom.
    UPDATE categorie SET nom_categorie = 'LOISIRS' WHERE id_categorie = identifiant_de_la_categorie_loisirs_supprimee;
    RAISE NOTICE 'OK : une catégorie peut changer les majuscules de son propre nom';

    INSERT INTO connexion_bancaire (id_connexion_externe, nom_banque, date_expiration_autorisation, id_utilisateur)
    VALUES ('connexion-categories', 'Banque de test', now() + interval '180 days', identifiant_de_l_utilisateur_des_categories)
    RETURNING id_connexion INTO identifiant_de_la_connexion_des_categories;
    INSERT INTO compte_bancaire (id_compte_externe, nom_compte, solde_compte, date_solde, id_connexion)
    VALUES ('compte-categories', 'Compte courant', 80000, now(), identifiant_de_la_connexion_des_categories)
    RETURNING id_compte INTO identifiant_du_compte_des_categories_testees;

    INSERT INTO transaction (id_transaction_externe, date_transaction, libelle_transaction,
                             montant_transaction, id_compte, id_categorie)
    VALUES ('operation-cinema', current_date, 'CINEMA', -1200,
            identifiant_du_compte_des_categories_testees, identifiant_de_la_categorie_loisirs_supprimee);
    INSERT INTO regle_classement (libelle_regle, id_categorie)
    VALUES ('CINEMA', identifiant_de_la_categorie_loisirs_supprimee);
    INSERT INTO suggestion (regle_suggestion, economie_mensuelle, id_utilisateur, id_categorie)
    VALUES ('depassement_categorie', 1500, identifiant_de_l_utilisateur_des_categories,
            identifiant_de_la_categorie_loisirs_supprimee)
    RETURNING id_suggestion INTO identifiant_de_la_suggestion_sur_les_loisirs;

    -- Tant qu'une transaction est classée dans Loisirs, la catégorie ne peut pas disparaître.
    -- SET CONSTRAINTS ALL IMMEDIATE vérifie tout de suite la clé différée, au lieu d'attendre la fin.
    BEGIN
        DELETE FROM categorie WHERE id_categorie = identifiant_de_la_categorie_loisirs_supprimee;
        SET CONSTRAINTS ALL IMMEDIATE;
    EXCEPTION WHEN foreign_key_violation THEN
        la_suppression_a_ete_refusee_par_la_base := TRUE;
    END;
    SET CONSTRAINTS ALL DEFERRED;
    ASSERT la_suppression_a_ete_refusee_par_la_base, 'Une catégorie qui avait encore des transactions a été supprimée';
    RAISE NOTICE 'OK : une catégorie qui a encore des transactions ne peut pas être supprimée';

    -- Une fois ses transactions déplacées dans « Autres », elle disparaît.
    UPDATE transaction SET id_categorie = identifiant_de_la_categorie_autres_du_test
    WHERE id_categorie = identifiant_de_la_categorie_loisirs_supprimee;
    DELETE FROM categorie WHERE id_categorie = identifiant_de_la_categorie_loisirs_supprimee;
    SET CONSTRAINTS ALL IMMEDIATE;
    SET CONSTRAINTS ALL DEFERRED;

    ASSERT NOT EXISTS (SELECT 1 FROM regle_classement WHERE id_categorie = identifiant_de_la_categorie_loisirs_supprimee),
        'La règle de Loisirs n''a pas disparu avec sa catégorie';
    ASSERT (SELECT id_categorie FROM suggestion WHERE id_suggestion = identifiant_de_la_suggestion_sur_les_loisirs) IS NULL,
        'La suggestion vise encore la catégorie supprimée';
    ASSERT (SELECT count(*) FROM transaction
            WHERE id_compte = identifiant_du_compte_des_categories_testees
              AND id_categorie = identifiant_de_la_categorie_autres_du_test) = 1,
        'La transaction n''est pas dans « Autres »';
    RAISE NOTICE 'OK : après le déplacement de ses transactions, une catégorie disparaît avec ses règles';
END;
$$;


-- 6. GR Sacs : supprimer une suggestion garde le mouvement qu'elle a créé dans l'historique
--    du sac (ON DELETE SET NULL).
DO $$
DECLARE
    identifiant_de_l_utilisateur_des_suggestions uuid;
    identifiant_du_sac_qui_recoit_l_economie uuid;
    identifiant_de_la_suggestion_que_le_test_supprime uuid;
    identifiant_du_mouvement_cree_par_la_suggestion uuid;
BEGIN
    INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash)
    VALUES ('test-suggestions@exemple.test', 'empreinte-de-test')
    RETURNING id_utilisateur INTO identifiant_de_l_utilisateur_des_suggestions;
    INSERT INTO sac (nom_sac, id_utilisateur) VALUES ('Épargne', identifiant_de_l_utilisateur_des_suggestions)
    RETURNING id_sac INTO identifiant_du_sac_qui_recoit_l_economie;
    INSERT INTO suggestion (regle_suggestion, economie_mensuelle, statut_suggestion, id_utilisateur)
    VALUES ('epargne_insuffisante', 2000, 'acceptee', identifiant_de_l_utilisateur_des_suggestions)
    RETURNING id_suggestion INTO identifiant_de_la_suggestion_que_le_test_supprime;
    INSERT INTO mouvement_sac (montant_mouvement, id_sac_arrivee, id_suggestion)
    VALUES (2000, identifiant_du_sac_qui_recoit_l_economie, identifiant_de_la_suggestion_que_le_test_supprime)
    RETURNING id_mouvement INTO identifiant_du_mouvement_cree_par_la_suggestion;

    DELETE FROM suggestion WHERE id_suggestion = identifiant_de_la_suggestion_que_le_test_supprime;

    ASSERT EXISTS (SELECT 1 FROM mouvement_sac
                   WHERE id_mouvement = identifiant_du_mouvement_cree_par_la_suggestion AND id_suggestion IS NULL),
        'Le mouvement a disparu, ou vise encore la suggestion supprimée';
    RAISE NOTICE 'OK : supprimer une suggestion garde son mouvement dans l''historique du sac';
END;
$$;


-- 7. GR Admin : un visiteur peut écrire au support avec une adresse de contact, et les réponses
--    disparaissent avec leur demande (ON DELETE CASCADE).
DO $$
DECLARE
    identifiant_de_la_demande_que_le_test_supprime uuid;
BEGIN
    INSERT INTO demande_support (message_demande, email_contact)
    VALUES ('Mon compte est bloqué', 'visiteur@exemple.test')
    RETURNING id_demande INTO identifiant_de_la_demande_que_le_test_supprime;
    INSERT INTO reponse_support (contenu_reponse, id_demande) VALUES
        ('Voici comment le débloquer', identifiant_de_la_demande_que_le_test_supprime),
        ('Est-ce résolu ?', identifiant_de_la_demande_que_le_test_supprime);

    DELETE FROM demande_support WHERE id_demande = identifiant_de_la_demande_que_le_test_supprime;

    ASSERT NOT EXISTS (SELECT 1 FROM reponse_support WHERE id_demande = identifiant_de_la_demande_que_le_test_supprime),
        'Des réponses restent sans leur demande';
    RAISE NOTICE 'OK : un visiteur peut écrire au support, et les réponses disparaissent avec la demande';
END;
$$;


-- 8. GR Suppression et SS §7 : effacer un utilisateur efface toutes ses données. Ses événements
--    restent dans le journal, sans identifiant ni adresse IP (tg_utilisateur_avant_suppression).
DO $$
DECLARE
    identifiant_de_l_utilisateur_a_effacer uuid;
    identifiant_de_la_categorie_autres_effacee uuid;
    identifiant_de_la_connexion_bancaire_effacee uuid;
    identifiant_du_compte_bancaire_efface_avec_lui uuid;
    identifiant_du_premier_sac_de_l_utilisateur uuid;
    identifiant_du_second_sac_de_l_utilisateur uuid;
    identifiant_de_la_demande_de_l_utilisateur uuid;
    identifiants_des_evenements_du_journal_de_securite uuid[];
    nombre_de_mouvements_avant_les_insertions_du_test integer;
    nombre_de_lignes_restantes_apres_l_effacement integer;
BEGIN
    SELECT count(*) INTO nombre_de_mouvements_avant_les_insertions_du_test FROM mouvement_sac;

    INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash, nom_utilisateur, prenom_utilisateur)
    VALUES ('test-effacement@exemple.test', 'empreinte-de-test', 'Martin', 'Alex')
    RETURNING id_utilisateur INTO identifiant_de_l_utilisateur_a_effacer;

    INSERT INTO categorie (nom_categorie, est_categorie_autres, id_utilisateur)
    VALUES ('Autres', TRUE, identifiant_de_l_utilisateur_a_effacer)
    RETURNING id_categorie INTO identifiant_de_la_categorie_autres_effacee;
    INSERT INTO regle_classement (libelle_regle, id_categorie)
    VALUES ('BOULANGERIE', identifiant_de_la_categorie_autres_effacee);

    INSERT INTO connexion_bancaire (id_connexion_externe, nom_banque, date_expiration_autorisation, id_utilisateur)
    VALUES ('connexion-effacement', 'Banque de test', now() + interval '180 days', identifiant_de_l_utilisateur_a_effacer)
    RETURNING id_connexion INTO identifiant_de_la_connexion_bancaire_effacee;
    INSERT INTO compte_bancaire (id_compte_externe, nom_compte, solde_compte, date_solde, id_connexion)
    VALUES ('compte-effacement', 'Compte courant', 90000, now(), identifiant_de_la_connexion_bancaire_effacee)
    RETURNING id_compte INTO identifiant_du_compte_bancaire_efface_avec_lui;
    INSERT INTO transaction (id_transaction_externe, date_transaction, libelle_transaction,
                             montant_transaction, id_compte, id_categorie)
    VALUES ('operation-effacement', current_date, 'BOULANGERIE', -350,
            identifiant_du_compte_bancaire_efface_avec_lui, identifiant_de_la_categorie_autres_effacee);

    -- Deux sacs reliés par un mouvement : sans le déclencheur, effacer les sacs viderait les deux
    -- côtés de ce mouvement, ce que la contrainte « au moins un sac » refuse.
    INSERT INTO sac (nom_sac, id_utilisateur) VALUES ('Vacances', identifiant_de_l_utilisateur_a_effacer)
    RETURNING id_sac INTO identifiant_du_premier_sac_de_l_utilisateur;
    INSERT INTO sac (nom_sac, id_utilisateur) VALUES ('Épargne', identifiant_de_l_utilisateur_a_effacer)
    RETURNING id_sac INTO identifiant_du_second_sac_de_l_utilisateur;
    INSERT INTO mouvement_sac (montant_mouvement, id_sac_depart, id_sac_arrivee) VALUES
        (500, NULL, identifiant_du_premier_sac_de_l_utilisateur),
        (200, identifiant_du_premier_sac_de_l_utilisateur, identifiant_du_second_sac_de_l_utilisateur);

    INSERT INTO suggestion (regle_suggestion, economie_mensuelle, id_utilisateur)
    VALUES ('argent_disponible', 3000, identifiant_de_l_utilisateur_a_effacer);
    INSERT INTO parametrer (id_utilisateur, code_type_alerte, alerte_activee)
    VALUES (identifiant_de_l_utilisateur_a_effacer, 'budget_atteint', FALSE);
    INSERT INTO demande_support (message_demande, id_utilisateur)
    VALUES ('Question sur mes sacs', identifiant_de_l_utilisateur_a_effacer)
    RETURNING id_demande INTO identifiant_de_la_demande_de_l_utilisateur;
    INSERT INTO reponse_support (contenu_reponse, id_demande)
    VALUES ('Voici la réponse', identifiant_de_la_demande_de_l_utilisateur);
    INSERT INTO jeton (type_jeton, empreinte_jeton, date_expiration_jeton, id_utilisateur)
    VALUES ('recuperation_mdp', repeat('a', 64), now() + interval '30 minutes', identifiant_de_l_utilisateur_a_effacer);
    INSERT INTO appareil (jeton_appareil, id_utilisateur)
    VALUES ('appareil-effacement', identifiant_de_l_utilisateur_a_effacer);
    INSERT INTO session (id_session, id_utilisateur)
    VALUES ('session-effacement', identifiant_de_l_utilisateur_a_effacer);
    INSERT INTO code_secours (empreinte_code_secours, id_utilisateur)
    VALUES ('empreinte-code-de-secours', identifiant_de_l_utilisateur_a_effacer);

    WITH evenements_ajoutes_au_journal AS (
        INSERT INTO evenement_securite (type_evenement, resultat_evenement, adresse_ip_evenement, id_utilisateur)
        VALUES ('connexion', 'succes', '192.0.2.10', identifiant_de_l_utilisateur_a_effacer),
               ('changement_mdp', 'succes', '192.0.2.10', identifiant_de_l_utilisateur_a_effacer)
        RETURNING id_evenement)
    SELECT array_agg(id_evenement) INTO identifiants_des_evenements_du_journal_de_securite
    FROM evenements_ajoutes_au_journal;

    DELETE FROM utilisateur WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer;
    -- La clé différée transaction.id_categorie est vérifiée tout de suite, au lieu d'attendre la fin.
    SET CONSTRAINTS ALL IMMEDIATE;
    SET CONSTRAINTS ALL DEFERRED;

    SELECT (SELECT count(*) FROM categorie WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
         + (SELECT count(*) FROM regle_classement WHERE id_categorie = identifiant_de_la_categorie_autres_effacee)
         + (SELECT count(*) FROM connexion_bancaire WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
         + (SELECT count(*) FROM compte_bancaire WHERE id_connexion = identifiant_de_la_connexion_bancaire_effacee)
         + (SELECT count(*) FROM transaction WHERE id_compte = identifiant_du_compte_bancaire_efface_avec_lui)
         + (SELECT count(*) FROM sac WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
         + (SELECT count(*) FROM suggestion WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
         + (SELECT count(*) FROM parametrer WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
         + (SELECT count(*) FROM demande_support WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
         + (SELECT count(*) FROM reponse_support WHERE id_demande = identifiant_de_la_demande_de_l_utilisateur)
         + (SELECT count(*) FROM jeton WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
         + (SELECT count(*) FROM appareil WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
         + (SELECT count(*) FROM session WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
         + (SELECT count(*) FROM code_secours WHERE id_utilisateur = identifiant_de_l_utilisateur_a_effacer)
    INTO nombre_de_lignes_restantes_apres_l_effacement;

    ASSERT nombre_de_lignes_restantes_apres_l_effacement = 0,
        'Des données de l''utilisateur restent après son effacement';
    ASSERT (SELECT count(*) FROM mouvement_sac) = nombre_de_mouvements_avant_les_insertions_du_test,
        'Des mouvements de ses sacs restent après son effacement';
    ASSERT (SELECT count(*) FROM evenement_securite
            WHERE id_evenement = ANY (identifiants_des_evenements_du_journal_de_securite)
              AND id_utilisateur IS NULL AND adresse_ip_evenement IS NULL) = 2,
        'Les événements du journal devraient rester, sans identifiant ni adresse IP';
    RAISE NOTICE 'OK : effacer un utilisateur efface ses données, et son journal reste sans identifiant ni adresse IP';
END;
$$;


-- 9. SS §11 et DD : le journal accepte chaque type d'événement du dictionnaire de données,
-- et refuse tout autre type.
DO $$
BEGIN
    INSERT INTO evenement_securite (type_evenement, resultat_evenement)
    VALUES ('connexion', 'succes'), ('blocage', 'succes'), ('deblocage', 'succes'),
           ('recuperation_mdp', 'succes'), ('changement_mdp', 'succes'), ('changement_email', 'succes'),
           ('activation_double_auth', 'succes'), ('desactivation_double_auth', 'succes'),
           ('connexion_banque', 'succes'), ('deconnexion_banque', 'succes'), ('export_donnees', 'succes'),
           ('demande_suppression', 'succes'), ('action_admin', 'succes'), ('changement_role', 'succes'),
           ('acces_refuse', 'succes');
    RAISE NOTICE 'OK : le journal accepte les 15 types d''événement du dictionnaire de données';

    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$INSERT INTO evenement_securite (type_evenement, resultat_evenement)
             VALUES ('type_inconnu', 'succes')$sql$,
        '23514', 'le journal refuse un type d''événement inconnu');
END;
$$;


-- 10. SS §8, §11 et §14 : l'application ne peut ni donner le rôle administrateur, ni modifier
--     ou effacer le journal de sécurité. Ces tests tournent avec l'utilisateur de l'application.
DO $$
BEGIN
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$UPDATE utilisateur SET role_utilisateur = 'administrateur'
             WHERE email_utilisateur = 'test-contraintes@exemple.test'$sql$,
        '42501', 'l''application ne peut pas donner le rôle administrateur');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$UPDATE evenement_securite SET resultat_evenement = 'succes'$sql$,
        '42501', 'l''application ne peut pas modifier le journal de sécurité');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$DELETE FROM evenement_securite$sql$,
        '42501', 'l''application ne peut pas effacer le journal de sécurité');
    PERFORM pg_temp.verifier_que_la_base_refuse_cette_instruction(
        $sql$TRUNCATE evenement_securite$sql$,
        '42501', 'l''application ne peut pas vider le journal de sécurité');

    -- L'application garde les droits dont elle a besoin sur les autres colonnes du compte.
    UPDATE utilisateur SET nb_tentatives_echouees = 1 WHERE email_utilisateur = 'test-contraintes@exemple.test';
    RAISE NOTICE 'OK : l''application peut encore modifier les compteurs d''un compte';
END;
$$;


\echo
\echo 'Tous les tests sont passés. La transaction est annulée : la base reste vide.'
ROLLBACK;
