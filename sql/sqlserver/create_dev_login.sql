-- Creates the local development SQL login used by appsettings.Development.json.
-- Run once against LocalDB with Windows authentication:
--   sqlcmd -S "(localdb)\MSSQLLocalDB" -E -i sql/sqlserver/create_dev_login.sql
-- Development only: never reuse this password outside a local machine.

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'user')
    CREATE LOGIN [user] WITH PASSWORD = N'PaymentApi!2026', CHECK_POLICY = OFF;
ELSE
    ALTER LOGIN [user] WITH PASSWORD = N'PaymentApi!2026', CHECK_POLICY = OFF;

ALTER SERVER ROLE [dbcreator] ADD MEMBER [user];

IF DB_ID(N'PaymentSystem') IS NOT NULL
BEGIN
    EXEC (N'USE [PaymentSystem];
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''user'')
            CREATE USER [user] FOR LOGIN [user];
        ALTER ROLE [db_owner] ADD MEMBER [user];');
END
