UPDATE payout.CouponPayouts SET LeaseHolder = NULL, LeaseUntil = NULL WHERE CouponId = @CouponId AND LeaseHolder = @Holder;
