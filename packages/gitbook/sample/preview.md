# Local preview

Anything in a `glosharp` fence is rendered by the block. Edit this file and
reload the preview to see it change.

```glosharp
var greeting = "Hello, Glo#";
//  ^?
Console.WriteLine(greeting.Length);
//                         ^?
```

Compiler diagnostics come through too:

```glosharp
// @errors: CS0029
int count = "not a number";
```

XML documentation shows up in the hover, which is the case that needs the most
room — the frame grows to fit it:

```glosharp
var total = PriceMath.ApplyDiscount(100m, 0.2m);
//                    ^?

/// <summary>Pricing helpers for order calculations.</summary>
static class PriceMath
{
    /// <summary>Computes the discounted price for an order line.</summary>
    /// <param name="price">Original unit price.</param>
    /// <param name="discount">Discount fraction between 0 and 1.</param>
    /// <returns>The price after applying the discount.</returns>
    public static decimal ApplyDiscount(decimal price, decimal discount) =>
        price * (1 - discount);
}
```

A plain `csharp` fence is left alone by the integration:

```csharp
var untouched = true;
```
