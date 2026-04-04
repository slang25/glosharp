---
layout: ../../layouts/Layout.astro
title: Getting Started with C#
---

# Getting Started with C#

Hover over any underlined token to see its type — powered by **glosharp** and the Roslyn compiler.

## Variables and Type Inference

The `var` keyword lets C# infer the type for you:

```csharp
// @noErrors
var greeting = "Hello, World!";
var count = 42;
Console.WriteLine($"{greeting} {count}");
```

## Working with Collections

LINQ makes working with collections a breeze:

```csharp
// @noErrors
var numbers = new List<int> { 1, 2, 3, 4, 5 };
var sum = numbers.Sum();
var evens = numbers.Where(n => n % 2 == 0).ToList();
```

## Error Handling

GloSharp can also show compile errors inline. Here's what happens when you use an undeclared variable:

```csharp
// @errors: CS0103
Console.WriteLine(undeclared);
```
