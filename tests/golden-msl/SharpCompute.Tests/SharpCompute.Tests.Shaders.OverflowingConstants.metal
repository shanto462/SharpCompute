// SharpCompute.Tests.Shaders.OverflowingConstants (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_OverflowingConstants, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Big = 2147483647;
constant int Wrapped = int(uint(::Big) + 1u);
constant int Product = int(uint(::Big) * 3u);
constant int Negated = int(-uint(::Wrapped));
constant long LongWrapped = long(ulong(9223372036854775807L) + ulong(::Big));
constant long LongProduct = long(ulong(::Big) * ulong(::Big) * 5UL);
constant int2 Vector = int2(uint2(int2(::Big, ::Wrapped)) * 2u);
constant int FoldedSum = 2147483647;
constant int FoldedProduct = -2;
constant int2 FoldedVector = int2(-2147483647 - 1, -2147483647 - 1);

kernel void SharpCompute_Tests_Shaders_OverflowingConstants(
    device long* output [[buffer(0)]])
{
    output[0] = long(::Wrapped);
    output[1] = long(::Product);
    output[2] = long(::Negated);
    output[3] = ::LongWrapped;
    output[4] = ::LongProduct;
    output[5] = long(int(uint(::Wrapped) - uint(::Big)));
    output[6] = long(::Vector.x);
    output[7] = long(::Vector.y);
    output[8] = long(::FoldedSum);
    output[9] = long(::FoldedProduct);
    output[10] = long(::FoldedVector.x);
    output[11] = long(::FoldedVector.y);
}
