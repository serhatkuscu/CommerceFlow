CREATE TABLE dbo.Customers
(
    CustomerId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    Name        NVARCHAR(200)     NOT NULL,
    Email       NVARCHAR(256)     NOT NULL,
    CONSTRAINT UQ_Customers_Email UNIQUE (Email)
);
