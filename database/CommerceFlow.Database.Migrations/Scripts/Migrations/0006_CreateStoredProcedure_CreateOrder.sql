CREATE PROCEDURE dbo.usp_CreateOrder
    @CustomerId INT,
    @Items      dbo.OrderItemTableType READONLY,
    @OrderId    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ErrorMessage NVARCHAR(2048);

    -- Pure validation: no transaction open yet, nothing to roll back.
    IF NOT EXISTS (SELECT 1 FROM @Items)
        THROW 51003, 'Order must contain at least one item.', 1;

    IF EXISTS (SELECT 1 FROM @Items WHERE Quantity <= 0)
    BEGIN
        DECLARE @BadQtyProductId INT = (SELECT TOP 1 ProductId FROM @Items WHERE Quantity <= 0 ORDER BY ProductId);
        SET @ErrorMessage = 'Quantity must be greater than zero for product ' + CAST(@BadQtyProductId AS NVARCHAR(20)) + '.';
        THROW 51004, @ErrorMessage, 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE CustomerId = @CustomerId)
    BEGIN
        SET @ErrorMessage = 'Customer ' + CAST(@CustomerId AS NVARCHAR(20)) + ' not found.';
        THROW 51000, @ErrorMessage, 1;
    END

    IF EXISTS (SELECT 1 FROM @Items i LEFT JOIN dbo.Products p ON p.ProductId = i.ProductId WHERE p.ProductId IS NULL)
    BEGIN
        DECLARE @MissingProductId INT = (
            SELECT TOP 1 i.ProductId FROM @Items i
            LEFT JOIN dbo.Products p ON p.ProductId = i.ProductId
            WHERE p.ProductId IS NULL ORDER BY i.ProductId);
        SET @ErrorMessage = 'Product ' + CAST(@MissingProductId AS NVARCHAR(20)) + ' not found.';
        THROW 51001, @ErrorMessage, 1;
    END

    -- Mutating section: transactional, explicit safe cleanup.
    BEGIN TRY
        BEGIN TRANSACTION;

        -- Grouped by product, ascending ProductId: canonical lock order across all callers,
        -- one UPDATE per distinct product regardless of duplicate request lines. This is what
        -- prevents deadlocks between concurrent orders sharing products in different submission
        -- order; the conditional UPDATE itself only prevents negative stock / oversell.
        DECLARE @ProductId INT, @TotalQuantity INT;
        DECLARE product_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT ProductId, SUM(Quantity)
            FROM @Items
            GROUP BY ProductId
            ORDER BY ProductId ASC;

        OPEN product_cursor;
        FETCH NEXT FROM product_cursor INTO @ProductId, @TotalQuantity;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            UPDATE dbo.Products
            SET StockQuantity = StockQuantity - @TotalQuantity
            WHERE ProductId = @ProductId AND StockQuantity >= @TotalQuantity;

            IF @@ROWCOUNT = 0
            BEGIN
                CLOSE product_cursor; DEALLOCATE product_cursor;
                SET @ErrorMessage = 'Insufficient stock for product ' + CAST(@ProductId AS NVARCHAR(20)) + '.';
                THROW 51002, @ErrorMessage, 1;
            END

            FETCH NEXT FROM product_cursor INTO @ProductId, @TotalQuantity;
        END
        CLOSE product_cursor; DEALLOCATE product_cursor;

        INSERT INTO dbo.Orders (CustomerId, CustomerName, CustomerEmail, OrderStatus, TotalAmount)
        SELECT @CustomerId, c.Name, c.Email, 0, 0
        FROM dbo.Customers c WHERE c.CustomerId = @CustomerId;

        SET @OrderId = SCOPE_IDENTITY();

        -- One row per ORIGINAL request line: duplicates preserved (AC8).
        INSERT INTO dbo.OrderItems (OrderId, ProductId, Quantity, UnitPrice, LineTotal)
        SELECT @OrderId, i.ProductId, i.Quantity, p.Price, i.Quantity * p.Price
        FROM @Items i JOIN dbo.Products p ON p.ProductId = i.ProductId;

        UPDATE dbo.Orders
        SET TotalAmount = (SELECT SUM(LineTotal) FROM dbo.OrderItems WHERE OrderId = @OrderId)
        WHERE OrderId = @OrderId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
