using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SharpCompute.Tests;

/// <summary>
/// The Xcode Metal Toolchain (<c>xcrun metal</c>, <c>xcrun metallib</c>), for the tests that build a
/// library by hand to put a particular one in front of the runtime: one built from another version of a
/// shader's source, or with other options. Deliberately not the build tool, so that the two agree by
/// the contract they share and not because one calls the other.
/// </summary>
internal static class MetalToolchain
{
    private static readonly Lazy<bool> s_isAvailable = new(static () =>
    {
        try
        {
            return Run(Path.GetTempPath(), "metal", "--version").ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    });

    /// <summary>True when <c>xcrun metal</c> runs on this machine.</summary>
    public static bool IsAvailable => s_isAvailable.Value;

    /// <summary>
    /// Compiles each kernel the way the runtime would compile it from source, stores it under
    /// <see cref="Kernel.FunctionName"/> and links them into <c>library.metallib</c> in <paramref name="directory"/>.
    /// </summary>
    public static string BuildLibrary(string directory, params Kernel[] kernels)
    {
        Directory.CreateDirectory(directory);
        List<string> arguments = ["metallib"];

        for (int i = 0; i < kernels.Length; i++)
        {
            Kernel kernel = kernels[i];
            bool fastMath = (kernel.Options & ShaderCompileOptions.FastMath) != 0;
            string source = $"kernel{i}.metal", air = $"kernel{i}.air";

            File.WriteAllText(Path.Combine(directory, source), PrecompiledShaderContract.CompiledSource(kernel.Source, (int)kernel.Options));

            (int exitCode, string output) = Run(
                directory,
                "metal",
                "-std=metal3.2",
                "-mmacosx-version-min=15.0",
                "-fmetal-math-mode=" + (fastMath ? "fast" : "safe"),
                "-fmetal-math-fp32-functions=" + (fastMath ? "fast" : "precise"),
                "-D" + kernel.EntryPoint + "=" + kernel.FunctionName,
                "-c",
                source,
                "-o",
                air);

            Assert.True(exitCode == 0, $"xcrun metal failed for '{kernel.EntryPoint}':\n{output}");
            arguments.Add(air);
        }

        arguments.Add("-o");
        arguments.Add("library.metallib");

        (int linkExitCode, string linkOutput) = Run(directory, [.. arguments]);
        Assert.True(linkExitCode == 0, $"xcrun metallib failed:\n{linkOutput}");

        return Path.Combine(directory, "library.metallib");
    }

    private static (int ExitCode, string Output) Run(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo info = new("xcrun")
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

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("xcrun could not be started.");
        System.Threading.Tasks.Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, standardError + standardOutput.Result);
    }

    /// <summary>One kernel of a hand-built library: the source to compile and the name to store it under.</summary>
    internal readonly record struct Kernel(string EntryPoint, string FunctionName, string Source, ShaderCompileOptions Options)
    {
        /// <summary>The kernel exactly as the build precompiles <typeparamref name="T"/>.</summary>
        public static Kernel Of<T>()
            where T : struct, IComputeShaderDescriptor<T> =>
            new(T.EntryPoint, PrecompiledShaderContract.FunctionName(T.EntryPoint, T.MslSource, (int)T.CompileOptions), T.MslSource, T.CompileOptions);
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> for tests that run a shader of this assembly from its precompiled
/// library. Skipped without a Metal device, and when this build made no library: the Metal toolchain
/// is not installed, or the suite was built with <c>SharpComputePrecompileShaders=false</c>.
/// </summary>
public sealed class PrecompiledFactAttribute : FactAttribute
{
    internal const string SkipReason =
        "This build has no precompiled shader library (the Metal toolchain is not installed, or SharpComputePrecompileShaders is false).";

    public PrecompiledFactAttribute()
    {
        if (!GraphicsDevice.IsSupported)
        {
            Skip = MetalFactAttribute.SkipReason;
        }
        else if (!HasLibrary)
        {
            Skip = SkipReason;
        }
    }

    /// <summary>The library the build puts next to this assembly.</summary>
    internal static string LibraryPath { get; } = Path.Combine(
        Path.GetDirectoryName(typeof(PrecompiledFactAttribute).Assembly.Location)!,
        typeof(PrecompiledFactAttribute).Assembly.GetName().Name + PrecompiledShaderContract.LibraryExtension);

    internal static bool HasLibrary => File.Exists(LibraryPath);
}

/// <summary>A <see cref="FactAttribute"/> for tests that build a Metal library themselves. Skipped without a Metal device or without the Metal toolchain.</summary>
public sealed class MetalToolchainFactAttribute : FactAttribute
{
    public MetalToolchainFactAttribute()
    {
        if (!GraphicsDevice.IsSupported)
        {
            Skip = MetalFactAttribute.SkipReason;
        }
        else if (!MetalToolchain.IsAvailable)
        {
            Skip = "The Xcode Metal Toolchain is not installed ('xcrun metal' does not run).";
        }
    }
}
