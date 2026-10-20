\set ON_ERROR_STOP on
\if :{?db_name}
\else
\set db_name fieldservice
\endif
\set migrator_password ''
\set app_password ''
\getenv migrator_password FIELDSERVICE_MIGRATOR_PASSWORD
\getenv app_password FIELDSERVICE_APP_PASSWORD
SELECT :'db_name' ~ '^[a-z][a-z0-9_]{0,62}$' AS db_name_valid,
       length(:'migrator_password') >= 16 AND length(:'app_password') >= 16 AS passwords_valid,
       EXISTS (SELECT FROM pg_roles WHERE rolname = 'fieldservice_migrator') AS migrator_exists,
       EXISTS (SELECT FROM pg_roles WHERE rolname = 'fieldservice_app') AS app_exists,
       EXISTS (SELECT FROM pg_database WHERE datname = :'db_name') AS database_exists
\gset
\if :db_name_valid
\else
DO $$BEGIN RAISE EXCEPTION 'db_name must be a lower-case identifier of at most 63 characters'; END$$;
\endif
\if :passwords_valid
\else
DO $$BEGIN RAISE EXCEPTION 'FIELDSERVICE_MIGRATOR_PASSWORD and FIELDSERVICE_APP_PASSWORD must each hold at least 16 characters'; END$$;
\endif
\if :migrator_exists
\else
CREATE ROLE fieldservice_migrator LOGIN PASSWORD :'migrator_password';
\endif
\if :app_exists
\else
CREATE ROLE fieldservice_app LOGIN PASSWORD :'app_password';
\endif
ALTER ROLE fieldservice_migrator WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'migrator_password';
ALTER ROLE fieldservice_app WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS CONNECTION LIMIT 30 PASSWORD :'app_password';
ALTER ROLE fieldservice_migrator SET lock_timeout = '10s';
ALTER ROLE fieldservice_migrator SET search_path = fieldservice;
ALTER ROLE fieldservice_app SET statement_timeout = '15s';
ALTER ROLE fieldservice_app SET lock_timeout = '5s';
ALTER ROLE fieldservice_app SET idle_in_transaction_session_timeout = '30s';
ALTER ROLE fieldservice_app SET search_path = fieldservice;
\if :database_exists
\else
SELECT format('CREATE DATABASE %I OWNER fieldservice_migrator TEMPLATE template0 ENCODING %L LOCALE_PROVIDER builtin BUILTIN_LOCALE %L', :'db_name', 'UTF8', 'C.UTF-8') \gexec
\endif
ALTER DATABASE :"db_name" OWNER TO fieldservice_migrator;
ALTER DATABASE :"db_name" SET timezone TO 'UTC';
REVOKE CONNECT, TEMPORARY ON DATABASE :"db_name" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db_name" TO fieldservice_app;
\connect :"db_name"
CREATE SCHEMA IF NOT EXISTS fieldservice AUTHORIZATION fieldservice_migrator;
ALTER SCHEMA fieldservice OWNER TO fieldservice_migrator;
REVOKE ALL ON SCHEMA fieldservice FROM PUBLIC;
GRANT USAGE ON SCHEMA fieldservice TO fieldservice_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fieldservice_migrator IN SCHEMA fieldservice GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fieldservice_app;
