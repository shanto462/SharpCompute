using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Running;
using SharpCompute;
using SharpCompute.Benchmarks;
using SharpCompute.Benchmarks.Shaders;

// Modes:
//   --verify            run every workload once on CPU and GPU and compare (exit 1 on failure)
//   --first-dispatch    time device creation and the first Mandelbrot dispatch in this fresh process
//   --msl               print the generated MSL of every benchmark shader
//   --run [--quick] [--out RESULTS.md] [--filter <glob>] [--merge <run-data.json>]
//                       verify, measure first dispatch, run BenchmarkDotNet, write the report
//   --render <run-data.json> [--out RESULTS.md]
//                       re-render the report from the data a --run saved
//   anything else       passed straight to BenchmarkDotNet's switcher (e.g. --filter *Saxpy* --job short)
// Before anything touches GraphicsDevice, so that device creation is part of what it times.
if (args.Contains("--first-dispatch"))
{
    FirstDispatch.Measure().Print();
    return 0;
}

if (!GraphicsDevice.IsSupported)
{
    Console.Error.WriteLine("Metal is not available (SharpCompute needs macOS 15+ on Apple Silicon).");
    return 2;
}

if (args.Contains("--verify"))
{
    List<Check> checks = Verify.Run(GraphicsDevice.GetDefault(), Console.WriteLine);
    int failed = checks.Count(c => !c.Passed);
    Console.WriteLine(failed == 0 ? $"verify: all {checks.Count} checks passed" : $"verify: {failed} of {checks.Count} checks FAILED");
    return failed == 0 ? 0 : 1;
}

if (args.Contains("--msl"))
{
    Console.WriteLine(ShaderInfo.MslSource<SaxpyGenerated>());
    Console.WriteLine(ShaderInfo.MslSource<MandelbrotIterations>());
    Console.WriteLine(ShaderInfo.MslSource<BlurHorizontal>());
    Console.WriteLine(ShaderInfo.MslSource<BlurVertical>());
    Console.WriteLine(ShaderInfo.MslSource<MatMulNaive>());
    Console.WriteLine(ShaderInfo.MslSource<NBodyStep>());
    return 0;
}

if (args.Contains("--render"))
{
    return Runner.Render(args);
}

if (args.Contains("--run"))
{
    return Runner.Run(args);
}

BenchmarkSwitcher.FromAssembly(typeof(SaxpyBenchmarks).Assembly).Run(args, Runner.CreateConfig(quick: false));
return 0;
