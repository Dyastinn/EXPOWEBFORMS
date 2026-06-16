-- Remove ALL poc-schema objects and the schema itself.
-- Run this to fully undo the spike without touching anything else.

IF OBJECT_ID('poc.usp_ListCustomers', 'P') IS NOT NULL
    DROP PROCEDURE poc.usp_ListCustomers;
GO

IF OBJECT_ID('poc.Customers', 'U') IS NOT NULL
    DROP TABLE poc.Customers;
GO

IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'poc')
    DROP SCHEMA poc;
GO

PRINT 'poc schema and all objects removed.';
GO
