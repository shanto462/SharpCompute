// SharpCompute.Tests.Shaders.WaveMatrixHalfValues (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_WaveMatrixHalfValues, compile options None
#include <metal_stdlib>
#include <metal_simdgroup_matrix>

using namespace metal;

struct __Constants
{
    half filled;
    half diagonal;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, filled) == 0, "SharpCompute: offset of '__Constants.filled' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, diagonal) == 2, "SharpCompute: offset of '__Constants.diagonal' does not match its C# layout");

kernel void SharpCompute_Tests_Shaders_WaveMatrixHalfValues(
    device half* destination [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]])
{
    metal::simdgroup_half8x8 a = metal::make_filled_simdgroup_matrix<half, 8, 8>(__c.filled);
    metal::simdgroup_half8x8 b = metal::simdgroup_half8x8(__c.diagonal);
    metal::simdgroup_half8x8 c = metal::make_filled_simdgroup_matrix<half, 8, 8>(0.0h);
    metal::simdgroup_store(a, destination + 0, ulong(8), ulong2(0), false);
    metal::simdgroup_store(b, destination + 64, ulong(8), ulong2(0), false);
    metal::simdgroup_store(c, destination + 128, ulong(8), ulong2(0), false);
}
