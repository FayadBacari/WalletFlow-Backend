# Les règles de gestion


## Cadre général

- Rôles :
  - **Visiteur** : personne non connectée.
  - **Utilisateur** : personne inscrite et connectée.
  - **Administrateur** : gère le support et les comptes bloqués.


## Interface et Authentification

- Le visiteur peut se rendre sur la page d'accueil de l'application.
- Le visiteur peut s'inscrire sur la plateforme de l'application.
- Le visiteur peut se connecter à l'application.
- Le visiteur peut demander à retrouver son mot de passe s'il le perd.
- L'utilisateur peut se déconnecter.
- L'utilisateur peut activer la double authentification avec une application d'authentification. Il reçoit alors 10 codes de secours.
- Pour activer la double authentification, l'utilisateur saisit son mot de passe, puis un premier code de son application. Elle n'est active qu'après ce code.
- Si la double authentification est activée, la connexion demande aussi un code de l'application ou un code de secours. Une connexion n'est réussie qu'après ce code.
- Avec la double authentification, au 5e essai, le compte se bloque, même avec le bon mot de passe. Le bon code de double authentification permet encore d'entrer, et lève le blocage.
- L'utilisateur peut désactiver la double authentification avec son mot de passe et un code de l'application ou un code de secours. En la réactivant, il reçoit 10 nouveaux codes de secours.
- L'utilisateur dispose de 5 tentatives de connexion avant que son compte soit bloqué.
- Le blocage est temporaire : 15 minutes la première fois, puis la durée double à chaque nouveau blocage, sans dépasser 24 heures.
- Le compteur de blocages repart à zéro après une connexion réussie.
- L'utilisateur reçoit un e-mail avec un lien pour débloquer son compte sans attendre. Ce lien remet le compteur de tentatives à zéro, mais ne connecte pas l'utilisateur.
- Le compte n'est actif qu'après confirmation de l'adresse e-mail, par un lien envoyé à l'inscription.
- Pour confirmer son adresse, le visiteur ouvre le lien reçu par e-mail et saisit le mot de passe choisi à l'inscription.
- Tant que l'adresse n'est pas confirmée, une nouvelle inscription remplace le mot de passe et envoie un nouveau lien. L'ancien lien ne fonctionne plus.
- Si l'e-mail d'inscription ne peut pas partir, le visiteur est invité à réessayer plus tard.
- Un compte non confirmé est effacé une fois son lien expiré.
- Le mot de passe contient de 12 à 128 caractères, dont une majuscule, une minuscule, un chiffre et un caractère spécial.
- La récupération du mot de passe se fait par un lien à usage unique, valable 30 minutes. Le mot de passe n'est jamais envoyé par e-mail.
- Une récupération réussie débloque aussi le compte.
- Si l'adresse n'est pas encore confirmée, l'e-mail de récupération invite à s'inscrire à nouveau.
- Après une récupération, l'utilisateur se connecte avec son nouveau mot de passe.
- Une récupération du mot de passe ne désactive pas la double authentification.
- En cas d'échec de connexion, le message est le même, que l'adresse e-mail existe ou non (« Identifiants incorrects »), pour ne pas révéler quels comptes existent.
- Si le mot de passe est juste mais que l'adresse n'est pas confirmée, l'application invite à la confirmer.
- Les pages « mot de passe oublié » et « inscription » affichent aussi le même message, que l'adresse existe ou non.
- La session se ferme automatiquement après 30 minutes d'inactivité.


## Comptes Bancaires

- L'utilisateur peut consulter tous ses comptes bancaires.
- L'utilisateur connecte sa banque à WalletFlow par l'intermédiaire d'un prestataire agréé. La connexion est en lecture seule : WalletFlow ne peut faire ni virement ni paiement.
- L'identification se fait sur la page de la banque : WalletFlow ne voit jamais les identifiants bancaires.
- Les comptes, les soldes et les transactions viennent de la banque. Dans WalletFlow, l'utilisateur peut seulement changer la catégorie d'une transaction ou la marquer comme transfert.
- Seuls les comptes en euros sont ajoutés à WalletFlow.
- Le solde affiché est le solde comptable donné par la banque, ou le plus proche si elle ne le donne pas.
- Le solde est mis à jour à chaque ouverture de l'application, et jusqu'à 4 fois par jour en arrière-plan. L'application affiche la date et l'heure de la dernière mise à jour.
- Si la banque ne répond pas, l'application affiche le dernier solde connu, avec sa date.
- L'autorisation donnée à la banque est à renouveler au plus tard tous les 180 jours, plus tôt si la banque n'accepte pas une durée aussi longue. L'application prévient l'utilisateur 7 jours avant.
- L'utilisateur peut déconnecter une banque : les comptes et les transactions de cette banque sont supprimés de WalletFlow, et l'autorisation est révoquée. L'application prévient l'utilisateur et lui demande de confirmer.
- Les transferts entre deux comptes de l'utilisateur ne sont comptés ni comme des dépenses, ni comme des revenus.
- L'utilisateur peut marquer une transaction comme transfert entre deux de ses comptes.
- L'utilisateur peut consulter son solde total, tous comptes confondus.
- L'utilisateur peut rechercher une transaction par libellé, montant ou date.


## Sacs et Épargne

- Un sac est une enveloppe virtuelle : l'argent ne quitte pas le compte bancaire, il est seulement réparti dans l'application.
- L'utilisateur peut créer des « sacs ».
- L'utilisateur peut renommer ses sacs.
- L'utilisateur peut supprimer ses sacs.
- L'utilisateur peut déplacer de l'argent disponible vers un sac, d'un sac à un autre, ou d'un sac vers l'argent disponible.
- L'utilisateur peut donner un objectif à un sac : un montant à atteindre et, s'il le souhaite, une date.
- L'utilisateur peut voir la progression de chaque sac vers son objectif.
- L'utilisateur peut voir l'historique des mouvements d'un sac.
- L'argent qui n'est dans aucun sac est affiché comme « argent disponible ».
- Un déplacement d'argent ne peut pas porter le total des sacs au-dessus du solde total.
- Si le solde total passe sous le total des sacs, l'application prévient l'utilisateur et lui demande de retirer de l'argent d'un ou plusieurs sacs.
- Un déplacement d'argent ne peut pas rendre un sac négatif.
- Supprimer un sac remet son montant dans l'argent disponible.
- L'utilisateur peut voir des suggestions d'épargne adaptées à ses dépenses.
- L'utilisateur peut accepter une suggestion : l'application lui propose alors de placer le montant économisé dans un sac (par exemple « Épargne »).
- L'utilisateur peut ignorer une suggestion.
- L'utilisateur peut voir combien il a économisé, c'est-à-dire l'argent placé dans ses sacs grâce aux suggestions acceptées.
- Les suggestions reposent sur des règles fixes. Chaque suggestion indique pourquoi elle apparaît et combien elle permet d'économiser par mois.
- Une suggestion apparaît quand une catégorie dépasse de plus de 20 % sa moyenne des 3 derniers mois.
- Une suggestion apparaît quand de l'argent disponible reste en fin de mois sans être placé dans un sac.
- Une suggestion apparaît quand l'épargne du mois (revenus moins dépenses) est inférieure à 20 % des revenus.
- Les suggestions basées sur des moyennes n'apparaissent qu'à partir de 3 mois d'historique.
- Une suggestion ignorée ne réapparaît pas pendant 30 jours.
- L'application ne recommande aucun produit financier (livret, placement, crédit). 


## Dépenses et graphiques

- L'utilisateur peut voir la répartition de ses dépenses par catégorie (graphique en anneau ou en camembert).
- L'utilisateur peut voir l'évolution de ses dépenses et de ses revenus mois par mois (graphique en barres ou en courbe).
- L'utilisateur peut comparer le mois en cours avec la moyenne des 3 derniers mois, catégorie par catégorie.
- L'utilisateur peut filtrer les graphiques par période (mois, trimestre, année, dates choisies) et par compte.
- L'utilisateur peut créer, renommer et supprimer ses propres catégories.
- Deux catégories d'un même utilisateur ne peuvent pas porter le même nom, sans tenir compte des majuscules.
- L'utilisateur peut changer la catégorie d'une transaction.
- À son arrivée depuis la banque, chaque transaction reçoit automatiquement une catégorie d'après son libellé, grâce à des règles communes fournies par l'application (par exemple « CARREFOUR » → Alimentation). Si aucune catégorie ne correspond, elle est classée dans « Autres ».
- Quand l'utilisateur change la catégorie d'une transaction, l'application lui propose d'appliquer ce choix aux prochaines transactions qui ont le même libellé.
- Une règle créée par l'utilisateur l'emporte sur une règle commune.
- L'utilisateur peut fixer un budget mensuel par catégorie.
- Chaque transaction appartient à une seule catégorie.
- Catégories par défaut : Logement, Alimentation, Transport, Loisirs, Santé, Shopping, Revenus, Autres.
- Supprimer une catégorie déplace ses transactions dans « Autres ».
- Les catégories par défaut se renomment et se suppriment comme les autres, sauf « Autres », qui ne peut être ni renommée ni supprimée.
- L'utilisateur est alerté quand il atteint 80 %, puis 100 % du budget d'une catégorie.


## Compte et données personnelles

- L'utilisateur peut changer son mot de passe.
- L'utilisateur peut changer son adresse e-mail.
- L'utilisateur peut changer ses données personnelles : son nom et son prénom.
- L'utilisateur peut effacer son nom et son prénom.
- L'utilisateur peut télécharger toutes ses données dans un fichier (droit à la portabilité prévu par le RGPD).
- Changer le mot de passe ou l'adresse e-mail demande de saisir le mot de passe actuel.
- Si la double authentification est activée, changer le mot de passe ou l'adresse e-mail demande aussi un code de l'application ou un code de secours.
- La nouvelle adresse e-mail doit être confirmée par un lien. L'ancienne adresse reçoit un message qui prévient du changement.
- Pour confirmer sa nouvelle adresse, l'utilisateur ouvre le lien reçu et saisit son mot de passe. Tant qu'elle n'est pas confirmée, l'ancienne reste valable. Un nouveau lien remplace l'ancien.
- La demande de changement affiche le même message, que la nouvelle adresse soit libre ou déjà utilisée.
- L'ancienne adresse est prévenue une fois le changement confirmé.
- Après un changement ou une récupération de mot de passe, les autres appareils connectés sont déconnectés.


## Administrateur et administration

- L'administrateur peut répondre aux demandes de support.
- L'administrateur peut débloquer un compte.
- L'administrateur ne voit ni les comptes bancaires, ni les transactions, ni les soldes, ni les sacs des utilisateurs. Chacune de ses actions est enregistrée dans le journal de sécurité.
- L'administrateur peut voir des statistiques globales et anonymes (nombre d'utilisateurs…).
- Le rôle d'administrateur est attribué ou retiré uniquement par le responsable du projet, depuis le serveur.
- Les connexions et les actions sensibles sont enregistrées dans un journal de sécurité.
- L'utilisateur peut contacter le support en cas de problème.
- L'utilisateur peut suivre l'état de ses demandes (envoyée, en cours, résolue).
- Un visiteur non connecté peut aussi contacter le support, par exemple si son compte est bloqué.


## Notifications

- L'utilisateur peut choisir comment il reçoit ses alertes : par e-mail, par notification sur son téléphone, ou les deux.
- L'utilisateur peut activer ou désactiver chaque type d'alerte.
- Types d'alertes : budget d'une catégorie atteint, nouvelle suggestion d'épargne, objectif d'un sac atteint, connexion depuis un nouvel appareil, autorisation bancaire à renouveler.
- Les alertes de sécurité (connexion depuis un nouvel appareil, compte bloqué, changement de mot de passe ou d'adresse e-mail) ne peuvent pas être désactivées.


## Suppression

- L'utilisateur peut supprimer son compte.
- La suppression demande de saisir le mot de passe, puis de confirmer.
- La demande de suppression révoque aussi l'autorisation donnée à la banque.
- Après la demande, l'utilisateur a 30 jours pour changer d'avis en se reconnectant. Passé ce délai, toutes ses données sont effacées définitivement, y compris des sauvegardes au plus tard 30 jours après. Dans le journal de sécurité, ses événements perdent son identifiant et son adresse IP.
- Un e-mail confirme la demande de suppression, puis un autre confirme l'effacement définitif.
