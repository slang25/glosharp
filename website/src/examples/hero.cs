var sensors = new[]
{
    new { Name = "North Tower", Readings = new[] { 18.2, 17.5, 19.1 } },
    new { Name = "South Tower", Readings = new[] { 22.4, 23.1, 21.8 } },
};

var warmest = sensors
    .Select(s => (s.Name, Avg: s.Readings.Average()))
    .MaxBy(x => x.Avg);
//  ^?

// @annotate: Compiled and type-checked — stale docs break the build
// @highlight
Console.WriteLine($"{warmest.Name} — {warmest.Avg:F1}°C");
