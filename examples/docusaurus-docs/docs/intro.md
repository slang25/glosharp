---
slug: /
sidebar_position: 1
---

# Introduction

Welcome to the docs! Code samples here use **twohash** to show type information
inline — hover over dotted tokens to see their types.

## Quick Example

```csharp
// @noErrors
var message = "Hello from the docs!";
//    ^?
Console.WriteLine(message);
//        ^?
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later
- The `twohash` CLI: `dotnet tool install -g twohash`
