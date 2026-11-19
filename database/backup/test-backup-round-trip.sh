#!/usr/bin/env bash
# Prouve la sauvegarde et la restauration de WalletFlow (SS §7) sur un serveur PostgreSQL de test, avec un compte
# administrateur (PGHOST, PGPORT, PGUSER) et le rôle walletflow_app sans mot de passe, comme en local et dans la CI.
# Le test crée ses propres bases et ses propres clés jetables, puis les efface : il ne touche à aucune autre base.
#
# Il vérifie que la sauvegarde est illisible sans la clé privée, qu'elle se restaure à l'identique (schéma, données et droits,
# par les tests SQL du script), que les vieilles sauvegardes sont effacées, et que la restauration refuse une base existante,
# une mauvaise clé et un fichier modifié, sans jamais laisser une base à moitié restaurée.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
suffix="$$"
source_db="walletflow_essai_sauvegarde_$suffix"
restored_db="walletflow_essai_restauree_$suffix"
refused_db="walletflow_essai_refusee_$suffix"
work="$(mktemp -d)"

# Efface les bases et les fichiers du test, quoi qu'il arrive.
cleanup() {
    for database in "$source_db" "$restored_db" "$refused_db"; do
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

# Dit si une base existe sur le serveur.
database_exists() {
    [ "$(psql -X -d postgres -tAc "SELECT count(*) FROM pg_database WHERE datname = '$1'")" = "1" ]
}

# Donne le contenu d'une base, sans les lignes que pg_dump change à chaque appel.
dump_of() {
    pg_dump --no-owner "$@" | grep -v '^\\restrict\|^\\unrestrict'
}

# Décrit le schéma public d'une base par son catalogue (voir database/tests/describe-schema.sql).
schema_of() {
    psql -X -d "$1" -tA -F ' | ' -v ON_ERROR_STOP=1 -f "$root/database/tests/describe-schema.sql"
}

# 1. La base source : le script du MPD, puis des données de chaque sorte, journal de sécurité compris.
createdb "$source_db"
if [ "$(psql -X -d postgres -tAc "SELECT count(*) FROM pg_roles WHERE rolname = 'walletflow_app'")" = "1" ]; then
    sed '/^CREATE ROLE walletflow_app LOGIN;$/d' "$root/database/001_create_walletflow_schema.sql" \
        | psql -X -q -d "$source_db" -v ON_ERROR_STOP=1 > /dev/null
else
    psql -X -q -d "$source_db" -v ON_ERROR_STOP=1 -f "$root/database/001_create_walletflow_schema.sql" > /dev/null
fi

psql -X -q -d "$source_db" -v ON_ERROR_STOP=1 > /dev/null <<'SQL'
DO $$
DECLARE
    compte uuid;
    autres uuid;
    connexion uuid;
    compte_bancaire uuid;
    sac_vacances uuid;
    suggestion_acceptee uuid;
BEGIN
    INSERT INTO utilisateur (email_utilisateur, mot_de_passe_hash, prenom_utilisateur)
    VALUES ('sauvegarde@exemple.test', 'empreinte-de-test', 'Élodie')
    RETURNING id_utilisateur INTO compte;

    INSERT INTO categorie (nom_categorie, est_categorie_autres, id_utilisateur)
    VALUES ('Autres', TRUE, compte)
    RETURNING id_categorie INTO autres;

    INSERT INTO connexion_bancaire (id_connexion_externe, nom_banque, date_expiration_autorisation, id_utilisateur)
    VALUES ('connexion-sauvegarde', 'Banque de test', now() + interval '180 days', compte)
    RETURNING id_connexion INTO connexion;

    INSERT INTO compte_bancaire (id_compte_externe, nom_compte, solde_compte, date_solde, id_connexion)
    VALUES ('compte-sauvegarde', 'Compte courant', 150000, now(), connexion)
    RETURNING id_compte INTO compte_bancaire;

    INSERT INTO transaction (id_transaction_externe, date_transaction, libelle_transaction, montant_transaction, id_compte, id_categorie)
    VALUES ('operation-sauvegarde', current_date, 'CB CAFÉ DES ÉCOLES', -450, compte_bancaire, autres);

    INSERT INTO sac (nom_sac, id_utilisateur)
    VALUES ('Vacances', compte)
    RETURNING id_sac INTO sac_vacances;

    INSERT INTO suggestion (regle_suggestion, mois_suggestion, economie_mensuelle, statut_suggestion, id_utilisateur)
    VALUES ('argent_disponible', DATE '2026-10-01', 3000, 'acceptee', compte)
    RETURNING id_suggestion INTO suggestion_acceptee;

    INSERT INTO mouvement_sac (montant_mouvement, id_sac_arrivee, id_suggestion)
    VALUES (3000, sac_vacances, suggestion_acceptee);

    INSERT INTO evenement_securite (type_evenement, resultat_evenement, adresse_ip_evenement, id_utilisateur)
    VALUES ('connexion', 'succes', '203.0.113.7', compte);
END;
$$;
SQL
ok "base source créée, avec des données de chaque sorte"

# 2. Des clés jetables : celle des sauvegardes, et une autre, qui ne doit rien ouvrir.
openssl req -x509 -newkey rsa:3072 -nodes -days 1 -subj "/CN=Sauvegardes WalletFlow (essai)" \
    -keyout "$work/cle-privee.pem" -out "$work/certificat.pem" 2> /dev/null
openssl req -x509 -newkey rsa:3072 -nodes -days 1 -subj "/CN=Autre destinataire (essai)" \
    -keyout "$work/autre-cle.pem" -out "$work/autre-certificat.pem" 2> /dev/null

# 3. Une sauvegarde de 31 jours, qui doit disparaître, et une de 29 jours, qui doit rester.
mkdir -p "$work/sauvegardes"
old_backup="$work/sauvegardes/walletflow-20000101-000000.dump.cms"
recent_backup="$work/sauvegardes/walletflow-20000102-000000.dump.cms"
touch "$old_backup" "$recent_backup"
touch -t "$(date -v-31d +%Y%m%d%H%M 2> /dev/null || date -d '31 days ago' +%Y%m%d%H%M)" "$old_backup"
touch -t "$(date -v-29d +%Y%m%d%H%M 2> /dev/null || date -d '29 days ago' +%Y%m%d%H%M)" "$recent_backup"

# 4. La sauvegarde.
backup="$(PGDATABASE="$source_db" BACKUP_CERTIFICATE="$work/certificat.pem" BACKUP_FOLDER="$work/sauvegardes" \
    "$here/backup-database.sh")"
[ -s "$backup" ] || fail "aucune sauvegarde écrite"
ok "sauvegarde écrite : $(basename "$backup")"

if pg_restore --list "$backup" > /dev/null 2>&1; then
    fail "la sauvegarde se lit sans la clé privée"
fi
if grep -q "sauvegarde@exemple.test" "$backup"; then
    fail "une adresse e-mail se lit en clair dans la sauvegarde"
fi
ok "la sauvegarde est illisible sans la clé privée"

if [ "$(python3 -c 'import os, sys; print(oct(os.stat(sys.argv[1]).st_mode & 0o777)[2:])' "$backup")" != "600" ]; then
    fail "la sauvegarde est lisible par d'autres utilisateurs de la machine"
fi
ok "la sauvegarde n'est lisible que par son propriétaire"

[ ! -e "$old_backup" ] || fail "la sauvegarde de 31 jours n'est pas effacée"
[ -e "$recent_backup" ] || fail "la sauvegarde de 29 jours est effacée"
ok "les sauvegardes de plus de 30 jours sont effacées, les autres restent"

# 5. La restauration, à l'identique : schéma, droits et données.
TARGET_DATABASE="$restored_db" BACKUP_FILE="$backup" BACKUP_CERTIFICATE="$work/certificat.pem" \
    BACKUP_PRIVATE_KEY="$work/cle-privee.pem" "$here/restore-database.sh" > /dev/null

if [ -z "$(schema_of "$source_db")" ] || ! diff <(schema_of "$source_db") <(schema_of "$restored_db") > /dev/null; then
    fail "le schéma restauré diffère de la source"
fi
if ! diff <(dump_of --data-only "$source_db") <(dump_of --data-only "$restored_db") > /dev/null; then
    fail "les données restaurées diffèrent de la source"
fi
ok "schéma, droits et données restaurés à l'identique, accents compris"

if ! psql -X -q -U walletflow_app -d "$restored_db" -v ON_ERROR_STOP=1 \
    -f "$root/database/tests/test_001_create_walletflow_schema.sql" > "$work/tests-sql.txt" 2>&1; then
    cat "$work/tests-sql.txt" >&2
    fail "les tests SQL échouent sur la base restaurée"
fi
ok "les $(grep -c 'OK :' "$work/tests-sql.txt") vérifications SQL passent sur la base restaurée, avec le rôle de l'API"

# 6. Les refus de la restauration.
source_before="$(dump_of --data-only "$source_db")"
if TARGET_DATABASE="$source_db" BACKUP_FILE="$backup" BACKUP_CERTIFICATE="$work/certificat.pem" \
    BACKUP_PRIVATE_KEY="$work/cle-privee.pem" "$here/restore-database.sh" > /dev/null 2>&1; then
    fail "la restauration a accepté une base existante"
fi
[ "$(dump_of --data-only "$source_db")" = "$source_before" ] || fail "la base existante a changé"
ok "une base existante n'est jamais écrasée"

if TARGET_DATABASE="$refused_db" BACKUP_FILE="$backup" BACKUP_CERTIFICATE="$work/autre-certificat.pem" \
    BACKUP_PRIVATE_KEY="$work/autre-cle.pem" "$here/restore-database.sh" > /dev/null 2>&1; then
    fail "une autre clé a ouvert la sauvegarde"
fi
if database_exists "$refused_db"; then
    fail "une base reste après le refus d'une mauvaise clé"
fi
ok "une autre clé n'ouvre rien, et ne laisse aucune base"

python3 -c "
import sys
path = sys.argv[1]
data = bytearray(open(path, 'rb').read())
data[len(data) // 2] ^= 1
open(path + '.modifiee', 'wb').write(data)
" "$backup"
if TARGET_DATABASE="$refused_db" BACKUP_FILE="$backup.modifiee" BACKUP_CERTIFICATE="$work/certificat.pem" \
    BACKUP_PRIVATE_KEY="$work/cle-privee.pem" "$here/restore-database.sh" > /dev/null 2>&1; then
    fail "une sauvegarde modifiée a été restaurée"
fi
if database_exists "$refused_db"; then
    fail "une base reste après le refus d'une sauvegarde modifiée"
fi
ok "une sauvegarde modifiée d'un seul bit est refusée, sans laisser de base"

# Un fichier bien chiffré qui n'est pas une sauvegarde : il se déchiffre, la base est créée, puis pg_restore échoue. La base
# créée doit disparaître avec l'échec.
head -c 4096 /dev/urandom \
    | openssl cms -encrypt -binary -stream -aes-256-gcm -outform DER -out "$work/pas-une-sauvegarde.cms" "$work/certificat.pem"
if TARGET_DATABASE="$refused_db" BACKUP_FILE="$work/pas-une-sauvegarde.cms" BACKUP_CERTIFICATE="$work/certificat.pem" \
    BACKUP_PRIVATE_KEY="$work/cle-privee.pem" "$here/restore-database.sh" > /dev/null 2>&1; then
    fail "un fichier qui n'est pas une sauvegarde a été restauré"
fi
if database_exists "$refused_db"; then
    fail "une base à moitié restaurée reste après l'échec de pg_restore"
fi
ok "un échec de pg_restore efface la base qu'il venait de créer"

# 7. Une sauvegarde qui échoue ne laisse aucun fichier.
files_before="$(ls "$work/sauvegardes" | wc -l)"
if PGDATABASE="walletflow_base_inexistante_$suffix" BACKUP_CERTIFICATE="$work/certificat.pem" \
    BACKUP_FOLDER="$work/sauvegardes" "$here/backup-database.sh" > /dev/null 2>&1; then
    fail "la sauvegarde d'une base inexistante a réussi"
fi
[ "$(ls "$work/sauvegardes" | wc -l)" = "$files_before" ] || fail "une sauvegarde ratée a laissé un fichier"
ok "une sauvegarde ratée ne laisse aucun fichier, même partiel"

# 8. Une sauvegarde tuée brutalement en pleine lecture ne laisse jamais un fichier au nom définitif, qui passerait pour une
#    sauvegarde complète. Son reste partiel est effacé par une sauvegarde suivante, une fois vieux d'un jour.
mkdir -p "$work/faux-outils" "$work/sauvegardes-tuees"
cat > "$work/faux-outils/pg_dump" <<EOF
#!/bin/sh
echo \$\$ > "$work/faux-pg-dump.pid"
printf 'PGDMP'
sleep 30
EOF
chmod +x "$work/faux-outils/pg_dump"
PATH="$work/faux-outils:$PATH" BACKUP_CERTIFICATE="$work/certificat.pem" BACKUP_FOLDER="$work/sauvegardes-tuees" \
    "$here/backup-database.sh" > /dev/null 2>&1 &
backup_pid=$!
# Détachée du shell du test, la sauvegarde tuée exprès n'affiche pas « Killed » dans le journal.
disown "$backup_pid"
for attempt in $(seq 1 50); do
    [ -s "$work/faux-pg-dump.pid" ] && break
    sleep 0.1
done
sleep 0.5
kill -9 "$backup_pid"
kill -9 "$(cat "$work/faux-pg-dump.pid")" 2> /dev/null || true
for attempt in $(seq 1 50); do
    kill -0 "$backup_pid" 2> /dev/null || break
    sleep 0.1
done
sleep 0.5
if ls "$work/sauvegardes-tuees"/walletflow-*.dump.cms > /dev/null 2>&1; then
    fail "une sauvegarde tuée a laissé un fichier au nom définitif"
fi
leftover="$(ls "$work/sauvegardes-tuees"/walletflow-*.dump.cms.partiel)"
touch -t "$(date -v-2d +%Y%m%d%H%M 2> /dev/null || date -d '2 days ago' +%Y%m%d%H%M)" "$leftover"
PGDATABASE="$source_db" BACKUP_CERTIFICATE="$work/certificat.pem" BACKUP_FOLDER="$work/sauvegardes-tuees" \
    "$here/backup-database.sh" > /dev/null
[ ! -e "$leftover" ] || fail "le reste d'une sauvegarde tuée n'est jamais effacé"
ok "une sauvegarde tuée ne passe jamais pour complète, et son reste est effacé le lendemain"

echo "Tous les essais de la sauvegarde sont passés."
