using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace SharpCompute.Benchmarks;

/// <summary>The --run pipeline: verify, first-dispatch samples, BenchmarkDotNet, RESULTS.md.</summary>
public static class Runner
{
    public const int FirstDispatchSamples = 5;

    public static IConfig CreateConfig(bool quick) =>
        DefaultConfig.Instance
            .AddJob(quick ? Job.ShortRun : Job.Default)
            .WithOrderer(new BenchmarkDotNet.Order.DefaultOrderer(BenchmarkDotNet.Order.SummaryOrderPolicy.Declared))
            .WithArtifactsPath(Path.Combine(Directory.GetCurrentDirectory(), "BenchmarkDotNet.Artifacts"));

    public static int Run(string[] args)
    {
        bool quick = args.Contains("--quick");
        string output = Option(args, "--out") ?? "RESULTS.md";
        string filter = Option(args, "--filter") ?? "*";

        Machine machine = Machine.Describe();

        Console.WriteLine("== verify");
        List<Check> checks = Verify.Run(GraphicsDevice.GetDefault(), Console.WriteLine);

        if (checks.Any(c => !c.Passed))
        {
            Console.Error.WriteLine("verify failed; not benchmarking.");
            return 1;
        }

        Console.WriteLine("== first dispatch (fresh process per sample)");
        List<FirstDispatch> firstDispatches = [];

        for (int i = 0; i < FirstDispatchSamples; i++)
        {
            FirstDispatch sample = SpawnFirstDispatch();
            sample.Print();
            firstDispatches.Add(sample);
        }

        // Let this program's own verify / first-dispatch work wind down, then record what else is running.
        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));
        Machine.Snapshot before = Machine.TakeSnapshot();

        Console.WriteLine($"== BenchmarkDotNet ({(quick ? "ShortRun" : "Default")} job)");
        Stopwatch stopwatch = Stopwatch.StartNew();
        Summary[] summaries = BenchmarkSwitcher.FromAssembly(typeof(Runner).Assembly)
            .Run(["--filter", .. filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)], CreateConfig(quick))
            .ToArray();
        stopwatch.Stop();

        if (summaries.Length == 0 || summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)))
        {
            Console.Error.WriteLine("BenchmarkDotNet reported errors; see the log above.");
            return 1;
        }

        Machine.Snapshot after = Machine.TakeSnapshot();
        List<BenchmarkRow> rows = Report.ToRows(summaries);

        // The native baselines are no BenchmarkDotNet benchmarks, so a filter cannot select them: they
        // run with every unfiltered run, and a filtered one keeps what the run it is merged into had.
        NativeBaselineData? native = null;

        if (filter == "*")
        {
            Console.WriteLine("== native baselines");
            native = NativeBaselines.Run(GraphicsDevice.GetDefault(), quick ? 1 : NativeBaselines.DefaultPasses, Console.WriteLine);
        }

        RunData data = Option(args, "--merge") is { } previous
            ? Merge(RunData.Load(previous), rows, checks, firstDispatches, new Rerun([.. rows.Select(r => r.Workload).Distinct()], before, after, quick, stopwatch.Elapsed.TotalMinutes), native)
            : new(machine, before, after, checks, firstDispatches, rows, quick, stopwatch.Elapsed.TotalMinutes, Native: native);
        string dataPath = Path.Combine(Directory.GetCurrentDirectory(), "BenchmarkDotNet.Artifacts", "run-data.json");
        data.Save(dataPath);
        Console.WriteLine($"saved {dataPath} (re-render with --render)");

        return Render(data, output);
    }

    /// <summary>
    /// --merge &lt;run-data.json&gt;: the workloads that just ran replace their rows in an earlier run, the
    /// rest are kept. Verify and first-dispatch results are always from this run (they cover every
    /// workload); the earlier run's load snapshots stay, and this run's are added as a re-run.
    /// </summary>
    private static RunData Merge(
        RunData previous, List<BenchmarkRow> rows, List<Check> checks, List<FirstDispatch> firstDispatches, Rerun rerun, NativeBaselineData? native) =>
        previous with
        {
            Rows = [.. previous.Rows.Where(r => !rerun.Workloads.Contains(r.Workload)), .. rows],
            Checks = checks,
            FirstDispatches = firstDispatches,
            Reruns = [.. previous.Reruns ?? [], rerun],
            Native = native ?? previous.Native,
        };

    /// <summary>
    /// --native-baselines [--passes N] [--merge &lt;run-data.json&gt; [--out RESULTS.md]]: verify, then
    /// measure the native baselines alone (well under a minute). With --merge they replace the native
    /// baselines of an earlier run, whose BenchmarkDotNet rows are kept, and the report is rendered
    /// again; the verification results are then this run's. Without it the section is printed.
    /// </summary>
    public static int RunNative(string[] args)
    {
        int passes = Option(args, "--passes") is { } text ? int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : NativeBaselines.DefaultPasses;
        GraphicsDevice device = GraphicsDevice.GetDefault();

        Console.WriteLine("== verify");
        List<Check> checks = Verify.Run(device, Console.WriteLine);

        if (checks.Any(c => !c.Passed))
        {
            Console.Error.WriteLine("verify failed; not measuring.");
            return 1;
        }

        // As before the benchmarks of a full run: let verify's CPU work wind down first.
        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));

        Console.WriteLine("== native baselines");
        NativeBaselineData native = NativeBaselines.Run(device, passes, Console.WriteLine);

        if (Option(args, "--merge") is not { } previous)
        {
            System.Text.StringBuilder section = new();
            NativeBaselinesReport.Append(section, native);
            Console.WriteLine();
            Console.Write(section);
            return 0;
        }

        RunData data = RunData.Load(previous) with { Checks = checks, Native = native };
        string dataPath = Path.Combine(Directory.GetCurrentDirectory(), "BenchmarkDotNet.Artifacts", "run-data.json");
        Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
        data.Save(dataPath);
        Console.WriteLine($"saved {dataPath} (re-render with --render)");

        return Render(data, Option(args, "--out") ?? "RESULTS.md");
    }

    /// <summary>--render &lt;run-data.json&gt; [--out RESULTS.md]: rebuild the report from a saved run.</summary>
    public static int Render(string[] args)
    {
        string input = Option(args, "--render") ?? throw new ArgumentException("--render needs the path of a run-data.json.");
        return Render(RunData.Load(input), Option(args, "--out") ?? "RESULTS.md");
    }

    private static int Render(RunData data, string output)
    {
        File.WriteAllText(output, Report.Build(data));
        Console.WriteLine($"wrote {Path.GetFullPath(output)}");
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Runs this program again with --first-dispatch, so the device and pipeline start cold.</summary>
    private static FirstDispatch SpawnFirstDispatch()
    {
        string self = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        ProcessStartInfo info = new(self) { RedirectStandardOutput = true, UseShellExecute = false };

        // Under `dotnet SharpCompute.Benchmarks.dll` the process is the dotnet host, which needs the dll.
        if (Path.GetFileNameWithoutExtension(self) == "dotnet")
        {
            info.ArgumentList.Add(typeof(Runner).Assembly.Location);
        }

        info.ArgumentList.Add("--first-dispatch");

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the first-dispatch process.");
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return stdout.Split('\n').Select(FirstDispatch.TryParse).FirstOrDefault(s => s is not null)
            ?? throw new InvalidOperationException($"The first-dispatch process printed no result (exit {process.ExitCode}):\n{stdout}");
    }
}
