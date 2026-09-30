SELECT CouponId, PaidToDate, LastVersion, CAST(CASE WHEN LeaseUntil > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsLeased FROM payout.CouponPayouts WHERE CouponId = @CouponId;
SELECT Version, TargetPayout, Delta, IdempotencyKey, RecordedAt FROM payout.Payments WHERE CouponId = @CouponId ORDER BY Version;
SELECT Reason FROM payout.DeadLetters WHERE CouponId = @CouponId;
