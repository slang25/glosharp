// @noErrors
var numbers = new List<int> { 1, 2, 3, 4, 5 };
//    ^?
var sum = numbers.Sum();
//  ^?
Console.WriteLine(sum);
//        ^?
