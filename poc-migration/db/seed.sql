-- Seed poc.Customers with representative data spanning all Tier brackets
DELETE FROM poc.Customers;
GO

INSERT INTO poc.Customers (Name, Email, TotalSpend) VALUES
    ('Alice Chen',   'alice@example.com',  15250.00),
    ('Bob Martinez', 'bob@example.com',     5430.75),
    ('Carol Davis',  'carol@example.com',    800.00),
    ('David Kim',    'david@example.com',  22100.50),
    ('Eve Robinson', 'eve@example.com',      250.00);
GO
