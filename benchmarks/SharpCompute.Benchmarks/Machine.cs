using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpCompute.Benchmarks;

/// <summary>Hardware and OS facts for the report, from sysctl / sw_vers / system_profiler.</summary>
public sealed record Machine(string Model, string Cpu, string Cores, string Memory, string Gpu, string Os, string Runtime, string Power)
{
    /// <summary>System load at one moment: other processes on a shared machine skew every number.</summary>
    public sealed record Snapshot(DateTimeOffset Time, string Uptime, IReadOnlyList<string> TopProcesses);

    public static Machine Describe()
    {
        string performance = Sysctl("hw.perflevel0.physicalcpu");
        string efficiency = Sysctl("hw.perflevel1.physicalcpu");
        long memory = long.TryParse(Sysctl("hw.memsize"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes) ? bytes : 0;

        string gpuCores = Shell("system_profiler", "SPDisplaysDataType")
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("Total Number of Cores:", StringComparison.Ordinal))?
            .Split(':')[1].Trim() ?? "?";

        string power = Shell("pmset", "-g", "batt").Split('\n').FirstOrDefault()?.Trim() ?? "?";

        return new Machine(
            Model: Sysctl("hw.model"),
            Cpu: Sysctl("machdep.cpu.brand_string"),
            Cores: $"{Sysctl("hw.ncpu")} ({performance} performance + {efficiency} efficiency)",
            Memory: $"{memory / (1L << 30)} GB unified",
            Gpu: $"{GraphicsDevice.GetDefault().Name}, {gpuCores} cores",
            Os: $"macOS {Shell("sw_vers", "-productVersion").Trim()} ({Shell("sw_vers", "-buildVersion").Trim()}), {RuntimeInformation.OSArchitecture}",
            Runtime: $"{RuntimeInformation.FrameworkDescription}, BenchmarkDotNet {typeof(BenchmarkDotNet.Running.BenchmarkRunner).Assembly.GetName().Version}",
            Power: power);
    }

    public static Snapshot TakeSnapshot()
    {
        string[] lines = Shell("ps", "-Ao", "pcpu=,comm=", "-r").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        List<string> top = [];

        foreach (string line in lines.Take(8))
        {
            string trimmed = line.Trim();
            int space = trimmed.IndexOf(' ', StringComparison.Ordinal);

            if (space > 0)
            {
                top.Add($"{Path.GetFileName(trimmed[(space + 1)..].Trim())} {trimmed[..space]} %");
            }
        }

        return new Snapshot(DateTimeOffset.Now, Shell("uptime").Trim(), top);
    }

    private static string Sysctl(string name) => Shell("sysctl", "-n", name).Trim();

    private static string Shell(string file, params string[] arguments)
    {
        try
        {
            ProcessStartInfo info = new(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(info)!;
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return stdout;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "?";
        }
    }
}
