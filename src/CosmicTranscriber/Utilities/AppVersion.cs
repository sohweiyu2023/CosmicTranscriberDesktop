using System.Reflection;

namespace CosmicTranscriber.Utilities;

public static class AppVersion
{
    public static string DisplayVersion { get; } = ResolveDisplayVersion();

    private static string ResolveDisplayVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadataSeparator = informational.IndexOf('+');
            return metadataSeparator >= 0
                ? informational[..metadataSeparator]
                : informational;
        }

        var version = assembly.GetName().Version;
        if (version is null)
        {
            return "unknown";
        }

        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
    }
}
