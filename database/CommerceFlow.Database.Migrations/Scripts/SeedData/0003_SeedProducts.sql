-- ProductId 1-5, deterministic (see 0002_SeedCustomers.sql for why).
-- Product 5 is intentionally low-stock: reserved for the future oversell/concurrency test (AC11).
INSERT INTO dbo.Products (SKU, Name, Price, StockQuantity) VALUES
    (N'SKU-0001', N'Wireless Mouse',      249.90,  100),
    (N'SKU-0002', N'Mechanical Keyboard', 899.00,  50),
    (N'SKU-0003', N'USB-C Hub',           349.50,  30),
    (N'SKU-0004', N'27" Monitor',         6499.00, 15),
    (N'SKU-0005', N'Limited Stock Item',  199.00,  1);
