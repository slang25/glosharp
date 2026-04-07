// @noErrors
var scores = new[] { 92, 85, 78, 96, 88 };

var stats = (
    Mean: scores.Average(),
    High: scores.Max(),
    Low: scores.Min()
);
//  ^?

// @highlight
Console.WriteLine($"Average: {stats.Mean:F1}, range {stats.Low}–{stats.High}");
