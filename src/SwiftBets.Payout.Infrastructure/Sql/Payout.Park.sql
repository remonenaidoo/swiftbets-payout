INSERT INTO payout.DeadLetters (CouponId, Version, AttemptJson, Reason, ParkedAt)
SELECT @CouponId, @Version, @AttemptJson, @Reason, @Now
WHERE NOT EXISTS (SELECT 1 FROM payout.DeadLetters WHERE CouponId = @CouponId AND Version = @Version);
SELECT @@ROWCOUNT;
