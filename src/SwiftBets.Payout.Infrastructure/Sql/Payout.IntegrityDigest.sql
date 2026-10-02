SELECT CouponId, PaidToDate, LastVersion
FROM payout.CouponPayouts
WHERE CouponId IN @Ids;
