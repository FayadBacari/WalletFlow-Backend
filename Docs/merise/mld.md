# MLD (Modèle logique de données)


| UTILISATEUR              | Clé    |
|--------------------------|--------|
| **id_utilisateur**       | PK     |
| email_utilisateur        | Unique |
| mot_de_passe_hash        |        |
| nom_utilisateur          |        |
| prenom_utilisateur       |        |
| role_utilisateur         |        |
| email_confirme           |        |
| double_auth_activee      |        |
| secret_totp_chiffre      |        |
| canal_notification       |        |
| nb_tentatives_echouees   |        |
| nb_blocages              |        |
| date_fin_blocage         |        |
| date_demande_suppression |        |

| CODE_SECOURS                  | Clé              |
|-------------------------------|------------------|
| **id_code_secours**           | PK               |
| empreinte_code_secours        |                  |
| date_utilisation_code_secours |                  |
| #id_utilisateur               | FK → UTILISATEUR |

| JETON                  | Clé              |
|------------------------|------------------|
| **id_jeton**           | PK               |
| type_jeton             |                  |
| empreinte_jeton        | Unique           |
| date_expiration_jeton  |                  |
| date_utilisation_jeton |                  |
| nouvel_email           |                  |
| #id_utilisateur        | FK → UTILISATEUR |

| APPAREIL        | Clé              |
|-----------------|------------------|
| **id_appareil** | PK               |
| jeton_appareil  | Unique           |
| jeton_push      |                  |
| #id_utilisateur | FK → UTILISATEUR |

| SESSION                | Clé              |
|------------------------|------------------|
| **id_session**         | PK               |
| date_derniere_activite |                  |
| #id_utilisateur        | FK → UTILISATEUR |

| EVENEMENT_SECURITE   | Clé                           |
|----------------------|-------------------------------|
| **id_evenement**     | PK                            |
| type_evenement       |                               |
| resultat_evenement   |                               |
| date_evenement       |                               |
| adresse_ip_evenement |                               |
| #id_utilisateur      | FK → UTILISATEUR, facultative |

| CONNEXION_BANCAIRE           | Clé              |
|------------------------------|------------------|
| **id_connexion**             | PK               |
| id_connexion_externe         | Unique           |
| nom_banque                   |                  |
| statut_connexion             |                  |
| date_expiration_autorisation |                  |
| #id_utilisateur              | FK → UTILISATEUR |

| COMPTE_BANCAIRE   | Clé                       |
|-------------------|---------------------------|
| **id_compte**     | PK                        |
| id_compte_externe | Unique avec #id_connexion |
| nom_compte        |                           |
| solde_compte      |                           |
| date_solde        |                           |
| #id_connexion     | FK → CONNEXION_BANCAIRE   |

| CATEGORIE            | Clé                                 |
|----------------------|-------------------------------------|
| **id_categorie**     | PK                                  |
| nom_categorie        |                                     |
| budget_mensuel       |                                     |
| est_categorie_autres | Vrai une seule fois par utilisateur |
| #id_utilisateur      | FK → UTILISATEUR                    |

| TRANSACTION            | Clé                    |
|------------------------|------------------------|
| **id_transaction**     | PK                     |
| id_transaction_externe | Unique avec #id_compte |
| date_transaction       |                        |
| libelle_transaction    |                        |
| montant_transaction    |                        |
| est_transfert_interne  |                        |
| #id_compte             | FK → COMPTE_BANCAIRE   |
| #id_categorie          | FK → CATEGORIE         |

| REGLE_CLASSEMENT        | Clé            |
|-------------------------|----------------|
| **id_regle_classement** | PK             |
| libelle_regle           |                |
| #id_categorie           | FK → CATEGORIE |

| SAC              | Clé              |
|------------------|------------------|
| **id_sac**       | PK               |
| nom_sac          |                  |
| montant_objectif |                  |
| date_objectif    |                  |
| #id_utilisateur  | FK → UTILISATEUR |

| SUGGESTION             | Clé                         |
|------------------------|-----------------------------|
| **id_suggestion**      | PK                          |
| regle_suggestion       |                             |
| economie_mensuelle     |                             |
| statut_suggestion      |                             |
| date_statut_suggestion |                             |
| #id_utilisateur        | FK → UTILISATEUR            |
| #id_categorie          | FK → CATEGORIE, facultative |

| MOUVEMENT_SAC     | Clé                                  |
|-------------------|--------------------------------------|
| **id_mouvement**  | PK                                   |
| montant_mouvement |                                      |
| date_mouvement    |                                      |
| #id_sac_depart    | FK → SAC, facultative                |
| #id_sac_arrivee   | FK → SAC, facultative                |
| #id_suggestion    | FK → SUGGESTION, facultative, unique |

| TYPE_ALERTE          | Clé |
|----------------------|-----|
| **code_type_alerte** | PK  |
| libelle_type_alerte  |     |
| est_desactivable     |     |

| PARAMETRER            | Clé                  |
|-----------------------|----------------------|
| **#id_utilisateur**   | PK, FK → UTILISATEUR |
| **#code_type_alerte** | PK, FK → TYPE_ALERTE |
| alerte_activee        |                      |

| DEMANDE_SUPPORT | Clé                           |
|-----------------|-------------------------------|
| **id_demande**  | PK                            |
| message_demande |                               |
| email_contact   |                               |
| statut_demande  |                               |
| #id_utilisateur | FK → UTILISATEUR, facultative |

| REPONSE_SUPPORT | Clé                  |
|-----------------|----------------------|
| **id_reponse**  | PK                   |
| contenu_reponse |                      |
| #id_demande     | FK → DEMANDE_SUPPORT |

| Relation           | Clé étrangère    | Référence          | Obligatoire  | Association |
|--------------------|------------------|--------------------|--------------|-------------|
| CODE_SECOURS       | id_utilisateur   | UTILISATEUR        | Oui          | OBTENIR     |
| JETON              | id_utilisateur   | UTILISATEUR        | Oui          | DEMANDER    |
| APPAREIL           | id_utilisateur   | UTILISATEUR        | Oui          | UTILISER    |
| SESSION            | id_utilisateur   | UTILISATEUR        | Oui          | OUVRIR      |
| EVENEMENT_SECURITE | id_utilisateur   | UTILISATEUR        | Non          | TRACER      |
| CONNEXION_BANCAIRE | id_utilisateur   | UTILISATEUR        | Oui          | AUTORISER   |
| COMPTE_BANCAIRE    | id_connexion     | CONNEXION_BANCAIRE | Oui          | FOURNIR     |
| CATEGORIE          | id_utilisateur   | UTILISATEUR        | Oui          | DISPOSER    |
| TRANSACTION        | id_compte        | COMPTE_BANCAIRE    | Oui          | CONTENIR    |
| TRANSACTION        | id_categorie     | CATEGORIE          | Oui          | CLASSER     |
| REGLE_CLASSEMENT   | id_categorie     | CATEGORIE          | Oui          | VISER       |
| SAC                | id_utilisateur   | UTILISATEUR        | Oui          | DETENIR     |
| SUGGESTION         | id_utilisateur   | UTILISATEUR        | Oui          | RECEVOIR    |
| SUGGESTION         | id_categorie     | CATEGORIE          | Non          | PORTER_SUR  |
| MOUVEMENT_SAC      | id_sac_depart    | SAC                | Non          | DEBITER     |
| MOUVEMENT_SAC      | id_sac_arrivee   | SAC                | Non          | CREDITER    |
| MOUVEMENT_SAC      | id_suggestion    | SUGGESTION         | Non (unique) | GENERER     |
| PARAMETRER         | id_utilisateur   | UTILISATEUR        | Oui          | PARAMETRER  |
| PARAMETRER         | code_type_alerte | TYPE_ALERTE        | Oui          | PARAMETRER  |
| DEMANDE_SUPPORT    | id_utilisateur   | UTILISATEUR        | Non          | EMETTRE     |
| REPONSE_SUPPORT    | id_demande       | DEMANDE_SUPPORT    | Oui          | REPONDRE    |
