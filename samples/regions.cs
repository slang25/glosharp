using System.Collections.Generic;

// This file demonstrates region extraction.
// Use --region getting-started to extract just the region below.

#region getting-started
var names = new List<string> { "Alice", "Bob", "Charlie" };
foreach (var name in names)
//          ^?
{
    Console.WriteLine($"Hello, {name}!");
    //               ^?
}
#endregion

#region advanced
var dict = new Dictionary<string, int>
//  ^?
{
    ["one"] = 1,
    ["two"] = 2,
};
#endregion
