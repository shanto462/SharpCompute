// SharpCompute.Tests.Shaders.IntOverflow (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_IntOverflow, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }

constant int Slots = 20;

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

kernel void SharpCompute_Tests_Shaders_IntOverflow(
    const device int* pairs [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int a = pairs[int(2u * uint(i))];
    int b = pairs[int((2u * uint(i)) + 1u)];
    int o = int(uint(i) * uint(::Slots));
    output[o] = long(int(uint(a) * uint(b)));
    output[int(uint(o) + 1u)] = long(b > 1 ? (int(uint(a) * uint(b))) / __ms_nonzero(b) : 0);
    output[int(uint(o) + 2u)] = long(int(uint(a) + 1u) > a ? 1 : 0);
    output[int(uint(o) + 3u)] = long(b > 0 && (int(uint(a) + uint(b))) < a ? 1 : 0);
    output[int(uint(o) + 4u)] = long(int(uint(a) - 1u) < a ? 1 : 0);
    output[int(uint(o) + 5u)] = long(int(uint(a) * 2u) / 2);
    output[int(uint(o) + 6u)] = long(int(uint(a) + uint(b)));
    output[int(uint(o) + 7u)] = long(int(uint(a) - uint(b)));
    output[int(uint(o) + 8u)] = long(int(-uint(a)));
    output[int(uint(o) + 9u)] = long(ulong(int(uint(a) + uint(b))) + ulong(int(uint(a) - uint(b))));
    output[int(uint(o) + 10u)] = long(int(uint(a) * uint(-3)));
    output[int(uint(o) + 11u)] = long(int((uint(a) * uint(b) * uint(a)) - uint(b)));
    output[int(uint(o) + 12u)] = long(int((uint(a) * 1103515245u) + 12345u));
    int x = a;
    x = int(uint(x) + uint(b));
    x = int(uint(x) * uint(b));
    x = int(uint(x) - uint(a));
    output[int(uint(o) + 13u)] = long(x);
    int y = a;
    y = int(uint(y) + 1u);
    output[int(uint(o) + 14u)] = long(y);
    y = a;
    y = int(uint(y) - 1u);
    output[int(uint(o) + 15u)] = long(y);
    int z = a;
    int old = int(reinterpret_cast<thread uint&>(z)++);
    output[int(uint(o) + 16u)] = (long(old) << 32) | long(uint(z));
    output[int(uint(o) + 17u)] = long(int(uint(int(++reinterpret_cast<thread uint&>(z))) + uint(b)));
    output[int(uint(o) + 18u)] = long(int(-(uint(a) * uint(b))));
    output[int(uint(o) + 19u)] = long(b > 0 && a > int(uint(2147483647) - uint(b)) ? 1 : 0);
}
