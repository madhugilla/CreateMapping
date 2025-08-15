CREATE TABLE dbo.MigrantCase
(
    MigrantCaseID           BIGINT            NOT NULL,
    CaseGUID                NVARCHAR(255)     NULL,
    CaseNo                  NVARCHAR(32)      NOT NULL,
    RegistrationDate        DATETIME          NOT NULL,
    SendingMission          NVARCHAR(5)       NULL,
    PrimarySource           NVARCHAR(50)      NULL,
    PrimaryRefNo            NVARCHAR(50)      NULL,
    DestinationCountry      NVARCHAR(3)       NULL,
    Category                NVARCHAR(50)      NULL,
    SecondarySource         NVARCHAR(50)      NULL,
    SecondaryReferenceNo    NVARCHAR(50)      NULL,
    ReferralDate            DATETIME          NULL,
    ReferralAgency          NVARCHAR(50)      NULL,
    ReferralEntity          NVARCHAR(10)      NULL,
    Location                NVARCHAR(50)      NULL,
    LocationCountry         NVARCHAR(3)       NULL,
    BasedCity               NVARCHAR(50)      NULL,
    BasedCountry            NVARCHAR(3)       NULL,
    PromissoryNoteCategory  NVARCHAR(10)      NULL,
    FinalDestination        NVARCHAR(5)       NULL,
    EarliestTravelDate      DATETIME          NULL,
    LatestTravelDate        DATETIME          NULL,
    GlobalCaseStatus        NVARCHAR(10)      NULL,
    Remarks                 NVARCHAR(255)     NULL,
    ChangeStatusReason      NVARCHAR(10)      NULL,
    ChangeStatusOtherReason NVARCHAR(255)     NULL,
    CreatedBy               NVARCHAR(50)      NOT NULL,
    ManagingMission         NVARCHAR(5)       NULL,
    LastDateModified        DATETIME          NULL,
    IsRevoke                BIT               NOT NULL,
    ValidFrom               DATETIME          NULL,
    ValidTo                 DATETIME          NULL,
    CaseWorker              NVARCHAR(50)      NULL,
    IsUnaccompaniedMinor    BIT               NOT NULL,
    xMFID                   NVARCHAR(20)      NULL,
    FinalDestinationType    NVARCHAR(5)       NULL,
    ReferralAgencyContact   NVARCHAR(100)     NULL,
    trigger_timestamp       DATETIME2(7)      NOT NULL,
    isExternal              BIT               NULL,
    rowguid                 UNIQUEIDENTIFIER  NOT NULL
);
GO

-- Primary Key on CaseNo (as in image)
ALTER TABLE dbo.MigrantCase
  ADD CONSTRAINT PK_MigrantCase
  PRIMARY KEY (CaseNo);
GO

-- Default constraints (named to match your screenshot)
ALTER TABLE dbo.MigrantCase
  ADD CONSTRAINT DF_MigrantCa_rowgu_4D628F63       DEFAULT NEWID()           FOR rowguid;

ALTER TABLE dbo.MigrantCase
  ADD CONSTRAINT DF_MigrantCase_IsRevoke            DEFAULT (0)               FOR IsRevoke;

ALTER TABLE dbo.MigrantCase
  ADD CONSTRAINT DF_MigrantCase_IsUnaccompaniedMinor DEFAULT (0)              FOR IsUnaccompaniedMinor;

ALTER TABLE dbo.MigrantCase
  ADD CONSTRAINT DF_MigrantCase_LastDateModified    DEFAULT (SYSUTCDATETIME()) FOR LastDateModified;

ALTER TABLE dbo.MigrantCase
  ADD CONSTRAINT DF_MigrantCase_trigger_timestamp   DEFAULT (SYSUTCDATETIME()) FOR trigger_timestamp;
GO
