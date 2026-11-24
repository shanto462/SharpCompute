using System.Text;
using SharpCompute;
using Xunit.Abstractions;

namespace SharpCompute.Tests.EndToEnd;

/// <summary>M2 integration: C# shaders → generated MSL → runtime dispatch → CPU reference.</summary>
public sealed class EndToEndTests(ITestOutputHelper output)
{
    [MetalFact]
    public void CoreShaders_MatchCpuReference() => RunSuite(MainSuite.Run);

    [MetalFact]
    public void EdgeCaseShaders_MatchCpuReference() => RunSuite(EdgeSuite.Run);

    private void RunSuite(System.Func<GraphicsDevice, StringBuilder, int> suite)
    {
        StringBuilder log = new();
        int failures = suite(GraphicsDevice.GetDefault(), log);
        output.WriteLine(log.ToString());
        Assert.True(failures == 0, log.ToString());
    }
}
