-- Not journaled: this runs in full every time reset-data is invoked (see MigrationRunner.ResetData,
-- gated on CommerceFlowEnvironment=Development|Test).
--
-- Uses TRUNCATE, not DELETE + DBCC CHECKIDENT RESEED: RESEED's "next inserted row" value depends
-- on whether the table has EVER held a row since creation (an undocumented-feeling but real
-- SQL Server quirk) -- a virgin table reseeded to N produces N as the next identity, but the same
-- table on a later run (having held rows before) produces N + increment instead. That made the
-- first-ever reset-data run land on CustomerId 0 and a later run land on CustomerId 2, from the
-- exact same script. TRUNCATE TABLE resets identity to the column's defined seed (1) every time,
-- unconditionally, so this is actually deterministic instead of merely usually-deterministic.
--
-- TRUNCATE refuses to run while any FK constraint references the table, regardless of whether the
-- referencing table is empty -- so the FKs are dropped and recreated around it.
BEGIN TRY
    BEGIN TRANSACTION;

    ALTER TABLE dbo.OrderItems DROP CONSTRAINT FK_OrderItems_Orders;
    ALTER TABLE dbo.OrderItems DROP CONSTRAINT FK_OrderItems_Products;
    ALTER TABLE dbo.Orders DROP CONSTRAINT FK_Orders_Customers;

    TRUNCATE TABLE dbo.OrderItems;
    TRUNCATE TABLE dbo.Orders;
    TRUNCATE TABLE dbo.Products;
    TRUNCATE TABLE dbo.Customers;

    ALTER TABLE dbo.Orders
        ADD CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(CustomerId);
    ALTER TABLE dbo.OrderItems
        ADD CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders(OrderId);
    ALTER TABLE dbo.OrderItems
        ADD CONSTRAINT FK_OrderItems_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(ProductId);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH
