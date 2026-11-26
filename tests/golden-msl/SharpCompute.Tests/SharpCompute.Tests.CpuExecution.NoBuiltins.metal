// SharpCompute.Tests.CpuExecution.NoBuiltins (SharpCompute.Tests): entry point SharpCompute_Tests_CpuExecution_NoBuiltins, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    float value;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, value) == 0, "SharpCompute: offset of '__Constants.value' does not match its C# layout");

kernel void SharpCompute_Tests_CpuExecution_NoBuiltins(
    device float* data [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]])
{
    data[0] = __c.value;
}
