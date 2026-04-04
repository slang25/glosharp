using System.Text.RegularExpressions;

namespace GloSharp.Core;

public enum FileDirectiveType
{
    Package,
    Sdk,
    Property,
    Project,
}

public record FileDirective(FileDirectiveType Type, string Name, string? Value);

public class FileDirectiveResult
{
    public required string CleanedSource { get; init; }
    public required string OriginalSource { get; init; }
    public required List<FileDirective> Directives { get; init; }
    public required int DirectiveLinesRemoved { get; init; }

    public bool HasDirectives => Directives.Count > 0;

    public List<PackageReference> GetPackageReferences() =>
        Directives
            .Where(d => d.Type == FileDirectiveType.Package)
            .Select(d => new PackageReference { Name = d.Name, Version = d.Value ?? "*" })
            .ToList();

    public string? GetSdk() =>
        Directives.FirstOrDefault(d => d.Type == FileDirectiveType.Sdk)?.Name;

    public string? GetProperty(string key) =>
        Directives.FirstOrDefault(d => d.Type == FileDirectiveType.Property && d.Name == key)?.Value;
}

public static partial class FileDirectiveParser
{
    private static readonly Regex DirectiveLineRegex = DirectiveLinePattern();

    [GeneratedRegex(@"^#:(\w+)\s+(.+)$")]
    private static partial Regex DirectiveLinePattern();

    public static FileDirectiveResult Parse(string source)
    {
        var lines = source.Split('\n');
        var cleanedLines = new List<string>();
        var directives = new List<FileDirective>();
        var directiveLinesRemoved = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            var match = DirectiveLineRegex.Match(trimmed);
            if (match.Success)
            {
                var directiveType = match.Groups[1].Value;
                var directiveArg = match.Groups[2].Value.Trim();
                var directive = ParseDirective(directiveType, directiveArg);
                if (directive != null)
                {
                    directives.Add(directive);
                    directiveLinesRemoved++;
                    continue;
                }
            }

            cleanedLines.Add(line);
        }

        return new FileDirectiveResult
        {
            CleanedSource = string.Join('\n', cleanedLines),
            OriginalSource = source,
            Directives = directives,
            DirectiveLinesRemoved = directiveLinesRemoved,
        };
    }

    public static bool HasDirectives(string source)
    {
        var lines = source.Split('\n');
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("#:"))
                return true;
        }
        return false;
    }

    private static FileDirective? ParseDirective(string type, string arg)
    {
        return type.ToLowerInvariant() switch
        {
            "package" => ParsePackageDirective(arg),
            "sdk" => new FileDirective(FileDirectiveType.Sdk, arg, null),
            "property" => ParsePropertyDirective(arg),
            "project" => new FileDirective(FileDirectiveType.Project, arg, null),
            _ => null, // Unknown directive type — skip
        };
    }

    private static FileDirective ParsePackageDirective(string arg)
    {
        var atIndex = arg.IndexOf('@');
        if (atIndex > 0)
        {
            var name = arg[..atIndex];
            var version = arg[(atIndex + 1)..];
            return new FileDirective(FileDirectiveType.Package, name, version);
        }
        return new FileDirective(FileDirectiveType.Package, arg, null);
    }

    private static FileDirective? ParsePropertyDirective(string arg)
    {
        var eqIndex = arg.IndexOf('=');
        if (eqIndex <= 0) return null;

        var key = arg[..eqIndex];
        var value = arg[(eqIndex + 1)..];
        return new FileDirective(FileDirectiveType.Property, key, value);
    }
}
