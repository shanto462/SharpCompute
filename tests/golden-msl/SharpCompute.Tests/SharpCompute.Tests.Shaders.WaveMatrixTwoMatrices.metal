// SharpCompute.Tests.Shaders.WaveMatrixTwoMatrices (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_WaveMatrixTwoMatrices, compile options None
#include <metal_stdlib>
#include <metal_simdgroup_matrix>

using namespace metal;

kernel void SharpCompute_Tests_Shaders_WaveMatrixTwoMatrices(
    device float* destination [[buffer(0)]],
    uint __lane [[thread_index_in_simdgroup]])
{
    metal::simdgroup_float8x8 ones = metal::make_filled_simdgroup_matrix<float, 8, 8>(1.0f);
    metal::simdgroup_float8x8 matrix = int(__lane) < 16 ? ones : metal::make_filled_simdgroup_matrix<float, 8, 8>(0.0f);
    metal::simdgroup_store(matrix, destination + 0, ulong(8), ulong2(0), false);
}
