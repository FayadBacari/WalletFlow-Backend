#!/usr/bin/env bash
# Restaure une sauvegarde chiffrée de WalletFlow dans une base neuve (SS §7). Jamais par-dessus une base existante : on vérifie
# la base restaurée, puis on y branche l'API.
#
# Le fichier est d'abord déchiffré dans un dossier privé : AES-256-GCM ne dit qu'à la fin de la lecture si le fichier a été
# modifié, et rien ne doit être restauré avant cette vérification. La copie en clair est effacée à la fin, quoi qu'il arrive.
# La restauration se fait en une seule transaction : une erreur ne laisse jamais une base à moitié restaurée.
#
# Réglages, par variables d'environnement :
#   PGHOST, PGPORT, PGUSER  Le serveur cible et un compte administrateur de PostgreSQL. Le mot de passe passe par PGPASSWORD
#                           ou ~/.pgpass.
#   BACKUP_FILE             La sauvegarde chiffrée (.dump.cms).
#   BACKUP_CERTIFICATE      Le certificat public des sauvegardes, qui désigne le destinataire.
#   BACKUP_PRIVATE_KEY      La clé privée des sauvegardes (fichier PEM), apportée seulement le temps de la restauration.
#   TARGET_DATABASE         Le nom de la base à créer : lettres minuscules, chiffres et « _ ».
#
# Le rôle walletflow_app doit déjà exister sur le serveur : les rôles ne font pas partie d'une sauvegarde (voir MPD §4).
# Après la restauration, la tâche horaire de l'API efface d'elle-même les comptes dont la suppression date de plus de 30 jours :
# une sauvegarde ancienne ne ressuscite donc pas durablement un compte effacé depuis (GR Suppression).
set -euo pipefail

: "${BACKUP_FILE:?Le réglage BACKUP_FILE, la sauvegarde à restaurer, manque.}"
: "${BACKUP_CERTIFICATE:?Le réglage BACKUP_CERTIFICATE, le certificat public des sauvegardes, manque.}"
: "${BACKUP_PRIVATE_KEY:?Le réglage BACKUP_PRIVATE_KEY, la clé privée des sauvegardes, manque.}"
: "${TARGET_DATABASE:?Le réglage TARGET_DATABASE, le nom de la base à créer, manque.}"

case "$TARGET_DATABASE" in
    '' | *[!a-z0-9_]*)
        echo "TARGET_DATABASE ne doit contenir que des lettres minuscules, des chiffres et « _ »." >&2
        exit 1
        ;;
esac

# Compte les lignes d'une requête faite sur la base d'administration du serveur.
count_rows() {
    psql -X -d postgres -tA -v ON_ERROR_STOP=1 -c "$1"
}

if [ "$(count_rows "SELECT count(*) FROM pg_database WHERE datname = '$TARGET_DATABASE'")" != "0" ]; then
    echo "La base « $TARGET_DATABASE » existe déjà : une restauration ne l'écrase jamais." >&2
    exit 1
fi

if [ "$(count_rows "SELECT count(*) FROM pg_roles WHERE rolname = 'walletflow_app'")" != "1" ]; then
    echo "Le rôle walletflow_app n'existe pas sur ce serveur : crée-le d'abord, avec son mot de passe (MPD §4)." >&2
    exit 1
fi

umask 077
work="$(mktemp -d)"
created=0
restored=0

# Efface la copie en clair, et la base créée si la restauration n'est pas allée au bout.
cleanup() {
    rm -rf "$work"
    if [ "$created" = 1 ] && [ "$restored" = 0 ]; then
        dropdb --if-exists "$TARGET_DATABASE"
    fi
}
trap cleanup EXIT

openssl cms -decrypt -binary -inform DER -in "$BACKUP_FILE" \
    -recip "$BACKUP_CERTIFICATE" -inkey "$BACKUP_PRIVATE_KEY" -out "$work/walletflow.dump"

createdb "$TARGET_DATABASE"
created=1
pg_restore --exit-on-error --single-transaction --no-owner --dbname="$TARGET_DATABASE" "$work/walletflow.dump"
restored=1

echo "La sauvegarde est restaurée dans la base « $TARGET_DATABASE »."
