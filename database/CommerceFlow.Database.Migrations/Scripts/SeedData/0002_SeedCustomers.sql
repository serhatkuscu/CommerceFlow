-- Identity was reset to 0 by 0001_ClearData.sql, so a single multi-row INSERT
-- assigns CustomerId 1, 2, 3 deterministically in row order.
INSERT INTO dbo.Customers (Name, Email) VALUES
    (N'Ayse Yilmaz',  N'ayse.yilmaz@example.com'),
    (N'Mehmet Demir', N'mehmet.demir@example.com'),
    (N'Zeynep Kaya',  N'zeynep.kaya@example.com');
