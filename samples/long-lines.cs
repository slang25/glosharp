// @noErrors
var connectionString = "Server=localhost;Database=Products;Trusted_Connection=True;MultipleActiveResultSets=true;Encrypt=False;Application Name=GloSharpGalleryLongLineSample";
var parsedConnectionSettings = connectionString.Split(';').Select(part => part.Trim()).Where(part => part.Length > 0).Select(part => part.Split('=')).ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : string.Empty);
//   ^?
Console.WriteLine(parsedConnectionSettings.Count is int veryDescriptiveCountName && veryDescriptiveCountName > 0 ? $"parsed {veryDescriptiveCountName} settings from the configuration value successfully" : "nothing parsed");
//                                                       ^?
