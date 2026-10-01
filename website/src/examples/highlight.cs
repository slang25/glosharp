var settings = new Dictionary<string, string> { ["Env"] = "prod" };
var env = settings["Env"];
// @highlight
Console.WriteLine($"Running in {env}");
