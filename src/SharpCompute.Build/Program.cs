using System;
using System.Collections.Generic;
using System.Globalization;
using SharpCompute.Build;

// SharpCompute.Build: precompiles the shaders of an assembly into a Metal library.
//
//   dotnet exec SharpCompute.Build.dll --assembly <dll> --output <metallib> --intermediate <dir>
//                                      [--state <file>] [--xcrun <path>] [--parallelism <n>]
//
// Run by SharpCompute.Build.targets after the compiler. Exits 0 whenever the outcome is one the
// build should carry on from, which includes "no toolchain, so no library": only a mistake in the
// arguments (2) or a failure nobody planned for (1) is an error.
Dictionary<string, string> options = new(StringComparer.Ordinal);

for (int i = 0; i < args.Length; i += 2)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
    {
        return Usage($"unexpected argument '{args[i]}'");
    }

    options[args[i][2..]] = args[i + 1];
}

if (!options.TryGetValue("assembly", out string? assembly) || !options.TryGetValue("output", out string? output) || !options.TryGetValue("intermediate", out string? intermediate))
{
    return Usage("--assembly, --output and --intermediate are required");
}

int parallelism = 0;

if (options.TryGetValue("parallelism", out string? text) && text.Length > 0 && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out parallelism) || parallelism < 0))
{
    return Usage($"'{text}' is not a number of compilers to run at once");
}

try
{
    ShaderPrecompiler.Run(new PrecompileRequest
    {
        AssemblyPath = assembly,
        OutputPath = output,
        IntermediateDirectory = intermediate,
        StatePath = options.GetValueOrDefault("state"),
        Xcrun = options.GetValueOrDefault("xcrun") is { Length: > 0 } xcrun ? xcrun : "xcrun",
        MaxParallelism = parallelism,
        Log = Console.Out.WriteLine,
    });

    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"SharpCompute.Build failed for '{assembly}': {e}");
    return 1;
}

static int Usage(string problem)
{
    Console.Error.WriteLine($"SharpCompute.Build: {problem}.");
    Console.Error.WriteLine("usage: SharpCompute.Build --assembly <dll> --output <metallib> --intermediate <dir> [--state <file>] [--xcrun <path>] [--parallelism <n>]");
    return 2;
}
