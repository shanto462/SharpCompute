// SharpCompute.Samples.ComputeLab.Pages.Simd.SumSerial (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_SumSerial, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int count;
    int slot;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 0, "SharpCompute: offset of '__Constants.count' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, slot) == 4, "SharpCompute: offset of '__Constants.slot' does not match its C# layout");

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_SumSerial(
    device float* partials [[buffer(0)]],
    device float* totals [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]])
{
    float sum = 0.0f;
    for (int i = 0; i < __c.count; i = int(uint(i) + 1u))
    {
        sum += partials[i];
    }
    totals[__c.slot] = sum;
}
