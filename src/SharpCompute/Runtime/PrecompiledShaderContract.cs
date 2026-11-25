using System.Globalization;

namespace SharpCompute;

/// <summary>
/// What the runtime and the build tool that precompiles shaders (<c>SharpCompute.Build</c>) have to
/// agree on: the file a library is stored in, the text that is compiled for a shader and the name its
/// kernel has in the library. This file is compiled into both, so they cannot drift apart.
/// </summary>
/// <remarks>
/// A kernel is stored under its entry point followed by a hash of everything that decides what the
/// compiled code is (<see cref="FunctionName"/>). The runtime computes that name from the shader's
/// current descriptor and asks the library for it, so a library that was built from another version
/// of the source, with other options or by a tool that compiles differently simply does not contain
/// the name, and the shader is compiled from source instead. A stale library is never run.
/// </remarks>
internal static class PrecompiledShaderContract
{
    /// <summary>The extension of a precompiled library; the file is named after the assembly whose shaders it holds.</summary>
    internal const string LibraryExtension = ".metallib";

    /// <summary><c>ShaderCompileOptions.FastMath</c>.</summary>
    internal const int FastMath = 1;

    /// <summary><c>ShaderCompileOptions.NoFpContract</c>.</summary>
    internal const int NoFpContract = 2;

    /// <summary>
    /// The line put ahead of a shader's MSL for <see cref="NoFpContract"/>, by the runtime when it
    /// compiles the source and by the build tool when it precompiles it.
    /// </summary>
    internal const string FpContractOffPragma = "#pragma clang fp contract(off)\n";

    /// <summary>Separates the entry point from the hash in <see cref="FunctionName"/>.</summary>
    internal const string HashSeparator = "__sc";

    // Hashed with every shader. Change it whenever the way a shader is precompiled changes (the
    // language version, how an option maps to a compiler flag): the names of every library built
    // before then stop matching, and those libraries are ignored.
    private const string Scheme = "SharpCompute.Build 1: -std=metal3.2, -fmetal-math-mode and -fmetal-math-fp32-functions from the options, fp contract pragma in the source";

    /// <summary>The exact text that is compiled for a shader: its MSL, after the pragma if contraction is off.</summary>
    internal static string CompiledSource(string mslSource, int compileOptions) =>
        (compileOptions & NoFpContract) != 0 ? FpContractOffPragma + mslSource : mslSource;

    /// <summary>
    /// The name of a shader's kernel in a precompiled library: <paramref name="entryPoint"/>,
    /// <see cref="HashSeparator"/> and 32 hex digits that depend on the entry point, the compile
    /// options and every character of the MSL.
    /// </summary>
    /// <remarks>
    /// The hash detects change; it protects against nothing else (a library next to an application is
    /// trusted as much as the application's own assemblies are). It is two independent 64-bit
    /// multiplicative hashes rather than SHA-256 because this runs when a shader is first used, the
    /// moment precompiling is meant to shorten: the first SHA-256 of a process loads the platform's
    /// cryptography library, which takes 2 to 3 ms, against some tens of microseconds for this.
    /// </remarks>
    internal static string FunctionName(string entryPoint, string mslSource, int compileOptions)
    {
        Hash hash = new();
        hash.Add(Scheme);
        hash.Add((char)compileOptions);
        hash.Add(entryPoint);
        hash.Add(mslSource);

        return string.Concat(
            entryPoint,
            HashSeparator,
            hash.A.ToString("x16", CultureInfo.InvariantCulture),
            hash.B.ToString("x16", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// FNV-1a and a rotate-multiply hash over the same UTF-16 code units, each finished with an
    /// avalanche step. A string's length is hashed after it, so that the parts cannot run into each other.
    /// </summary>
    private struct Hash
    {
        private ulong _a = 14695981039346656037UL;
        private ulong _b = 0x9E3779B97F4A7C15UL;

        public Hash()
        {
        }

        public readonly ulong A => Finish(_a);

        public readonly ulong B => Finish(_b);

        public void Add(string text)
        {
            foreach (char c in text)
            {
                Add(c);
            }

            Add((char)(text.Length & 0xFFFF));
            Add((char)(text.Length >> 16));
        }

        public void Add(char c)
        {
            unchecked
            {
                _a = (_a ^ c) * 1099511628211UL;
                _b = (((_b << 23) | (_b >> 41)) + c) * 0xD6E8FEB86659FD93UL;
            }
        }

        private static ulong Finish(ulong value)
        {
            unchecked
            {
                value ^= value >> 33;
                value *= 0xFF51AFD7ED558CCDUL;
                value ^= value >> 33;
                value *= 0xC4CEB9FE1A85EC53UL;
                return value ^ (value >> 33);
            }
        }
    }
}
