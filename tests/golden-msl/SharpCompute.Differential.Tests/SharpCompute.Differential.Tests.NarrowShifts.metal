// SharpCompute.Differential.Tests.NarrowShifts (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_NarrowShifts, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 22;

struct __GridWindow
{
    packed_uint3 offset;
    packed_uint3 size;
    packed_uint3 groupOffset;
};
static_assert(sizeof(__GridWindow) == 36, "SharpCompute: size of '__GridWindow' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, offset) == 0, "SharpCompute: offset of '__GridWindow.offset' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, size) == 12, "SharpCompute: offset of '__GridWindow.size' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, groupOffset) == 24, "SharpCompute: offset of '__GridWindow.groupOffset' does not match its C# layout");

constant bool __windowed [[function_constant(0)]];

kernel void SharpCompute_Differential_Tests_NarrowShifts(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    device short* shorts [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    ulong v = input[int(2u * uint(t))];
    int n = int(input[int(2u * uint(t) + 1u)]);
    int o = int(uint(t) * uint(::Slots));
    short s = short(v);
    ushort us = ushort(v);
    char sb = char(v);
    uchar by = uchar(v);
    int i = int(v);
    long l = long(v);
    short s0 = s;
    s0 >>= 15;
    output[int(uint(o) + 0u)] = long(s0);
    short s1 = s;
    s1 <<= 1;
    output[int(uint(o) + 1u)] = long(s1);
    short s2 = s;
    s2 = short(uint(int(s2)) >> 17);
    output[int(uint(o) + 2u)] = long(s2);
    short s3 = s;
    s3 >>= n;
    output[int(uint(o) + 3u)] = long(s3);
    short s4 = s;
    s4 <<= n;
    output[int(uint(o) + 4u)] = long(s4);
    short s5 = s;
    s5 = short(uint(int(s5)) >> n);
    output[int(uint(o) + 5u)] = long(s5);
    ushort u0 = us;
    u0 = ushort(0);
    output[int(uint(o) + 6u)] = long(u0);
    ushort u1 = us;
    u1 = ushort(0);
    output[int(uint(o) + 7u)] = long(u1);
    ushort u2 = us;
    u2 >>= n;
    output[int(uint(o) + 8u)] = long(u2);
    char b0 = sb;
    b0 >>= 1;
    output[int(uint(o) + 9u)] = long(b0);
    char b1 = sb;
    b1 = char(uint(int(b1)) >> n);
    output[int(uint(o) + 10u)] = long(b1);
    uchar b2 = by;
    b2 = uchar(0);
    output[int(uint(o) + 11u)] = long(b2);
    uchar b3 = by;
    b3 >>= n;
    output[int(uint(o) + 12u)] = long(b3);
    int i0 = i;
    i0 <<= 1;
    output[int(uint(o) + 13u)] = long(i0);
    int i1 = i;
    i1 = int(uint(i1) >> 1);
    output[int(uint(o) + 14u)] = long(i1);
    long l0 = l;
    l0 <<= 1;
    output[int(uint(o) + 15u)] = l0;
    long l1 = l;
    l1 >>= n;
    output[int(uint(o) + 16u)] = l1;
    shorts[t] = s;
    shorts[t] >>= 15;
    output[int(uint(o) + 17u)] = long(shorts[t]);
    int3 w = int3(i, int(uint(i) + 1u), int(uint(i) + 2u));
    w.y <<= 8;
    output[int(uint(o) + 18u)] = long(w.y);
    output[int(uint(o) + 19u)] = long(int(s) << 1);
    output[int(uint(o) + 20u)] = long(short(int(s) >> 21));
    output[int(uint(o) + 21u)] = long(ulong(l) >> 6);
}
