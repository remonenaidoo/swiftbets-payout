DELETE FROM payout.DeadLetters OUTPUT deleted.AttemptJson WHERE CouponId = @CouponId AND Version = @Version;
