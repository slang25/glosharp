// @noErrors
var numbers = Enumerable.Range(1, 10).ToArray();
// @focus
var evens = numbers.Where(n => n % 2 == 0).ToArray();
Console.WriteLine(string.Join(", ", evens));
