// SharpCompute.Tests.EndToEnd.Ints (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Ints, compile options None
#include <metal_stdlib>

using namespace metal;

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

kernel void SharpCompute_Tests_EndToEnd_Ints(
    device int* a [[buffer(0)]],
    device uint* u [[buffer(1)]],
    device float* f [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int v = a[i];
    int q = v / 3;
    int r = v % 3;
    int s = v >> 2;
    int m = int(uint(v) * 1000003u);
    int n = int(-uint(v));
    int b = (~v & 255) | (256 ^ v);
    uint w = u[i];
    uint x = w / 3u + w % 7u + (w >> 3) + (w << 29) + uint(v);
    float g = float(v / 2);
    float h = float(v) / 2.0f;
    float k = float(i);
    a[i] = int(uint(q) + uint(r) * 10u + uint(s) * 100u + uint(m ^ n) + uint(b) + w + uint(int(h * 3.7f)) + uint(int(-h * 3.7f)));
    a[i] = int(uint(a[i]) + 1u);
    u[i] = x;
    u[i] >>= 1;
    f[i] = g + h * 0.5f + k + float(w > 100u ? 1 : 0) + float(uint(i));
}
