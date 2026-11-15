using System.Reflection;

namespace RagEval.Core;

/// <summary>Version information shared by the API and the CLI.</summary>
public static class BuildInfo
{
    /// <summary>The informational version of the build, without source revision metadata.</summary>
    public static string Version { get; } =
        (typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];
}
