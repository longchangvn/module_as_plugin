-- Retrofit an EXISTING database (pgdata already initialized) to the
-- module_migrator model. init/01-migrator.sh only runs on FIRST init.
--
-- Usage (host port may be 5432, or 5433 per PROGRESS.md notes):
--   PGPASSWORD=module_platform psql -h localhost -U module_platform \
--     -d module_platform -f module_as_plugin/db/retrofit-migrator.sql
--
-- Override the migrator password first if needed:
--   ALTER ROLE module_migrator WITH PASSWORD '...';

DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'module_migrator') THEN
    CREATE ROLE module_migrator WITH LOGIN CREATEDB PASSWORD 'module_migrator';
  ELSE
    ALTER ROLE module_migrator WITH LOGIN CREATEDB;
  END IF;
END
$$;

GRANT CONNECT, CREATE, TEMPORARY ON DATABASE module_platform TO module_migrator;
GRANT USAGE, CREATE ON SCHEMA public TO module_migrator;

-- Transfer ownership of existing objects so MigrateAsync run as
-- module_migrator can ALTER them (fresh installs don't need this — the
-- migrator owns everything it creates there).
-- NOTE: database ownership stays with module_platform; only the public
-- schema + user objects inside it move. (No REASSIGN OWNED: it touches
-- system-required objects owned by the bootstrap superuser and fails.)
ALTER SCHEMA public OWNER TO module_migrator;

-- Move only user tables/sequences/views/materialized views in public that
-- are still owned by the app role. Must run as an owner/superuser.
DO $$
DECLARE
  r record;
BEGIN
  -- Tables (owned sequences move with their table automatically).
  FOR r IN
    SELECT c.relname
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    JOIN pg_roles o ON o.oid = c.relowner
    WHERE n.nspname = 'public'
      AND o.rolname = 'module_platform'
      AND c.relkind IN ('r', 'p', 'f')
  LOOP
    EXECUTE format('ALTER TABLE public.%I OWNER TO module_migrator', r.relname);
  END LOOP;
  -- Standalone sequences (skip column-owned ones: they follow their table).
  FOR r IN
    SELECT c.relname
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    JOIN pg_roles o ON o.oid = c.relowner
    WHERE n.nspname = 'public'
      AND o.rolname = 'module_platform'
      AND c.relkind = 'S'
      AND NOT EXISTS (
        SELECT 1 FROM pg_depend d
        WHERE d.objid = c.oid AND d.deptype = 'a')
  LOOP
    EXECUTE format('ALTER SEQUENCE public.%I OWNER TO module_migrator', r.relname);
  END LOOP;
  -- Views / materialized views.
  FOR r IN
    SELECT c.relname, c.relkind
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    JOIN pg_roles o ON o.oid = c.relowner
    WHERE n.nspname = 'public'
      AND o.rolname = 'module_platform'
      AND c.relkind IN ('v', 'm')
  LOOP
    IF r.relkind = 'm' THEN
      EXECUTE format('ALTER MATERIALIZED VIEW public.%I OWNER TO module_migrator', r.relname);
    ELSE
      EXECUTE format('ALTER VIEW public.%I OWNER TO module_migrator', r.relname);
    END IF;
  END LOOP;
END
$$;

-- Hand runtime DML back to the app role on current + future objects.
GRANT CONNECT ON DATABASE module_platform TO module_platform;
GRANT USAGE, CREATE ON SCHEMA public TO module_platform;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO module_platform;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO module_platform;
ALTER DEFAULT PRIVILEGES FOR ROLE module_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO module_platform;
ALTER DEFAULT PRIVILEGES FOR ROLE module_migrator IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO module_platform;
ALTER DEFAULT PRIVILEGES FOR ROLE module_migrator IN SCHEMA public
  GRANT EXECUTE ON FUNCTIONS TO module_platform;
