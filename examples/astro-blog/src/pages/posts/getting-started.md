---
layout: ../../layouts/Layout.astro
title: Getting Started with C#
---

# Getting Started with C#

Hover over any token to see its type, powered by **Glo#** and the Roslyn compiler.

## Variables and Type Inference

The `var` keyword lets C# infer the type for you. A `// ^?` line pins the hover
for the token above the caret:

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
//                ^?
var evens = numbers.Where(n => n % 2 == 0).ToList();
```

## Error Handling

Glo# can also show compile errors inline. `// @errors:` declares the errors you
expect on the next line, so `glosharp verify` treats them as intended:

```csharp
// @errors: CS0103
Console.WriteLine(undeclared);
```

## The Same Snippet Twice

Identical blocks share one result, so repeating a snippet costs nothing extra
and both copies get hovers:

```csharp
var greeting = "Hello, World!";
//  ^?
var count = 42;
Console.WriteLine($"{greeting} {count}");
```
