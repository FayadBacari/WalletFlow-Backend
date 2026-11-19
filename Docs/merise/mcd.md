# MCD (Modèle conceptuel de données)


| UTILISATEUR              |
|--------------------------|
| **id_utilisateur**       |
| email_utilisateur        |
| mot_de_passe_hash        |
| nom_utilisateur          |
| prenom_utilisateur       |
| role_utilisateur         |
| email_confirme           |
| double_auth_activee      |
| secret_totp_chiffre      |
| canal_notification       |
| nb_tentatives_echouees   |
| nb_blocages              |
| date_fin_blocage         |
| date_demande_suppression |
| dernier_mois_suggestions |
| dernier_pas_totp         |

| CODE_SECOURS                  |
|-------------------------------|
| **id_code_secours**           |
| empreinte_code_secours        |
| date_utilisation_code_secours |

| JETON                  |
|------------------------|
| **id_jeton**           |
| type_jeton             |
| empreinte_jeton        |
| date_expiration_jeton  |
| date_utilisation_jeton |
| nouvel_email           |
| nb_essais_jeton        |

| APPAREIL                         |
|----------------------------------|
| **id_appareil**                  |
| jeton_appareil                   |
| jeton_push                       |
| date_derniere_connexion_appareil |

| SESSION                |
|------------------------|
| **id_session**         |
| date_derniere_activite |
| date_creation_session  |

| EVENEMENT_SECURITE   |
|----------------------|
| **id_evenement**     |
| type_evenement       |
| resultat_evenement   |
| date_evenement       |
| adresse_ip_evenement |

| CONNEXION_BANCAIRE           |
|------------------------------|
| **id_connexion**             |
| id_connexion_externe         |
| nom_banque                   |
| statut_connexion             |
| date_expiration_autorisation |

| COMPTE_BANCAIRE   |
|-------------------|
| **id_compte**     |
| id_compte_externe |
| nom_compte        |
| solde_compte      |
| date_solde        |

| TRANSACTION            |
|------------------------|
| **id_transaction**     |
| id_transaction_externe |
| date_transaction       |
| libelle_transaction    |
| montant_transaction    |
| est_transfert_interne  |

| CATEGORIE            |
|----------------------|
| **id_categorie**     |
| nom_categorie        |
| budget_mensuel       |
| est_categorie_autres |

| REGLE_CLASSEMENT        |
|-------------------------|
| **id_regle_classement** |
| libelle_regle           |

| SAC              |
|------------------|
| **id_sac**       |
| nom_sac          |
| montant_objectif |
| date_objectif    |

| MOUVEMENT_SAC     |
|-------------------|
| **id_mouvement**  |
| montant_mouvement |
| date_mouvement    |

| SUGGESTION             |
|------------------------|
| **id_suggestion**      |
| regle_suggestion       |
| mois_suggestion        |
| economie_mensuelle     |
| statut_suggestion      |
| date_statut_suggestion |

| TYPE_ALERTE          |
|----------------------|
| **code_type_alerte** |
| libelle_type_alerte  |
| est_desactivable     |

| ALERTE        |
|---------------|
| **id_alerte** |
| date_alerte   |
| mois_alerte   |
| seuil_alerte  |

| DEMANDE_EMAIL         |
|-----------------------|
| **id_demande_email**  |
| type_demande_email    |
| adresse_email_demande |
| adresse_ip_demande    |
| date_demande_email    |
| nb_essais_envoi       |
| date_prochain_essai   |

| DEMANDE_SUPPORT |
|-----------------|
| **id_demande**  |
| message_demande |
| email_contact   |
| statut_demande  |
| date_demande    |

| REPONSE_SUPPORT |
|-----------------|
| **id_reponse**  |
| contenu_reponse |
| date_reponse    |

| OBTENIR      | Card. |
|--------------|-------|
| UTILISATEUR  | 0,n   |
| CODE_SECOURS | 1,1   |

| DEMANDER    | Card. |
|-------------|-------|
| UTILISATEUR | 0,n   |
| JETON       | 1,1   |

| UTILISER    | Card. |
|-------------|-------|
| UTILISATEUR | 0,n   |
| APPAREIL    | 1,1   |

| OUVRIR      | Card. |
|-------------|-------|
| UTILISATEUR | 0,n   |
| SESSION     | 1,1   |

| TRACER             | Card. |
|--------------------|-------|
| UTILISATEUR        | 0,n   |
| EVENEMENT_SECURITE | 0,1   |

| CONCERNER          | Card. |
|--------------------|-------|
| UTILISATEUR        | 0,n   |
| EVENEMENT_SECURITE | 0,1   |

| AUTORISER          | Card. |
|--------------------|-------|
| UTILISATEUR        | 0,n   |
| CONNEXION_BANCAIRE | 1,1   |

| FOURNIR            | Card. |
|--------------------|-------|
| CONNEXION_BANCAIRE | 0,n   |
| COMPTE_BANCAIRE    | 1,1   |

| CONTENIR        | Card. |
|-----------------|-------|
| COMPTE_BANCAIRE | 0,n   |
| TRANSACTION     | 1,1   |

| DISPOSER    | Card. |
|-------------|-------|
| UTILISATEUR | 1,n   |
| CATEGORIE   | 1,1   |

| CLASSER     | Card. |
|-------------|-------|
| CATEGORIE   | 0,n   |
| TRANSACTION | 1,1   |

| VISER            | Card. |
|------------------|-------|
| CATEGORIE        | 0,n   |
| REGLE_CLASSEMENT | 1,1   |

| DETENIR     | Card. |
|-------------|-------|
| UTILISATEUR | 0,n   |
| SAC         | 1,1   |

| DEBITER       | Card. |
|---------------|-------|
| SAC           | 0,n   |
| MOUVEMENT_SAC | 0,1   |

| CREDITER      | Card. |
|---------------|-------|
| SAC           | 0,n   |
| MOUVEMENT_SAC | 0,1   |

| RECEVOIR    | Card. |
|-------------|-------|
| UTILISATEUR | 0,n   |
| SUGGESTION  | 1,1   |

| PORTER_SUR | Card. |
|------------|-------|
| CATEGORIE  | 0,n   |
| SUGGESTION | 0,1   |

| GENERER       | Card. |
|---------------|-------|
| SUGGESTION    | 0,1   |
| MOUVEMENT_SAC | 0,1   |

| PARAMETRER     | Card.     |
|----------------|-----------|
| UTILISATEUR    | 0,n       |
| TYPE_ALERTE    | 0,n       |
| alerte_activee | propriété |

| PREVENIR    | Card. |
|-------------|-------|
| UTILISATEUR | 0,n   |
| ALERTE      | 1,1   |

| CORRESPONDRE | Card. |
|--------------|-------|
| TYPE_ALERTE  | 0,n   |
| ALERTE       | 1,1   |

| SURVEILLER | Card. |
|------------|-------|
| CATEGORIE  | 0,n   |
| ALERTE     | 0,1   |

| ATTEINDRE | Card. |
|-----------|-------|
| SAC       | 0,1   |
| ALERTE    | 0,1   |

| RAPPELER           | Card. |
|--------------------|-------|
| CONNEXION_BANCAIRE | 0,1   |
| ALERTE             | 0,1   |

| ATTENDRE      | Card. |
|---------------|-------|
| UTILISATEUR   | 0,n   |
| DEMANDE_EMAIL | 0,1   |

| ANNONCER      | Card. |
|---------------|-------|
| ALERTE        | 0,1   |
| DEMANDE_EMAIL | 0,1   |

| TRANSMETTRE     | Card. |
|-----------------|-------|
| REPONSE_SUPPORT | 0,1   |
| DEMANDE_EMAIL   | 0,1   |

| EMETTRE         | Card. |
|-----------------|-------|
| UTILISATEUR     | 0,n   |
| DEMANDE_SUPPORT | 0,1   |

| REPONDRE        | Card. |
|-----------------|-------|
| DEMANDE_SUPPORT | 0,n   |
| REPONSE_SUPPORT | 1,1   |
