using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SharpCompute.Build.Tests;

/// <summary>A <see cref="FactAttribute"/> for tests that run the Metal compiler. Skipped when <c>xcrun metal</c> does not run on this machine.</summary>
public sealed class ToolchainFactAttribute : FactAttribute
{
    internal const string SkipReason = "The Xcode Metal Toolchain is not installed ('xcrun metal' does not run).";

    private static readonly Lazy<bool> s_isAvailable = new(static () => new MetalToolchain("xcrun").FindProblem() is null);

    public ToolchainFactAttribute()
    {
        if (!IsAvailable)
        {
            Skip = SkipReason;
        }
    }

    internal static bool IsAvailable => s_isAvailable.Value;
}

/// <summary>A directory under the temporary folder that is deleted with the test.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sharpcompute-build-tests-" + Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

/// <summary>Runs a program and collects what it prints.</summary>
internal static class Tool
{
    public static (int ExitCode, string Output) Run(string fileName, string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo info = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // A build started from a test run must find its own MSBuild, not the one `dotnet test` is in
        // the middle of using, which the test host inherits in these variables.
        foreach (string variable in info.Environment.Keys.Where(static key => key.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            info.Environment.Remove(variable);
        }

        using Process process = Process.Start(info) ?? throw new InvalidOperationException($"'{fileName}' could not be started.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"'{fileName} {string.Join(' ', arguments)}' did not finish in five minutes.");
        }

        process.WaitForExit();
        return (process.ExitCode, standardOutput.Result + standardError.Result);
    }

    /// <summary>The <c>dotnet</c> that runs these tests.</summary>
    public static string DotNet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && System.IO.File.Exists(host)
        ? host
        : Environment.ProcessPath is { } process && Path.GetFileNameWithoutExtension(process) == "dotnet" ? process : "dotnet";

    /// <summary>The names of the functions in a Metal library, as <c>xcrun metal-nm</c> lists them.</summary>
    public static IReadOnlyList<string> FunctionNames(string library)
    {
        (int exitCode, string output) = Run("xcrun", Path.GetDirectoryName(library)!, "metal-nm", library);
        Assert.True(exitCode == 0, output);

        // "0000005c T Name"
        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(static parts => parts.Length == 3 && parts[1] == "T")
            .Select(static parts => parts[2])
            .Order(StringComparer.Ordinal)];
    }
}
