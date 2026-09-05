-- Provisionamento do SQL Server de DESENVOLVIMENTO da Secco Platform.
--
-- Este arquivo existe para o ambiente de desenvolvimento parar de ensinar `sa`. Antes, todas as
-- connection strings do compose usavam a conta de administrador do servidor — e era isso que o
-- próximo adotante copiava. Com `sa`, o isolamento database-per-tenant da ADR-0005 é convenção:
-- um erro de connection string alcança o banco do tenant vizinho.
--
-- O modelo aplicado aqui é o MESMO que o endpoint de provisionamento do SecureGate produz
-- (ADR-0028, issue #3): um login por banco, com db_owner APENAS nele e NADA no servidor.
-- db_owner é necessário porque cada produto roda as próprias migrations do EF Core.
--
-- A senha é fixa e pública de propósito: é ambiente de desenvolvimento local, o servidor não
-- é exposto e o banco é descartável (`docker compose --profile all down -v`). Em qualquer outro
-- ambiente, quem gera a senha é o SecureGate, aleatória e cifrada no catálogo.

SET NOCOUNT ON;
GO

DECLARE @databases TABLE (name SYSNAME, login SYSNAME);

INSERT INTO @databases (name, login) VALUES
    -- Banco de plataforma do SecureGate (identidade não é dado de tenant, ADR-0022)
    (N'secco_securegate',                  N'secco_securegate_app'),
    (N'secco_securegate_tenant_alfa',      N'secco_securegate_alfa_app'),
    -- Tenants de demonstração do LogStream
    (N'secco_logstream_tenant_alfa',       N'secco_logstream_alfa_app'),
    (N'secco_logstream_tenant_beta',       N'secco_logstream_beta_app'),
    -- NotificationHub: um tenant e o banco de plataforma do Hangfire (ADR-0015), que ao
    -- contrário das migrations do EF NÃO se cria sozinho
    (N'secco_notificationhub_tenant_alfa', N'secco_nh_alfa_app'),
    (N'secco_notificationhub_platform',    N'secco_nh_platform_app');

DECLARE @name SYSNAME, @login SYSNAME, @sql NVARCHAR(MAX);
DECLARE cursor_databases CURSOR LOCAL FAST_FORWARD FOR SELECT name, login FROM @databases;

OPEN cursor_databases;
FETCH NEXT FROM cursor_databases INTO @name, @login;

WHILE @@FETCH_STATUS = 0
BEGIN
    IF DB_ID(@name) IS NULL
    BEGIN
        SET @sql = N'CREATE DATABASE ' + QUOTENAME(@name) + N';';
        EXEC sp_executesql @sql;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
    BEGIN
        SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login)
                 + N' WITH PASSWORD = N''Secco@Dev123'', CHECK_POLICY = OFF;';
        EXEC sp_executesql @sql;
    END

    -- O usuário e a concessão vivem DENTRO do banco: é o que impede o login de um tenant
    -- de abrir o banco de outro.
    SET @sql = N'USE ' + QUOTENAME(@name) + N';
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = ' + QUOTENAME(@login, '''') + N')
            CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';
        ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@login) + N';';
    EXEC sp_executesql @sql;

    FETCH NEXT FROM cursor_databases INTO @name, @login;
END

CLOSE cursor_databases;
DEALLOCATE cursor_databases;
GO

PRINT 'Bancos de desenvolvimento provisionados com login proprio por banco (sem sa).';
GO
