using System.Reflection;

namespace TwoHash.Core;

internal static class VersionInfo
{
    public static string GetVersion()
    {
        var assembly = typeof(VersionInfo).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        return version;
    }
}
