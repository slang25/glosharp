---
slug: /
sidebar_position: 1
---

# Introduction

Welcome to the docs! Code samples here use **Glo#** to show type information
inline. Hover over any token to see its type.

## Quick Example

```csharp
var message = "Hello from the docs!";
//    ^?
Console.WriteLine(message);
//        ^?
```

## Prerequisites

- The [.NET SDK](https://dotnet.microsoft.com/download) (8 or later; 10 or later
  for `#:package` snippets)
- The `glosharp` tool: `dotnet tool install --global GloSharp.Cli --prerelease`
