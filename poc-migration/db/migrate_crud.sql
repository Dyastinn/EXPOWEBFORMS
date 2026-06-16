-- CRUD stored procedures for poc.Customers
-- All objects created idempotently inside the poc schema only.
-- Run after setup.sql (which creates the schema and table).

-- ─── usp_GetCustomer ───────────────────────────────────────────────────────
IF OBJECT_ID('poc.usp_GetCustomer', 'P') IS NOT NULL
    DROP PROCEDURE poc.usp_GetCustomer;
GO

CREATE PROCEDURE poc.usp_GetCustomer
    @CustomerId INT
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
    WHERE CustomerId = @CustomerId;
END
GO

-- ─── usp_ListCustomers (updated — adds pagination, backward-compatible defaults) ──
IF OBJECT_ID('poc.usp_ListCustomers', 'P') IS NOT NULL
    DROP PROCEDURE poc.usp_ListCustomers;
GO

CREATE PROCEDURE poc.usp_ListCustomers
    @Page     INT = 1,
    @PageSize INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    -- COUNT(*) OVER() is computed once by the engine; avoids a second round-trip.
    SELECT
        CustomerId,
        Name,
        Email,
        TotalSpend,
        CASE
            WHEN TotalSpend >= 10000 THEN 'Gold'
            WHEN TotalSpend >= 1000  THEN 'Silver'
            ELSE                          'Bronze'
        END AS Tier,
        COUNT(*) OVER() AS TotalCount
    FROM poc.Customers
    ORDER BY TotalSpend DESC
    OFFSET  (@Page - 1) * @PageSize ROWS
    FETCH NEXT @PageSize            ROWS ONLY;
END
GO

-- ─── usp_CreateCustomer ────────────────────────────────────────────────────
IF OBJECT_ID('poc.usp_CreateCustomer', 'P') IS NOT NULL
    DROP PROCEDURE poc.usp_CreateCustomer;
GO

CREATE PROCEDURE poc.usp_CreateCustomer
    @Name       NVARCHAR(100),
    @Email      NVARCHAR(200),
    @TotalSpend DECIMAL(10, 2)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO poc.Customers (Name, Email, TotalSpend)
    VALUES (@Name, @Email, @TotalSpend);

    DECLARE @NewId INT = SCOPE_IDENTITY();
    EXEC poc.usp_GetCustomer @NewId;
END
GO

-- ─── usp_UpdateCustomer ────────────────────────────────────────────────────
IF OBJECT_ID('poc.usp_UpdateCustomer', 'P') IS NOT NULL
    DROP PROCEDURE poc.usp_UpdateCustomer;
GO

CREATE PROCEDURE poc.usp_UpdateCustomer
    @CustomerId INT,
    @Name       NVARCHAR(100),
    @Email      NVARCHAR(200),
    @TotalSpend DECIMAL(10, 2)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE poc.Customers
    SET    Name       = @Name,
           Email      = @Email,
           TotalSpend = @TotalSpend
    WHERE  CustomerId = @CustomerId;

    -- Returns 0 rows if the customer did not exist; caller detects 404 from empty result.
    IF @@ROWCOUNT > 0
        EXEC poc.usp_GetCustomer @CustomerId;
END
GO

-- ─── usp_DeleteCustomer ────────────────────────────────────────────────────
IF OBJECT_ID('poc.usp_DeleteCustomer', 'P') IS NOT NULL
    DROP PROCEDURE poc.usp_DeleteCustomer;
GO

CREATE PROCEDURE poc.usp_DeleteCustomer
    @CustomerId INT
AS
BEGIN
    SET NOCOUNT OFF;   -- let @@ROWCOUNT / rows-affected flow through to Dapper
    DELETE FROM poc.Customers WHERE CustomerId = @CustomerId;
END
GO
