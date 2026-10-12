#!/bin/bash
# Provision the least-privilege migration role.
# Runs automatically on FIRST `docker compose up postgres` (empty pgdata)
# via docker-entrypoint-initdb.d. For existing data dirs, run the same
# statements manually — see ../retrofit-migrator.sql.
set -e

: "${POSTGRES_USER:=module_platform}"
: "${POSTGRES_DB:=module_platform}"
: "${POSTGRES_MIGRATOR_PASSWORD:=module_migrator}"

export PGUSER="$POSTGRES_USER"
export PGDATABASE="$POSTGRES_DB"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<EOSQL
-- Idempotent: safe to re-run.
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'module_migrator') THEN
    CREATE ROLE module_migrator WITH LOGIN CREATEDB;
  ELSE
    ALTER ROLE module_migrator WITH LOGIN CREATEDB;
  END IF;
END
\$\$;

-- Password from env (default: module_migrator). Quote-escape single quotes.
-- NOTE: keep local-dev passwords alphanumeric to avoid quoting surprises.
ALTER ROLE module_migrator WITH PASSWORD '$POSTGRES_MIGRATOR_PASSWORD';

-- Migrator needs to connect + create tables, and to CREATE DATABASE for
-- future per-tenant provisioning (P10 deferred item).
GRANT CONNECT, CREATE, TEMPORARY ON DATABASE "$POSTGRES_DB" TO module_migrator;
GRANT USAGE, CREATE ON SCHEMA public TO module_migrator;

-- Migrator will own migration-created objects. Hand DB/schema ownership to it
-- on FRESH installs so ALTER TABLE during MigrateAsync succeeds.
-- (On retrofit installs this is done by retrofit-migrator.sql instead,
--  since REASSIGN would touch existing objects.)
ALTER SCHEMA public OWNER TO module_migrator;

-- Runtime app role keeps working: DML on current + future objects.
GRANT CONNECT ON DATABASE "$POSTGRES_DB" TO "$POSTGRES_USER";
GRANT USAGE, CREATE ON SCHEMA public TO "$POSTGRES_USER";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO "$POSTGRES_USER";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO "$POSTGRES_USER";
ALTER DEFAULT PRIVILEGES FOR ROLE module_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "$POSTGRES_USER";
ALTER DEFAULT PRIVILEGES FOR ROLE module_migrator IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO "$POSTGRES_USER";
ALTER DEFAULT PRIVILEGES FOR ROLE module_migrator IN SCHEMA public
  GRANT EXECUTE ON FUNCTIONS TO "$POSTGRES_USER";
EOSQL
