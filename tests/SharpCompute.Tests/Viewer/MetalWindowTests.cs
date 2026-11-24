using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SharpCompute.Interop;
using SharpCompute.Presentation;
using SharpCompute.Samples.Shaders;
using Xunit.Abstractions;

namespace SharpCompute.Tests.Viewer;

/// <summary>
/// A test that opens real windows. Opt-in so <c>dotnet test</c> never pops windows by default: runs
/// only with <c>SHARPCOMPUTE_WINDOW_TESTS=1</c>, and is still skipped in CI, without a window server
/// (SSH, headless), without a Metal device, or when the Viewer isn't built.
/// </summary>
public sealed class WindowFactAttribute : FactAttribute
{
    public WindowFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHARPCOMPUTE_WINDOW_TESTS") != "1" ||
            Environment.GetEnvironmentVariable("CI") is { Length: > 0 } ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is { Length: > 0 })
        {
            Skip = "Window tests are opt-in: set SHARPCOMPUTE_WINDOW_TESTS=1 (never run in CI).";
        }
        else if (!MetalWindow.IsSupported)
        {
            Skip = "No Metal device or no window server (e.g. an SSH or headless session).";
        }
        else if (ViewerAssembly.Path is null)
        {
            Skip = ViewerAssembly.NotBuilt;
        }
    }
}

public class MetalWindowTests(ITestOutputHelper output)
{
    [Fact]
    public void IsSupported_DoesNotThrow()
    {
        _ = MetalWindow.IsSupported;
    }

    [MetalFact]
    public void Constructor_OffTheMainThread_ThrowsAClearError()
    {
        // Test runners never execute tests on the process main thread, which is exactly the case to reject.
        Assert.False(AppKit.IsMainThread);

        InvalidOperationException e = Assert.Throws<InvalidOperationException>(
            () => new MetalWindow(GraphicsDevice.GetDefault(), "test", 320, 200));

        Assert.Contains("main thread", e.Message, StringComparison.Ordinal);
        Assert.Contains("Main", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, -1)]
    [InlineData(16385, 100)]
    public void Constructor_RejectsBadSizesBeforeTouchingAppKit(int width, int height)
    {
        if (!GraphicsDevice.IsSupported)
        {
            return;
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new MetalWindow(GraphicsDevice.GetDefault(), "test", width, height));
    }

    /// <summary>
    /// End to end through a real window: the Viewer's screenshot hook opens a MetalWindow on its main
    /// thread, renders and presents frames for a gallery shader and both live demos, saves the last
    /// frame and exits.
    /// </summary>
    [WindowFact]
    public void Viewer_PresentsFramesInARealWindow()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpcompute-viewer-tests-" + Environment.ProcessId);
        Directory.CreateDirectory(directory);

        try
        {
            foreach (string shader in new[] { Gallery.All[0].Name, "GameOfLife", "NBody" })
            {
                string png = Path.Combine(directory, shader + ".png");
                (int exitCode, string stdout, string stderr) = RunViewer(shader, png, frames: 30);

                output.WriteLine(stdout.Trim());

                // 3 = every frame was rendered and saved, but the window was hidden the whole time
                // (screen locked, another Space) so nothing reached the display. That's not a failure
                // of the code under test, but it is worth seeing in the output.
                Assert.True(exitCode is 0 or 3, $"{shader}: exit code {exitCode}\n{stdout}\n{stderr}");

                if (exitCode == 3)
                {
                    output.WriteLine($"{shader}: the window was not visible, so no frame was presented.");
                }

                byte[] bytes = File.ReadAllBytes(png);
                Assert.True(bytes.AsSpan(0, 8).SequenceEqual(PngEncoder.Encode([new Rgba32(0, 0, 0, 255)], 1, 1).AsSpan(0, 8)), "Not a PNG.");
                Assert.Equal(1280, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)));
                Assert.Equal(720, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunViewer(string shader, string png, int frames)
    {
        ProcessStartInfo start = new(DotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(ViewerAssembly.Path!);
        start.ArgumentList.Add("--shader");
        start.ArgumentList.Add(shader);
        start.Environment["SHARPCOMPUTE_VIEWER_SCREENSHOT"] = png;
        start.Environment["SHARPCOMPUTE_VIEWER_FRAMES"] = frames.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using Process process = Process.Start(start)!;
        System.Threading.Tasks.Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        System.Threading.Tasks.Task<string> stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{shader}: the viewer did not exit within 60 s.");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>The <c>dotnet</c> muxer that hosts this test run (…/dotnet/shared/Microsoft.NETCore.App/&lt;version&gt;/ → …/dotnet/dotnet).</summary>
    private static string DotnetHost()
    {
        string runtime = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", "dotnet"));
        return File.Exists(candidate) ? candidate : "dotnet";
    }
}
