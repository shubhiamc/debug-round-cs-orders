using System;
using System.Collections.Generic;
using System.Linq;

namespace OrderDesk;

public static class TextUtils
{
    /// <summary>Coupon codes are case-insensitive and users love to paste trailing spaces.</summary>
    public static string NormalizeCode(string? raw) => (raw ?? "").Trim().ToUpperInvariant();

    /// <summary>Finds the first item whose key equals <paramref name="key"/>.</summary>
    public static T? FindByKey<T>(IEnumerable<T> items, Func<T, object> keySelector, object key) where T : class =>
        items.FirstOrDefault(item => Equals(keySelector(item), key));
}
