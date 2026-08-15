CREATE TABLE dbo.Products
(
    ProductId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
    SKU             NVARCHAR(50)      NOT NULL,
    Name            NVARCHAR(200)     NOT NULL,
    Price           DECIMAL(18,2)     NOT NULL,
    StockQuantity   INT               NOT NULL,
    CONSTRAINT UQ_Products_SKU UNIQUE (SKU),
    CONSTRAINT CK_Products_Price CHECK (Price >= 0),
    CONSTRAINT CK_Products_StockQuantity CHECK (StockQuantity >= 0)
);
