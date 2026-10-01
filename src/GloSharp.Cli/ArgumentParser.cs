using System.Text;

namespace GloSharp.Cli;

/// <summary>A command-line usage mistake. Reported with the command's usage line; exit code 2.</summary>
internal sealed class UsageException(string message) : Exception(message);

internal sealed record OptionSpec(string Name, string Description, string? ValueName = null, string? Alias = null)
{
    public bool TakesValue => ValueName != null;
}

internal sealed record CommandSpec
{
    public required string Name { get; init; }
    public required string Summary { get; init; }

    /// <summary>Synopsis after "glosharp &lt;name&gt;", e.g. "[&lt;file&gt;] [options]".</summary>
    public required string Synopsis { get; init; }

    public required IReadOnlyList<OptionSpec> Options { get; init; }
    public int MinPositionals { get; init; }
    public int MaxPositionals { get; init; }

    /// <summary>Extra help text printed after the options.</summary>
    public string? Notes { get; init; }

    public OptionSpec? Find(string token) =>
        Options.FirstOrDefault(o => o.Name == token || o.Alias == token);
}

internal sealed class ParsedCommand
{
    public required CommandSpec Spec { get; init; }
    public bool HelpRequested { get; init; }
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);
    public List<string> Positionals { get; } = [];

    public string? Get(string name) => Values.TryGetValue(name, out var v) ? v : null;
    public bool Has(string name) => Flags.Contains(name) || Values.ContainsKey(name);
}

/// <summary>
/// The one argument parser for every glosharp command: rejects unknown options (with a
/// "did you mean" hint), options missing their value and surplus positionals, instead of
/// silently ignoring them. Supports <c>--name value</c>, <c>--name=value</c> and <c>--</c>.
/// </summary>
internal static class ArgumentParser
{
    public static ParsedCommand Parse(CommandSpec spec, IReadOnlyList<string> args)
    {
        // --help wins over everything else (so "process --help" never reads stdin and
        // "init --help" never writes a file), but only before a "--" terminator.
        var terminator = IndexOf(args, "--");
        var scanEnd = terminator < 0 ? args.Count : terminator;
        for (var i = 0; i < scanEnd; i++)
        {
            if (args[i] is "--help" or "-h" or "-?")
                return new ParsedCommand { Spec = spec, HelpRequested = true };
        }

        var parsed = new ParsedCommand { Spec = spec };
        var onlyPositionals = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!onlyPositionals && arg == "--")
            {
                onlyPositionals = true;
                continue;
            }

            if (!onlyPositionals && arg.Length > 1 && arg[0] == '-')
            {
                string name = arg;
                string? inlineValue = null;
                var eq = arg.IndexOf('=');
                if (arg.StartsWith("--", StringComparison.Ordinal) && eq > 2)
                {
                    name = arg[..eq];
                    inlineValue = arg[(eq + 1)..];
                }

                var option = spec.Find(name) ?? throw new UsageException(UnknownOptionMessage(spec, arg, name));

                if (!option.TakesValue)
                {
                    if (inlineValue != null)
                        throw new UsageException($"option '{option.Name}' does not take a value.");
                    parsed.Flags.Add(option.Name);
                    continue;
                }

                string value;
                if (inlineValue != null)
                {
                    value = inlineValue;
                }
                else if (i + 1 < args.Count && !LooksLikeOption(args[i + 1]))
                {
                    value = args[++i];
                }
                else
                {
                    throw new UsageException($"option '{name}' requires a value: {name} <{option.ValueName}>.");
                }

                if (value.Length == 0)
                    throw new UsageException($"option '{name}' requires a non-empty value.");
                parsed.Values[option.Name] = value;
                continue;
            }

            if (parsed.Positionals.Count >= spec.MaxPositionals)
            {
                throw new UsageException(spec.MaxPositionals == 0
                    ? $"unexpected argument '{arg}'; '{spec.Name}' takes no positional arguments."
                    : $"unexpected argument '{arg}'.");
            }
            parsed.Positionals.Add(arg);
        }

        if (parsed.Positionals.Count < spec.MinPositionals)
            throw new UsageException($"missing required argument: glosharp {spec.Name} {spec.Synopsis}");

        return parsed;
    }

    private static bool LooksLikeOption(string arg) => arg.Length > 1 && arg[0] == '-';

    private static int IndexOf(IReadOnlyList<string> args, string value)
    {
        for (var i = 0; i < args.Count; i++)
            if (args[i] == value) return i;
        return -1;
    }

    private static string UnknownOptionMessage(CommandSpec spec, string arg, string name)
    {
        var message = new StringBuilder($"unknown option '{arg}'.");
        if (arg.Contains(' '))
        {
            message.Append(" (An option and its value were passed as a single argument; pass them separately.)");
            return message.ToString();
        }

        var suggestion = spec.Options
            .Select(o => (o.Name, Distance: Levenshtein(name, o.Name)))
            .Where(t => t.Distance <= 2)
            .OrderBy(t => t.Distance)
            .Select(t => t.Name)
            .FirstOrDefault();
        if (suggestion != null)
            message.Append($" Did you mean '{suggestion}'?");
        return message.ToString();
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
        }
        return d[a.Length, b.Length];
    }

    public static string FormatHelp(CommandSpec spec)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Usage: glosharp {spec.Name} {spec.Synopsis}");
        sb.AppendLine();
        sb.AppendLine(spec.Summary);
        if (spec.Options.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Options:");
            var rows = spec.Options
                .Select(o => (Left: (o.Alias != null ? $"{o.Alias}, " : "") + o.Name + (o.TakesValue ? $" <{o.ValueName}>" : ""), o.Description))
                .ToList();
            var width = Math.Max(rows.Max(r => r.Left.Length), "-h, --help".Length) + 2;
            foreach (var (left, description) in rows)
                sb.AppendLine($"  {left.PadRight(width)}{description}");
            sb.AppendLine($"  {"-h, --help".PadRight(width)}Show this help");
        }
        if (spec.Notes != null)
        {
            sb.AppendLine();
            sb.Append(spec.Notes.TrimEnd());
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
