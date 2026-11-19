#!/usr/bin/env bash
# Prouve l'outil de migration (database/migrate.sh) sur un serveur PostgreSQL de test, avec un compte administrateur (PGHOST,
# PGPORT, PGUSER) et le rôle walletflow_app sans mot de passe, comme en local et dans la CI. Le test crée ses propres bases et
# dossiers jetables, puis les efface : il ne touche à aucune autre base.
#
# Il vérifie qu'une base vide migrée a exactement le schéma d'une base neuve créée par le script du MPD, et passe ses tests SQL ;
# qu'un second lancement ne change rien ; que le rôle de l'API n'a aucun accès à l'historique ; qu'une base créée par le MPD
# s'adopte avec --baseline ; qu'une nouvelle migration passe en entier, et qu'une migration en échec ne laisse rien ; et qu'une
# migration modifiée après son passage, mal nommée ou qui ouvre sa propre transaction est refusée.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
suffix="$$"
fresh_db="walletflow_essai_mpd_$suffix"
migrated_db="walletflow_essai_migree_$suffix"
adopted_db="walletflow_essai_adoptee_$suffix"
empty_db="walletflow_essai_vide_$suffix"
work="$(mktemp -d)"

# Efface les bases et les fichiers du test, quoi qu'il arrive.
cleanup() {
    for database in "$fresh_db" "$migrated_db" "$adopted_db" "$empty_db"; do
        dropdb --if-exists "$database" 2> /dev/null || true
    done
    rm -rf "$work"
}
trap cleanup EXIT

# Arrête le test sur un échec, en disant lequel.
fail() {
    echo "ÉCHEC : $1" >&2
    exit 1
}

# Note une vérification réussie.
ok() {
    echo "OK : $1"
}

# Décrit le schéma public d'une base (voir describe-schema.sql).
schema_of() {
    psql -X -d "$1" -tA -F ' | ' -v ON_ERROR_STOP=1 -f "$root/database/tests/describe-schema.sql"
}

# Lit une seule valeur dans une base.
value_of() {
    psql -X -d "$1" -tAc "$2"
}

# Crée une base avec le script du MPD, comme avant l'outil. Le rôle de l'API, commun au serveur, n'est créé qu'une fois.
create_with_mpd_script() {
    createdb "$1"
    if [ "$(value_of postgres "SELECT count(*) FROM pg_roles WHERE rolname = 'walletflow_app'")" = "1" ]; then
        sed '/^CREATE ROLE walletflow_app LOGIN;$/d' "$root/database/001_create_walletflow_schema.sql" \
            | psql -X -q -d "$1" -v ON_ERROR_STOP=1 > /dev/null
    else
        psql -X -q -d "$1" -v ON_ERROR_STOP=1 -f "$root/database/001_create_walletflow_schema.sql" > /dev/null
    fi
}

# Lance l'outil sur une base, avec ce dossier de migrations ou celui du dépôt, et ses autres arguments.
migrate() {
    local database="$1" folder="$2"
    shift 2
    PGDATABASE="$database" MIGRATIONS_FOLDER="${folder:-$root/database/migrations}" "$root/database/migrate.sh" "$@"
}

# Vérifie que l'outil refuse, avec ce message, et renvoie son texte d'erreur.
expect_refusal() {
    local expected="$1" description="$2"
    shift 2
    if "$@" > "$work/sortie.txt" 2> "$work/erreur.txt"; then
        fail "$description : l'outil a accepté."
    fi
    grep -q "$expected" "$work/erreur.txt" || fail "$description : le refus ne dit pas « $expected » ($(cat "$work/erreur.txt"))."
}

# 1. Une base vide migrée a exactement le schéma d'une base neuve créée par le MPD.
create_with_mpd_script "$fresh_db"
createdb "$migrated_db"
migrate "$migrated_db" "" > "$work/premier-passage.txt"
grep -q "^Appliquée : 001_baseline.sql$" "$work/premier-passage.txt" || fail "la migration 001 n'a pas été appliquée."
if [ -z "$(schema_of "$fresh_db")" ] || ! diff <(schema_of "$fresh_db") <(schema_of "$migrated_db") > "$work/ecart.txt"; then
    fail "la base migrée n'a pas le schéma du MPD : $(head -20 "$work/ecart.txt")"
fi
ok "une base vide migrée a exactement le schéma d'une base neuve créée par le MPD"

# 2. Elle passe les tests SQL du script, avec le rôle de l'API.
psql -X -q -U walletflow_app -d "$migrated_db" -v ON_ERROR_STOP=1 \
    -f "$root/database/tests/test_001_create_walletflow_schema.sql" > "$work/tests-sql.txt" 2>&1 \
    || fail "la base migrée ne passe pas les tests SQL du script : $(tail -5 "$work/tests-sql.txt")"
ok "la base migrée passe les tests SQL du script"

# 3. Un second lancement ne change rien.
migrate "$migrated_db" "" > "$work/second-passage.txt"
grep -q "^0 migration(s) appliquée(s). La base est à la migration 001.$" "$work/second-passage.txt" \
    || fail "le second lancement a changé quelque chose : $(cat "$work/second-passage.txt")"
diff <(schema_of "$fresh_db") <(schema_of "$migrated_db") > /dev/null || fail "le second lancement a changé le schéma."
ok "un second lancement ne change rien"

# 4. Le rôle de l'API n'a aucun accès à l'historique.
if psql -X -U walletflow_app -d "$migrated_db" -c "SELECT * FROM walletflow_migrations.migration_appliquee" > /dev/null 2>&1; then
    fail "le rôle de l'API lit l'historique des migrations."
fi
ok "le rôle de l'API n'a aucun accès à l'historique"

# 5. Une base créée par le MPD avant l'outil s'adopte : la migration 001 est notée sans être lancée, et le schéma ne change pas.
create_with_mpd_script "$adopted_db"
schema_of "$adopted_db" > "$work/schema-avant-adoption.txt"
migrate "$adopted_db" "" --baseline > "$work/adoption.txt"
grep -q "^Adoptée : 001_baseline.sql" "$work/adoption.txt" || fail "la base n'a pas été adoptée : $(cat "$work/adoption.txt")"
grep -q "^Appliquée : 001_baseline.sql$" "$work/adoption.txt" && fail "l'adoption a relancé la migration 001."
diff "$work/schema-avant-adoption.txt" <(schema_of "$adopted_db") > /dev/null || fail "l'adoption a changé le schéma."
[ "$(value_of "$adopted_db" "SELECT count(*) FROM walletflow_migrations.migration_appliquee")" = "1" ] \
    || fail "l'adoption n'a pas noté une seule migration."
expect_refusal "déjà un historique" "une deuxième adoption" migrate "$adopted_db" "" --baseline
createdb "$empty_db"
expect_refusal "pas le schéma de WalletFlow" "l'adoption d'une base vide" migrate "$empty_db" "" --baseline
ok "une base créée par le MPD s'adopte avec --baseline, une seule fois, et jamais une base vide"

# 6. Une nouvelle migration passe en entier, et seulement elle.
mkdir "$work/migrations"
cp "$root/database/migrations/"*.sql "$work/migrations/"
cat > "$work/migrations/002_essai_ajout.sql" <<'SQL'
CREATE TABLE essai_migration (id_essai INTEGER PRIMARY KEY);
SQL
migrate "$migrated_db" "$work/migrations" > "$work/ajout.txt"
grep -q "^1 migration(s) appliquée(s). La base est à la migration 002.$" "$work/ajout.txt" \
    || fail "la nouvelle migration n'est pas passée seule : $(cat "$work/ajout.txt")"
[ "$(value_of "$migrated_db" "SELECT to_regclass('public.essai_migration') IS NOT NULL")" = "t" ] \
    || fail "la nouvelle migration n'a pas créé sa table."
ok "une nouvelle migration passe, et seulement elle"

# 7. Une migration en échec ne laisse rien : ni sa première instruction, ni sa ligne dans l'historique.
cat > "$work/migrations/003_essai_echec.sql" <<'SQL'
CREATE TABLE essai_echec (id_essai INTEGER PRIMARY KEY);
SELECT 1 / 0;
SQL
expect_refusal "003_essai_echec.sql a échoué" "une migration en échec" migrate "$migrated_db" "$work/migrations"
[ "$(value_of "$migrated_db" "SELECT to_regclass('public.essai_echec') IS NULL")" = "t" ] || fail "la migration en échec a laissé sa table."
[ "$(value_of "$migrated_db" "SELECT count(*) FROM walletflow_migrations.migration_appliquee WHERE numero = 3")" = "0" ] \
    || fail "la migration en échec est notée dans l'historique."
rm "$work/migrations/003_essai_echec.sql"
ok "une migration en échec ne laisse rien"

# 8. Une migration modifiée après son passage est refusée, et rien ne passe.
echo "-- modifiée après son passage" >> "$work/migrations/002_essai_ajout.sql"
expect_refusal "002_essai_ajout.sql a changé depuis son passage" "une migration modifiée" migrate "$migrated_db" "$work/migrations"
ok "une migration modifiée après son passage est refusée"

# 9. Un nom mal formé, un numéro en double et une migration qui ouvre sa propre transaction sont refusés avant tout passage.
for case_name in nom double transaction; do
    mkdir "$work/cas-$case_name"
    cp "$root/database/migrations/"*.sql "$work/cas-$case_name/"
done
echo "SELECT 1;" > "$work/cas-nom/002-Mauvais Nom.sql"
echo "SELECT 1;" > "$work/cas-double/001_double.sql"
printf 'BEGIN;\nSELECT 1;\nCOMMIT;\n' > "$work/cas-transaction/002_transaction.sql"
expect_refusal "ne suit pas la forme NNN_nom.sql" "un nom mal formé" migrate "$empty_db" "$work/cas-nom"
expect_refusal "est déjà pris" "un numéro en double" migrate "$empty_db" "$work/cas-double"
expect_refusal "ouvre ou ferme sa propre transaction" "une migration avec sa transaction" migrate "$empty_db" "$work/cas-transaction"
[ "$(value_of "$empty_db" "SELECT to_regclass('public.utilisateur') IS NULL")" = "t" ] || fail "un refus a laissé passer une migration."
ok "un nom mal formé, un numéro en double et une migration avec sa propre transaction sont refusés avant tout passage"

echo "Tous les tests des migrations sont passés."
