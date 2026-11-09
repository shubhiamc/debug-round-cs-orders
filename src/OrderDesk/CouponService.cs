using System;

namespace OrderDesk;

public sealed record CouponLookup(Coupon? Coupon, string? Error);

public sealed class CouponService
{
    private readonly Store _store;
    private readonly TimeProvider _time;

    public CouponService(Store store, TimeProvider time)
    {
        _store = store;
        _time = time;
    }

    /// <summary>No code at all is fine (no discount). An unknown or expired code is an error.</summary>
    public CouponLookup Find(string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode)) return new CouponLookup(null, null);

        var code = TextUtils.NormalizeCode(rawCode);
        var coupon = TextUtils.FindByKey(_store.Coupons, c => c.Code, code);
        if (coupon is null) return new CouponLookup(null, $"Unknown coupon '{code}'.");

        if (_time.GetLocalNow().DateTime > coupon.ExpiresAtUtc)
            return new CouponLookup(null, $"Coupon '{code}' has expired.");

        return new CouponLookup(coupon, null);
    }

    /// <summary>Discount in currency for the given base amount, honouring the coupon's cap.</summary>
    public decimal DiscountFor(Coupon coupon, decimal baseAmount)
    {
        var discount = Money.Round(baseAmount * coupon.Percent);
        if (coupon.MaxDiscount is decimal cap && discount > cap) discount = cap;
        return Math.Min(discount, baseAmount);
    }
}
