CREATE TABLE dbo.Orders
(
    OrderId             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
    CustomerId          INT               NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers(CustomerId),
    CustomerName        NVARCHAR(200)     NOT NULL,   -- denormalized at order time, may drift from Customers.Name
    CustomerEmail       NVARCHAR(256)     NOT NULL,   -- denormalized at order time
    OrderStatus         INT               NOT NULL CONSTRAINT CK_Orders_OrderStatus CHECK (OrderStatus IN (0, 1, 2)), -- 0=Pending,1=Confirmed,2=Cancelled
    TotalAmount         DECIMAL(18,2)     NOT NULL CONSTRAINT CK_Orders_TotalAmount CHECK (TotalAmount >= 0),
    IsExportedToErp     BIT               NOT NULL CONSTRAINT DF_Orders_IsExportedToErp DEFAULT (0),
    ExportedDate        DATETIME2         NULL,
    ErpExportAttempts   INT               NOT NULL CONSTRAINT DF_Orders_ErpExportAttempts DEFAULT (0),
    CreatedDate          DATETIME2        NOT NULL CONSTRAINT DF_Orders_CreatedDate DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Orders_ErpExportAttempts CHECK (ErpExportAttempts >= 0),
    CONSTRAINT CK_Orders_ErpConsistency CHECK (
        (IsExportedToErp = 0 AND ExportedDate IS NULL) OR
        (IsExportedToErp = 1 AND ExportedDate IS NOT NULL)
    )
);
