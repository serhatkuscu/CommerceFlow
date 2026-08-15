CREATE PROCEDURE dbo.usp_GetOrderById
    @OrderId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Two result sets (header, then lines) rather than a join, so the header
    -- doesn't repeat once per line. The DAL reads both in order.
    SELECT
        o.OrderId,
        o.CustomerId,
        o.CustomerName,
        o.CustomerEmail,
        o.OrderStatus,
        o.TotalAmount,
        o.IsExportedToErp,
        o.ExportedDate,
        o.ErpExportAttempts,
        o.CreatedDate
    FROM dbo.Orders o
    WHERE o.OrderId = @OrderId;

    SELECT
        oi.OrderItemId,
        oi.OrderId,
        oi.ProductId,
        oi.Quantity,
        oi.UnitPrice,
        oi.LineTotal
    FROM dbo.OrderItems oi
    WHERE oi.OrderId = @OrderId
    ORDER BY oi.OrderItemId;
END
