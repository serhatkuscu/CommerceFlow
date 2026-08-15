CREATE TABLE dbo.OrderItems
(
    OrderItemId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderItems PRIMARY KEY,
    OrderId      INT               NOT NULL CONSTRAINT FK_OrderItems_Orders REFERENCES dbo.Orders(OrderId),
    ProductId    INT               NOT NULL CONSTRAINT FK_OrderItems_Products REFERENCES dbo.Products(ProductId),
    Quantity     INT               NOT NULL CONSTRAINT CK_OrderItems_Quantity CHECK (Quantity > 0),
    UnitPrice    DECIMAL(18,2)     NOT NULL,
    LineTotal    DECIMAL(18,2)     NOT NULL
    -- Deliberately no uniqueness constraint on (OrderId, ProductId): duplicate product lines
    -- within one order are required to remain legal, not an oversight (see AC8).
);
