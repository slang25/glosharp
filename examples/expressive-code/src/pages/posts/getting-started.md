---
layout: ../../layouts/Layout.astro
title: Getting Started with C#
---

# Getting Started with C#

Hover over any token to see its type, powered by **Glo#** and the Roslyn compiler, rendered with **Expressive Code**.

## Variables and Type Inference

The `var` keyword lets C# infer the type for you. A `// ^?` line keeps the hover
for the token above the caret open:

```csharp
var greeting = "Hello, World!";
//  ^?
var count = 42;
Console.WriteLine($"{greeting} {count}");
```

## Working with Collections

LINQ makes working with collections a breeze:

```csharp
var numbers = new List<int> { 1, 2, 3, 4, 5 };
var sum = numbers.Sum();
var evens = numbers.Where(n => n % 2 == 0).ToList();
```

## Error Handling

Glo# can also show compile errors. `// @errors:` declares the errors you expect
on the next line, so `glosharp verify` treats them as intended:

```csharp
// @errors: CS0103
Console.WriteLine(undeclared);
```
