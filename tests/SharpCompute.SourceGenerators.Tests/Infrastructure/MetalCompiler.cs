using System;
using System.Runtime.InteropServices;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Test-only sliver of Metal interop: compiles MSL with <c>-[MTLDevice newLibraryWithSource:options:error:]</c>
/// using the same fixed options as the runtime (MSL 3.2, safe math, precise math functions) and builds a compute pipeline
/// for the kernel. This intentionally duplicates a little of the runtime's interop so the generator
/// tests do not depend on it.
/// </summary>
internal static partial class MetalCompiler
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string Metal = "/System/Library/Frameworks/Metal.framework/Metal";
    private const nint LanguageVersion3_2 = (3 << 16) + 2;
    private const nint MathModeSafe = 0;
    private const nint MathFloatingPointFunctionsPrecise = 1;
    private const nint DataTypeBool = 53;

    private static readonly Lazy<nint> Device = new(CreateDevice);

    /// <summary>False on machines without a Metal device (and on other operating systems).</summary>
    public static bool IsAvailable => Device.Value != 0;

    private const string WarningProbe = "__sharpcompute_warning_probe__";

    /// <summary>The outcome of compiling one MSL translation unit.</summary>
    /// <param name="Success">True when the library compiled and a compute pipeline was built for the kernel.</param>
    /// <param name="Messages">The compiler output when compilation failed.</param>
    /// <param name="Warnings">The warnings clang has about the source, even when it compiled.</param>
    public readonly record struct Result(bool Success, string Messages, string Warnings);

    /// <summary>
    /// Compiles <paramref name="source"/> and builds a pipeline for <paramref name="entryPoint"/>.
    /// Metal reports nothing when a compilation with warnings succeeds, so the source is compiled a
    /// second time with a deliberate error appended: clang then prints the warnings along with it.
    /// </summary>
    public static Result Compile(string source, string entryPoint)
    {
        Result result = CompileCore(source, entryPoint);

        if (!result.Success)
        {
            return result;
        }

        Result probe = CompileCore(source + $"\nstatic_assert(false, \"{WarningProbe}\");\n", entryPoint);
        string warnings = string.Join("\n", ExtractWarnings(probe.Messages));

        return result with { Warnings = warnings };
    }

    private static System.Collections.Generic.IEnumerable<string> ExtractWarnings(string messages)
    {
        string[] lines = messages.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(": warning:", StringComparison.Ordinal))
            {
                // The warning, plus the source line and caret clang prints below it.
                yield return string.Join("\n", lines[i..Math.Min(lines.Length, i + 3)]);
            }
        }
    }

    private static Result CompileCore(string source, string entryPoint)
    {
        nint device = Device.Value;

        if (device == 0)
        {
            throw new InvalidOperationException("No Metal device is available.");
        }

        nint pool = objc_autoreleasePoolPush();

        try
        {
            nint options = Send(Send(objc_getClass("MTLCompileOptions"), Selector("alloc")), Selector("init"));
            SendVoid(options, Selector("setLanguageVersion:"), LanguageVersion3_2);

            if (Responds(options, "setMathMode:"))
            {
                SendVoid(options, Selector("setMathMode:"), MathModeSafe);
            }

            if (Responds(options, "setMathFloatingPointFunctions:"))
            {
                SendVoid(options, Selector("setMathFloatingPointFunctions:"), MathFloatingPointFunctionsPrecise);
            }

            nint library = Send(device, Selector("newLibraryWithSource:options:error:"), ToNSString(source), options, out nint error);
            string messages = Describe(error);
            Send(options, Selector("release"));

            if (library == 0)
            {
                return new Result(false, messages, string.Empty);
            }

            // A library can compile and still not contain the kernel we expect. Like the runtime, build
            // both specializations of function constant 0: the whole-grid kernel and the windowed one
            // that split dispatches use (the value is ignored by kernels that do not declare it).
            try
            {
                foreach (bool windowed in (ReadOnlySpan<bool>)[false, true])
                {
                    Result? failure = BuildPipeline(device, library, entryPoint, windowed, messages);

                    if (failure is not null)
                    {
                        return failure.Value;
                    }
                }
            }
            finally
            {
                Send(library, Selector("release"));
            }

            return new Result(true, messages, string.Empty);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static unsafe Result? BuildPipeline(nint device, nint library, string entryPoint, bool windowed, string messages)
    {
        nint constants = Send(Send(objc_getClass("MTLFunctionConstantValues"), Selector("alloc")), Selector("init"));
        byte value = windowed ? (byte)1 : (byte)0;
        SendVoid(constants, Selector("setConstantValue:type:atIndex:"), &value, DataTypeBool, 0);

        nint function = Send(library, Selector("newFunctionWithName:constantValues:error:"), ToNSString(entryPoint), constants, out nint functionError);
        Send(constants, Selector("release"));

        if (function == 0)
        {
            return new Result(false, $"The library has no function named '{entryPoint}' (windowed: {windowed}). {Describe(functionError)} {messages}", string.Empty);
        }

        nint pipeline = Send(device, Selector("newComputePipelineStateWithFunction:error:"), function, out nint pipelineError);
        string pipelineMessages = Describe(pipelineError);
        Send(function, Selector("release"));

        if (pipeline == 0)
        {
            return new Result(false, $"(windowed: {windowed}) {pipelineMessages}", string.Empty);
        }

        Send(pipeline, Selector("release"));
        return null;
    }

    private static nint CreateDevice()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return 0;
        }

        try
        {
            return MTLCreateSystemDefaultDevice();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    private static nint Selector(string name) => sel_registerName(name);

    private static bool Responds(nint receiver, string selector) =>
        SendBool(receiver, Selector("respondsToSelector:"), Selector(selector)) != 0;

    private static nint ToNSString(string value) =>
        SendString(objc_getClass("NSString"), Selector("stringWithUTF8String:"), value);

    private static string Describe(nint error)
    {
        if (error == 0)
        {
            return string.Empty;
        }

        nint utf8 = Send(Send(error, Selector("localizedDescription")), Selector("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
    }

    [LibraryImport(Metal)]
    private static partial nint MTLCreateSystemDefaultDevice();

    [LibraryImport(ObjC)]
    private static partial nint objc_autoreleasePoolPush();

    [LibraryImport(ObjC)]
    private static partial void objc_autoreleasePoolPop(nint pool);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial byte SendBool(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static unsafe partial void SendVoid(nint receiver, nint selector, void* value, nint type, nint index);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SendString(nint receiver, nint selector, string argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument, out nint error);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint first, nint second, out nint error);
}

/// <summary>A fact that is skipped (rather than silently passing) when this machine has no Metal device.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MetalFactAttribute : FactAttribute
{
    public MetalFactAttribute()
    {
        if (!MetalCompiler.IsAvailable)
        {
            Skip = "No Metal device is available on this machine.";
        }
    }
}

/// <summary>A theory that is skipped (rather than silently passing) when this machine has no Metal device.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MetalTheoryAttribute : TheoryAttribute
{
    public MetalTheoryAttribute()
    {
        if (!MetalCompiler.IsAvailable)
        {
            Skip = "No Metal device is available on this machine.";
        }
    }
}
