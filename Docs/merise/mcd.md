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

| APPAREIL        |
|-----------------|
| **id_appareil** |
| jeton_appareil  |
| jeton_push      |

| SESSION                |
|------------------------|
| **id_session**         |
| date_derniere_activite |

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
| economie_mensuelle     |
| statut_suggestion      |
| date_statut_suggestion |

| TYPE_ALERTE          |
|----------------------|
| **code_type_alerte** |
| libelle_type_alerte  |
| est_desactivable     |

| DEMANDE_SUPPORT |
|-----------------|
| **id_demande**  |
| message_demande |
| email_contact   |
| statut_demande  |

| REPONSE_SUPPORT |
|-----------------|
| **id_reponse**  |
| contenu_reponse |

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

| EMETTRE         | Card. |
|-----------------|-------|
| UTILISATEUR     | 0,n   |
| DEMANDE_SUPPORT | 0,1   |

| REPONDRE        | Card. |
|-----------------|-------|
| DEMANDE_SUPPORT | 0,n   |
| REPONSE_SUPPORT | 1,1   |
