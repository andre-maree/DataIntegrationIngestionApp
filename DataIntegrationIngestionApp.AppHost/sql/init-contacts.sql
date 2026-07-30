-- Aspire runs this as the DemoDatabase creation script (executed in the master context).
-- It creates the database itself and then the ingestion target table used by the app.
IF DB_ID(N'DemoDatabase') IS NULL
BEGIN
	CREATE DATABASE [DemoDatabase];
END;
GO

USE [DemoDatabase];
GO

IF OBJECT_ID(N'dbo.Contacts', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.Contacts
	(
		Id      INT           NOT NULL,
		Name    NVARCHAR(200) NOT NULL,
		Surname NVARCHAR(200) NOT NULL,
		Age     INT           NULL,
		Email   NVARCHAR(320) NULL
	);
END;
GO
