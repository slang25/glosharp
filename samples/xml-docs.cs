// @noErrors
var total = PriceMath.ApplyDiscount(100m, 0.2m);
//                       ^?

/// <summary>Pricing helpers for order calculations.</summary>
static class PriceMath
{
    /// <summary>Computes the discounted price for an order line.</summary>
    /// <param name="price">Original unit price.</param>
    /// <param name="discount">Discount fraction between 0 and 1.</param>
    /// <returns>The price after applying the discount.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the discount is negative.</exception>
    public static decimal ApplyDiscount(decimal price, decimal discount) =>
        discount < 0
            ? throw new ArgumentOutOfRangeException(nameof(discount))
            : price * (1 - discount);
}
