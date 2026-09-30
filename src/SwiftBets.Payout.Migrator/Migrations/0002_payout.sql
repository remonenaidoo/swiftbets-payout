CREATE TABLE payout.CouponPayouts
(
    CouponId    uniqueidentifier  NOT NULL CONSTRAINT PK_CouponPayouts PRIMARY KEY,
    PunterId    uniqueidentifier  NOT NULL,
    PaidToDate  bigint            NOT NULL CONSTRAINT DF_CouponPayouts_Paid DEFAULT (0),
    LastVersion int               NOT NULL CONSTRAINT DF_CouponPayouts_Version DEFAULT (0),
    LeaseHolder char(32)          NULL,
    LeaseUntil  datetimeoffset(3) NULL
);

CREATE TABLE payout.Payments
(
    CouponId       uniqueidentifier  NOT NULL,
    Version        int               NOT NULL,
    TargetPayout   bigint            NOT NULL,
    Delta          bigint            NOT NULL,
    IdempotencyKey nvarchar(200)     NOT NULL CONSTRAINT UQ_Payments_IdempotencyKey UNIQUE,
    RecordedAt     datetimeoffset(3) NOT NULL,
    CONSTRAINT PK_Payments PRIMARY KEY (CouponId, Version)
);

CREATE TABLE payout.DeadLetters
(
    CouponId    uniqueidentifier  NOT NULL,
    Version     int               NOT NULL,
    AttemptJson nvarchar(max)     NOT NULL,
    Reason      nvarchar(500)     NOT NULL,
    ParkedAt    datetimeoffset(3) NOT NULL,
    CONSTRAINT PK_DeadLetters PRIMARY KEY (CouponId, Version)
);
