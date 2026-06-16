-- poc schema, table, and stored procedure
-- Idempotent: safe to run multiple times

-- Metadata-only check: does a poc schema collision exist outside our ownership?
-- (Read-only sys access is permitted per the spike rules)
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'poc')
    EXEC('CREATE SCHEMA poc');
GO

-- Table
IF OBJECT_ID('poc.Customers', 'U') IS NOT NULL
    DROP TABLE poc.Customers;
GO

CREATE TABLE poc.Customers (
    CustomerId  INT           IDENTITY(1,1) PRIMARY KEY,
    Name        NVARCHAR(100) NOT NULL,
    Email       NVARCHAR(200) NOT NULL,
    TotalSpend  DECIMAL(10,2) NOT NULL DEFAULT 0
);
GO

-- Stored procedure (business logic: Tier derived from TotalSpend thresholds)
IF OBJECT_ID('poc.usp_ListCustomers', 'P') IS NOT NULL
    DROP PROCEDURE poc.usp_ListCustomers;
GO

CREATE PROCEDURE poc.usp_ListCustomers
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        CustomerId,
        Name,
        Email,
        TotalSpend,
        CASE
            WHEN TotalSpend >= 10000 THEN 'Gold'
            WHEN TotalSpend >= 1000  THEN 'Silver'
            ELSE                          'Bronze'
        END AS Tier
    FROM poc.Customers
    ORDER BY TotalSpend DESC;
END
GO
