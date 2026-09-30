INSERT INTO payout.CouponPayouts (CouponId, PunterId)
SELECT @CouponId, @PunterId
WHERE NOT EXISTS (SELECT 1 FROM payout.CouponPayouts WHERE CouponId = @CouponId);
