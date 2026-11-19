#!/usr/bin/env bash
# Sauvegarde chiffrée de la base de WalletFlow (SS §7 : une sauvegarde par jour, gardée 30 jours, chiffrée avant de partir).
#
# pg_dump lit la base au format personnalisé de PostgreSQL, et la sauvegarde est chiffrée au fil de la lecture avec le
# certificat public des sauvegardes (CMS, AES-256-GCM) : elle n'existe jamais en clair sur le disque. Le serveur n'a que ce
# certificat : il chiffre, mais ne peut pas déchiffrer. La clé privée qui déchiffre reste hors du serveur.
#
# Réglages, par variables d'environnement :
#   PGHOST, PGPORT, PGDATABASE, PGUSER  La base à sauvegarder, comme pour psql. Le mot de passe passe par PGPASSWORD ou
#                                       ~/.pgpass, jamais par la ligne de commande.
#   BACKUP_CERTIFICATE                  Le certificat public des sauvegardes (fichier PEM).
#   BACKUP_FOLDER                       Le dossier où ranger les sauvegardes chiffrées.
#   BACKUP_KEEP_DAYS                    La durée de conservation en jours, 30 par défaut. Les sauvegardes plus anciennes de ce
#                                       dossier sont effacées : une donnée effacée de la base disparaît ainsi des sauvegardes au
#                                       plus tard 30 jours après (GR Suppression). Un stockage distant applique la même durée
#                                       avec sa propre règle d'expiration.
#
# Écrit walletflow-AAAAMMJJ-HHMMSS.dump.cms (heure UTC) dans BACKUP_FOLDER, puis affiche son chemin.
set -euo pipefail

: "${BACKUP_CERTIFICATE:?Le réglage BACKUP_CERTIFICATE, le certificat public des sauvegardes, manque.}"
: "${BACKUP_FOLDER:?Le réglage BACKUP_FOLDER, le dossier des sauvegardes, manque.}"
keep_days="${BACKUP_KEEP_DAYS:-30}"

case "$keep_days" in
    '' | *[!0-9]*)
        echo "BACKUP_KEEP_DAYS doit être un nombre entier de jours." >&2
        exit 1
        ;;
esac

if [ "$keep_days" -lt 1 ]; then
    echo "BACKUP_KEEP_DAYS doit valoir au moins 1 jour." >&2
    exit 1
fi

if [ ! -r "$BACKUP_CERTIFICATE" ]; then
    echo "Le certificat des sauvegardes est illisible : $BACKUP_CERTIFICATE" >&2
    exit 1
fi

# Les fichiers créés ne sont lisibles que par l'utilisateur qui sauvegarde.
umask 077
mkdir -p "$BACKUP_FOLDER"

final="$BACKUP_FOLDER/walletflow-$(date -u +%Y%m%d-%H%M%S).dump.cms"
partial="$final.partiel"
trap 'rm -f "$partial"' EXIT

# Une sauvegarde interrompue n'est qu'un fichier partiel, effacé : seul un fichier complet porte le nom définitif.
pg_dump --format=custom --no-owner \
    | openssl cms -encrypt -binary -stream -aes-256-gcm -outform DER -out "$partial" "$BACKUP_CERTIFICATE"
mv "$partial" "$final"
trap - EXIT

# Efface les sauvegardes de ce dossier plus anciennes que la durée de conservation, et les restes d'une sauvegarde tuée en
# cours de route (arrêt brutal de la machine, par exemple), une fois vieux d'un jour.
find "$BACKUP_FOLDER" -maxdepth 1 -type f \
    \( -name 'walletflow-*.dump.cms' -mmin +"$((keep_days * 24 * 60))" -o -name 'walletflow-*.dump.cms.partiel' -mmin +1440 \) \
    -delete

echo "$final"
