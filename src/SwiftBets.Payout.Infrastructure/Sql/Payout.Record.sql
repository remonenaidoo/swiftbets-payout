UPDATE payout.CouponPayouts
SET PaidToDate = PaidToDate + @Delta, LastVersion = @Version
WHERE CouponId = @CouponId AND LastVersion < @Version;
IF @@ROWCOUNT = 1
BEGIN
    INSERT INTO payout.Payments (CouponId, Version, TargetPayout, Delta, IdempotencyKey, RecordedAt)
    VALUES (@CouponId, @Version, @TargetPayout, @Delta, @IdempotencyKey, @Now);
    SELECT 1;
END
ELSE
    SELECT 0;
