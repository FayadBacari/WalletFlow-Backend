-- Décrit le schéma public d'une base de WalletFlow par son catalogue : colonnes, contraintes, index, déclencheurs, fonctions et
-- tous les droits. Deux bases au même schéma donnent la même description : les tests de la sauvegarde et des migrations comparent
-- ainsi une base à une autre. PostgreSQL réécrit le texte d'une contrainte CHECK sans en changer le sens : ce texte n'est donc pas
-- comparé, et le comportement des contraintes est prouvé à part, par les tests SQL du script.
--
-- Lancement : psql -X -tA -F ' | ' -v ON_ERROR_STOP=1 -d <base> -f database/tests/describe-schema.sql
SELECT 'colonne', table_name, column_name, data_type, coalesce(character_maximum_length::text, ''), is_nullable, coalesce(column_default, '')
FROM information_schema.columns WHERE table_schema = 'public'
UNION ALL
SELECT 'contrainte', conrelid::regclass::text, conname, contype::text, condeferrable::text, condeferred::text, ''
FROM pg_constraint WHERE connamespace = 'public'::regnamespace
UNION ALL
SELECT 'index', tablename, indexname, '', '', '', '' FROM pg_indexes WHERE schemaname = 'public'
UNION ALL
SELECT 'declencheur', event_object_table, trigger_name, action_timing, event_manipulation, action_orientation, ''
FROM information_schema.triggers WHERE trigger_schema = 'public'
UNION ALL
SELECT 'fonction', p.proname, pg_get_function_identity_arguments(p.oid), p.prosecdef::text,
       coalesce(array_to_string(p.proconfig, ','), ''), coalesce(p.proacl::text, ''), ''
FROM pg_proc p WHERE p.pronamespace = 'public'::regnamespace
UNION ALL
SELECT 'droit_table', c.relname, coalesce(c.relacl::text, ''), '', '', '', ''
FROM pg_class c WHERE c.relnamespace = 'public'::regnamespace AND c.relkind = 'r'
UNION ALL
SELECT 'droit_colonne', a.attrelid::regclass::text, a.attname, a.attacl::text, '', '', ''
FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
WHERE c.relnamespace = 'public'::regnamespace AND a.attacl IS NOT NULL
UNION ALL
SELECT 'droit_schema', nspname, coalesce(nspacl::text, ''), '', '', '', '' FROM pg_namespace WHERE nspname = 'public'
ORDER BY 1, 2, 3, 4, 5, 6, 7;
