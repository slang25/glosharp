---
sidebar_position: 3
---

# Error Handling

Glo# can display compiler errors inline, which is useful for showing what _not_ to do.

## Expected Errors

Use `// @errors: CS0103` to declare the errors you expect on the next line. They
still render inline, and `glosharp verify` treats them as intended instead of
failing:

```csharp
// @errors: CS0103
Console.WriteLine(oops);
```

## Nullable Warnings

Nullable reference types are enabled by default, so the compiler's nullable
analysis shows up in hovers and warnings:

```csharp
string? name = null;
//      ^?
string definite = name ?? "hello";
//       ^?
Console.WriteLine(definite);
```

## Completions

Put `^|` under a position to show the completion list there:

```csharp
var message = "Hello";
Console.WriteLine(message.ToUpper());
//                        ^|
```
