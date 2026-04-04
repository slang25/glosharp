---
sidebar_position: 2
---

# Type Inference

C# uses `var` to infer types at compile time. GloSharp shows you exactly what the
compiler resolves.

## Basic Inference

```csharp
// @noErrors
var name = "Ada Lovelace";
//   ^?
var year = 1843;
//   ^?
var pi = 3.14159;
//  ^?
```

## Collection Inference

Generic types are fully resolved:

```csharp
// @noErrors
var words = new List<string> { "async", "await", "yield" };
//   ^?
var lookup = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 };
//    ^?
var first = words.First();
//   ^?
```

## Anonymous Types

Anonymous types get their shape inferred:

```csharp
// @noErrors
var point = new { X = 10, Y = 20 };
//   ^?
var distance = Math.Sqrt(point.X * point.X + point.Y * point.Y);
//    ^?
```
