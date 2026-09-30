UPDATE payout.CouponPayouts
SET LeaseHolder = @Holder, LeaseUntil = @Until
OUTPUT inserted.PaidToDate, inserted.LastVersion
WHERE CouponId = @CouponId AND (LeaseUntil IS NULL OR LeaseUntil < @Now);
