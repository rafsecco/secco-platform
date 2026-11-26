-- Bancos de PLATAFORMA da Secco Platform e seus logins (ADR-0062, guia de implantação).
--
-- Quem aplica é o DBA, uma vez, com um login que pode criar banco e login:
--
--   sqlcmd -S <servidor> -U <admin> -C -b -i sqlserver-plataforma.sql \
--     -v SECUREGATE_DB_MIGRATOR_PASSWORD="..." SECUREGATE_DB_APP_PASSWORD="..." \
--        NOTIFICATIONHUB_JOBS_MIGRATOR_PASSWORD="..." NOTIFICATIONHUB_JOBS_APP_PASSWORD="..."
--
-- Cada banco tem DOIS logins: o "migrator" é dono (db_owner) e só o `migrate` o usa; o "app" é
-- o runtime, só leitura e escrita. No SecureGate isso é obrigatório (ADR-0041: o dono desligaria
-- o Row-Level Security, e o SecureGate recusa subir com runtime dona). Nenhum login recebe nada
-- no servidor além do acesso ao próprio banco, e a aplicação nunca usa `sa`.
--
-- Os bancos de TENANT não estão aqui: vêm do provisionamento do SecureGate (ADR-0028) ou do DBA.
-- Idempotente: rodar de novo não recria nada que já exista (e não troca senha).

:on error exit

-- ---------------------------------------------------------------------------------------------
-- SecureGate
-- ---------------------------------------------------------------------------------------------
IF DB_ID(N'secco_securegate') IS NULL
    CREATE DATABASE [secco_securegate];
GO

IF SUSER_ID(N'secco_securegate_migrator') IS NULL
    CREATE LOGIN [secco_securegate_migrator] WITH PASSWORD = N'$(SECUREGATE_DB_MIGRATOR_PASSWORD)', DEFAULT_DATABASE = [secco_securegate], CHECK_POLICY = ON;
IF SUSER_ID(N'secco_securegate_app') IS NULL
    CREATE LOGIN [secco_securegate_app] WITH PASSWORD = N'$(SECUREGATE_DB_APP_PASSWORD)', DEFAULT_DATABASE = [secco_securegate], CHECK_POLICY = ON;
GO

USE [secco_securegate];
IF USER_ID(N'secco_securegate_migrator') IS NULL
    CREATE USER [secco_securegate_migrator] FOR LOGIN [secco_securegate_migrator];
IF USER_ID(N'secco_securegate_app') IS NULL
    CREATE USER [secco_securegate_app] FOR LOGIN [secco_securegate_app];
ALTER ROLE [db_owner] ADD MEMBER [secco_securegate_migrator];
ALTER ROLE [db_datareader] ADD MEMBER [secco_securegate_app];
ALTER ROLE [db_datawriter] ADD MEMBER [secco_securegate_app];
GO

-- ---------------------------------------------------------------------------------------------
-- NotificationHub: fila do Hangfire (banco de plataforma do produto, ADR-0015)
-- ---------------------------------------------------------------------------------------------
USE [master];
IF DB_ID(N'secco_notificationhub_jobs') IS NULL
    CREATE DATABASE [secco_notificationhub_jobs];
GO

IF SUSER_ID(N'secco_nh_jobs_migrator') IS NULL
    CREATE LOGIN [secco_nh_jobs_migrator] WITH PASSWORD = N'$(NOTIFICATIONHUB_JOBS_MIGRATOR_PASSWORD)', DEFAULT_DATABASE = [secco_notificationhub_jobs], CHECK_POLICY = ON;
IF SUSER_ID(N'secco_nh_jobs_app') IS NULL
    CREATE LOGIN [secco_nh_jobs_app] WITH PASSWORD = N'$(NOTIFICATIONHUB_JOBS_APP_PASSWORD)', DEFAULT_DATABASE = [secco_notificationhub_jobs], CHECK_POLICY = ON;
GO

USE [secco_notificationhub_jobs];
IF USER_ID(N'secco_nh_jobs_migrator') IS NULL
    CREATE USER [secco_nh_jobs_migrator] FOR LOGIN [secco_nh_jobs_migrator];
IF USER_ID(N'secco_nh_jobs_app') IS NULL
    CREATE USER [secco_nh_jobs_app] FOR LOGIN [secco_nh_jobs_app];
ALTER ROLE [db_owner] ADD MEMBER [secco_nh_jobs_migrator];
ALTER ROLE [db_datareader] ADD MEMBER [secco_nh_jobs_app];
ALTER ROLE [db_datawriter] ADD MEMBER [secco_nh_jobs_app];
-- O Hangfire usa procedures e locks de aplicação no schema dele, criado pelo `migrate`
GRANT EXECUTE TO [secco_nh_jobs_app];
GO
