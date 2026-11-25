using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SharpCompute.Build;

/// <summary>
/// The Metal compiler and linker of the Xcode Metal Toolchain, run through <c>xcrun</c>
/// (<c>xcrun metal</c>, <c>xcrun metallib</c>), which finds whichever toolchain is selected.
/// </summary>
/// <param name="xcrun">The <c>xcrun</c> to run: a name on the PATH or a path.</param>
internal sealed class MetalToolchain(string xcrun)
{
    /// <summary>
    /// The deployment target of every precompiled library: the oldest macOS SharpCompute runs on.
    /// Without it the compiler targets the machine it runs on, and a library built on a newer macOS
    /// would not load on an older one.
    /// </summary>
    internal const string DeploymentTarget = "15.0";

    /// <summary>
    /// Null when <c>xcrun metal</c> runs; otherwise why it does not: there is no <c>xcrun</c> (not a
    /// Mac, or no developer tools), or Xcode is there without the Metal Toolchain component.
    /// </summary>
    public string? FindProblem()
    {
        ToolResult result = Run(["metal", "--version"], workingDirectory: null);

        return result.ExitCode == 0
            ? null
            : result.Output.Length == 0 ? $"'{xcrun} metal --version' exited with code {result.ExitCode}" : FirstLine(result.Output);
    }

    /// <summary>
    /// Compiles <paramref name="sourceFile"/> to AIR with the options the runtime compiles the same
    /// shader with, and with its kernel named <paramref name="functionName"/> instead of <paramref name="entryPoint"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runtime's <c>MTLCompileOptions</c> are MSL 3.2, <c>mathMode</c> and
    /// <c>mathFloatingPointFunctions</c>: safe and precise, or both fast for
    /// <c>ShaderCompileOptions.FastMath</c>. Their compiler flags are <c>-std=metal3.2</c>,
    /// <c>-fmetal-math-mode</c> and <c>-fmetal-math-fp32-functions</c>. (The compiler's own default is
    /// fast for both, so they are always passed.) <c>ShaderCompileOptions.NoFpContract</c> is not a
    /// flag here: as at run time, it is a pragma in the text being compiled.
    /// </para>
    /// <para>
    /// The kernel is renamed with a macro. The text that is compiled stays the shader's MSL to the
    /// character; only the one identifier differs in the result, and a name has no part in the code.
    /// </para>
    /// </remarks>
    public ToolResult Compile(string sourceFile, string outputFile, string entryPoint, string functionName, int compileOptions, string workingDirectory)
    {
        bool fastMath = (compileOptions & PrecompiledShaderContract.FastMath) != 0;

        return Run(
            [
                "metal",
                "-std=metal3.2",
                "-mmacosx-version-min=" + DeploymentTarget,
                "-fmetal-math-mode=" + (fastMath ? "fast" : "safe"),
                "-fmetal-math-fp32-functions=" + (fastMath ? "fast" : "precise"),
                "-D" + entryPoint + "=" + functionName,
                "-c",
                sourceFile,
                "-o",
                outputFile,
            ],
            workingDirectory);
    }

    /// <summary>Links AIR files into one library. Every kernel in them stays a function of its own.</summary>
    public ToolResult Link(IEnumerable<string> airFiles, string outputFile, string workingDirectory)
    {
        List<string> arguments = ["metallib"];
        arguments.AddRange(airFiles);
        arguments.Add("-o");
        arguments.Add(outputFile);

        return Run(arguments, workingDirectory);
    }

    private ToolResult Run(IReadOnlyList<string> arguments, string? workingDirectory)
    {
        ProcessStartInfo info = new(xcrun)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (workingDirectory is not null)
        {
            info.WorkingDirectory = workingDirectory;
        }

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process? process;

        try
        {
            process = Process.Start(info);
        }
        catch (Win32Exception)
        {
            // No such program: reported like any other reason the toolchain cannot be used.
            return new ToolResult(-1, $"'{xcrun}' could not be started");
        }

        if (process is null)
        {
            return new ToolResult(-1, $"'{xcrun}' could not be started");
        }

        using (process)
        {
            // Both pipes are drained while the process runs; reading one after the other can deadlock
            // once the other's buffer is full.
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            return new ToolResult(process.ExitCode, (standardError.Result + standardOutput.Result).Trim());
        }
    }

    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n');
        return (end < 0 ? text : text[..end]).Trim();
    }
}

/// <summary>How a tool ended and everything it printed (standard error, then standard output).</summary>
internal readonly record struct ToolResult(int ExitCode, string Output);
