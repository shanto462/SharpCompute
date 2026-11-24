using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

public class ShaderCompilationTests
{
    [MetalFact]
    public void MslCompileError_ThrowsWithTheCompilerOutputAndTheSource()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        ShaderCompilationException exception = Assert.Throws<ShaderCompilationException>(
            () => device.For(4, default(BrokenSyntax)));

        // NSError.localizedDescription carries the clang diagnostics.
        Assert.Contains("undeclared_identifier_xyz", exception.Message);
        Assert.Contains("error", exception.Message);
        Assert.Contains(nameof(BrokenSyntax), exception.Message);
        Assert.Equal(BrokenSyntax.MslSource, exception.MslSource);
    }

    [MetalFact]
    public void MslCompileError_IsReportedOnEveryDispatchAndDoesNotPoisonOtherShaders()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        Assert.Throws<ShaderCompilationException>(() => device.For(4, default(BrokenSyntax)));
        Assert.Throws<ShaderCompilationException>(() => device.For(4, 4, default(BrokenSyntax)));

        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3]);
        device.For(buffer.Length, new MultiplyByTwo(buffer, 2f));

        Assert.Equal([2f, 4f, 6f], buffer.ToArray());
    }

    [MetalFact]
    public void FailedLayoutDriftGuard_SurfacesAsACompilationError()
    {
        // This is how a generator layout bug shows up: static_assert fails at pipeline compile
        // instead of the kernel silently reading constants at the wrong offsets.
        GraphicsDevice device = GraphicsDevice.GetDefault();

        ShaderCompilationException exception = Assert.Throws<ShaderCompilationException>(
            () => device.For(1, default(BrokenLayoutGuard)));

        Assert.Contains("__Constants size drifted from the C# mirror", exception.Message);
        Assert.Equal(BrokenLayoutGuard.MslSource, exception.MslSource);
    }

    [MetalFact]
    public void MissingEntryPoint_ThrowsNamingTheEntryPoint()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        ShaderCompilationException exception = Assert.Throws<ShaderCompilationException>(
            () => device.For(1, default(MissingEntryPoint)));

        Assert.Contains(MissingEntryPoint.EntryPoint, exception.Message);
        Assert.Equal(MissingEntryPoint.MslSource, exception.MslSource);
    }
}
