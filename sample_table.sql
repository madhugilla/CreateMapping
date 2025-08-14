CREATE TABLE dbo.Customer (
    CustomerId INT IDENTITY(1,1) NOT NULL,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    Email NVARCHAR(255) NULL,
    CreatedDate DATETIME2 NULL,
    AmountDue DECIMAL(18,2) NULL,
    CONSTRAINT PK_Customer PRIMARY KEY (CustomerId)
);
