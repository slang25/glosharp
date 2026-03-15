---
sidebar_position: 3
---

# Error Handling

Twohash can display compiler errors inline — useful for showing what _not_ to do.

## Expected Errors

Use `// @errors: CS0103` to mark errors as intentional. They render inline without
failing your build:

```csharp
// @errors: CS0103
Console.WriteLine(oops);
```

## Nullable Warnings

Show nullable reference type warnings:

```csharp
// @noErrors
// @nullable: enable
string? name = null;
//      ^?
string definite = "hello";
//       ^?
```

## Completions

Use `^|` to show IntelliSense completions at a position:

```csharp
Console.
//      ^|
```
