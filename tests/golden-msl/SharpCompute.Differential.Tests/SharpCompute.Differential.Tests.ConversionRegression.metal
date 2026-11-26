// SharpCompute.Differential.Tests.ConversionRegression (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_ConversionRegression, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_ConversionRegression(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    int i = int(input[0]);
    int i300 = int(input[1]);
    short minusOne = short(input[2]);
    short s16384 = short(input[3]);
    char minus17 = char(input[4]);
    long minus129 = long(input[5]);
    int i70000 = int(input[6]);
    output[0] = long(uchar(metal::clamp(int(float(i)), 0, 255)));
    output[1] = long(uchar(metal::clamp(int(float(i300)), 0, 255)));
    output[2] = long(uchar(metal::clamp(int(half(minusOne)), 0, 255)));
    output[3] = long(char(metal::clamp(int(float(s16384)), -128, 127)));
    output[4] = long(uint(metal::max(float(minusOne), 0.0f)));
    output[5] = long(uint(metal::max(half(minus17), 0.0h)));
    output[6] = long(ulong(metal::max(float(minus17), 0.0f)));
    output[7] = long(ushort(metal::clamp(int(float(i70000)), 0, 65535)));
    output[8] = long(short(metal::clamp(int(float(minus129) * 256.0f), -32768, 32767)));
    output[9] = long(uchar(metal::clamp(int(half(i300)), 0, 255)));
    output[10] = long(ushort(metal::clamp(int(half(minusOne)), 0, 65535)));
}
