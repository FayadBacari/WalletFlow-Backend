#!/usr/bin/env bash
# Fait évoluer une base de WalletFlow sans jamais la recréer : applique, dans l'ordre de leur numéro, les migrations de
# database/migrations qui n'y sont pas encore passées. Une base vide reçoit ainsi tout le schéma, et une base existante seulement
# ce qui lui manque.
#
# Chaque migration passe dans sa propre transaction, avec la ligne qui la note dans l'historique : elle passe en entier, ou pas
# du tout. L'historique (walletflow_migrations.migration_appliquee) garde le numéro, le nom et l'empreinte SHA-256 de chaque
# fichier : un fichier modifié après son passage est refusé, au lieu de laisser la base et le dépôt se contredire. Il vit dans son
# propre schéma, auquel le rôle de l'API n'a aucun accès. Deux lancements en même temps ne passent jamais deux fois la même
# migration : chacune attend la précédente, et la clé de l'historique refuse un doublon.
#
# Une migration ne contient ni BEGIN, ni COMMIT, ni ROLLBACK : ce script ouvre et ferme sa transaction. Son fichier se nomme
# NNN_nom.sql, avec un numéro à trois chiffres et un nom en minuscules, chiffres et tirets bas.
#
# Usage :
#   database/migrate.sh              applique les migrations qui manquent.
#   database/migrate.sh --baseline   adopte une base créée avant cet outil par le script du MPD : note la migration 001 comme
#                                    déjà passée, sans la lancer, puis applique les suivantes. Refusé sur une base sans le
#                                    schéma de WalletFlow, ou qui a déjà un historique.
#
# Réglages, par variables d'environnement :
#   PGHOST, PGPORT, PGDATABASE, PGUSER  La base, avec un compte administrateur de PostgreSQL : les migrations changent le schéma.
#                                       Le mot de passe passe par PGPASSWORD ou ~/.pgpass, jamais par la ligne de commande.
#   MIGRATIONS_FOLDER                   Le dossier des migrations, database/migrations par défaut. Les tests en donnent un autre.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
folder="${MIGRATIONS_FOLDER:-$here/migrations}"

# La clé du verrou que prend chaque migration : « WFmigrat » écrit en octets ASCII.
lock_key=6288834229144019316

# Arrête le script sur une erreur, en disant laquelle.
fail() {
    echo "ÉCHEC : $1" >&2
    exit 1
}

# Lance psql sans réglage personnel, arrêté à la première erreur.
run_psql() {
    psql -X -q -v ON_ERROR_STOP=1 "$@"
}

# Donne l'empreinte SHA-256 d'un fichier.
sha256_of() {
    if command -v sha256sum > /dev/null; then
        sha256sum "$1" | cut -d ' ' -f 1
    else
        shasum -a 256 "$1" | cut -d ' ' -f 1
    fi
}

# Crée l'historique s'il n'existe pas encore. Un nouveau schéma n'accorde rien à PUBLIC : seul le compte administrateur y a accès.
create_history() {
    run_psql --single-transaction > /dev/null <<SQL
SET LOCAL client_min_messages = warning;
SELECT pg_advisory_xact_lock($lock_key);
CREATE SCHEMA IF NOT EXISTS walletflow_migrations;
CREATE TABLE IF NOT EXISTS walletflow_migrations.migration_appliquee (
    numero           INTEGER      PRIMARY KEY,
    nom              VARCHAR(200) NOT NULL UNIQUE,
    empreinte        CHAR(64)     NOT NULL,
    date_application TIMESTAMPTZ  NOT NULL DEFAULT now()
);
SQL
}

# Donne l'empreinte notée dans l'historique pour ce numéro, ou rien si la migration n'est pas encore passée.
recorded_checksum_of() {
    run_psql -tA -c "SELECT empreinte FROM walletflow_migrations.migration_appliquee WHERE numero = $1"
}

# Note une migration comme passée, sans la lancer : seulement pour adopter une base créée par le script du MPD.
record_without_running() {
    run_psql --single-transaction -c "SELECT pg_advisory_xact_lock($lock_key)" \
        -c "INSERT INTO walletflow_migrations.migration_appliquee (numero, nom, empreinte) VALUES ($1, '$2', '$3')" > /dev/null
}

# Passe une migration et la note dans l'historique, dans une seule transaction.
apply() {
    run_psql --single-transaction -c "SELECT pg_advisory_xact_lock($lock_key)" -f "$1" \
        -c "INSERT INTO walletflow_migrations.migration_appliquee (numero, nom, empreinte) VALUES ($2, '$3', '$4')" > /dev/null
}

baseline=false
case "${1:-}" in
    '') ;;
    --baseline) baseline=true ;;
    *) fail "argument inconnu : $1 (seul --baseline est accepté)." ;;
esac

[ -d "$folder" ] || fail "le dossier des migrations n'existe pas : $folder"

# Les migrations, dans l'ordre de leur numéro : chaque nom est vérifié, et aucun numéro ne se répète.
migrations=()
previous_number=0
for file in "$folder"/*.sql; do
    [ -e "$file" ] || continue
    name="$(basename "$file")"
    [[ "$name" =~ ^[0-9]{3}_[a-z0-9_]+\.sql$ ]] || fail "le nom de la migration $name ne suit pas la forme NNN_nom.sql."
    number=$((10#${name:0:3}))
    [ "$number" -gt "$previous_number" ] || fail "le numéro de la migration $name est déjà pris."
    if grep -qiE '^[[:space:]]*(BEGIN|COMMIT|ROLLBACK)[[:space:]]*;' "$file"; then
        fail "la migration $name ouvre ou ferme sa propre transaction : migrate.sh s'en charge."
    fi
    migrations+=("$file")
    previous_number=$number
done

[ "${#migrations[@]}" -gt 0 ] || fail "aucune migration dans $folder."

create_history

if [ "$baseline" = true ]; then
    [ "$(run_psql -tA -c "SELECT count(*) FROM walletflow_migrations.migration_appliquee")" = "0" ] \
        || fail "cette base a déjà un historique : --baseline ne sert qu'à adopter une base créée avant l'outil."
    [ "$(run_psql -tA -c "SELECT to_regclass('public.utilisateur') IS NOT NULL")" = "t" ] \
        || fail "cette base n'a pas le schéma de WalletFlow : lance migrate.sh sans --baseline pour le créer."
    first="${migrations[0]}"
    first_name="$(basename "$first")"
    [ "$((10#${first_name:0:3}))" = "1" ] || fail "la première migration doit porter le numéro 001."
    record_without_running 1 "$first_name" "$(sha256_of "$first")"
    echo "Adoptée : $first_name, notée comme passée sans être lancée."
fi

applied_count=0
for file in "${migrations[@]}"; do
    name="$(basename "$file")"
    number=$((10#${name:0:3}))
    checksum="$(sha256_of "$file")"
    recorded="$(recorded_checksum_of "$number")"

    if [ -n "$recorded" ]; then
        [ "$recorded" = "$checksum" ] \
            || fail "la migration $name a changé depuis son passage : écris une nouvelle migration au lieu de la modifier."
        continue
    fi

    apply "$file" "$number" "$name" "$checksum" || fail "la migration $name a échoué : sa transaction est annulée, rien n'en reste."
    echo "Appliquée : $name"
    applied_count=$((applied_count + 1))
done

last="$(run_psql -tA -c "SELECT coalesce(max(numero), 0) FROM walletflow_migrations.migration_appliquee")"
echo "$applied_count migration(s) appliquée(s). La base est à la migration $(printf '%03d' "$last")."
