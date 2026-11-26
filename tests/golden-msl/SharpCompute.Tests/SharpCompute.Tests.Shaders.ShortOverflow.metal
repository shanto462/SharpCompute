// SharpCompute.Tests.Shaders.ShortOverflow (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_ShortOverflow, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return x | int(metal::reverse_bits(x) == int(0)); }

constant int Slots = 12;

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

kernel void SharpCompute_Tests_Shaders_ShortOverflow(
    const device short* pairs [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    short a = pairs[int(2u * uint(i))];
    short b = pairs[int((2u * uint(i)) + 1u)];
    int o = int(uint(i) * uint(::Slots));
    output[o] = long(short(int(uint(a) * uint(b))));
    output[int(uint(o) + 1u)] = long(int(b) > 1 ? int(short(int(uint(a) * uint(b)))) / __ms_nonzero(int(b)) : 0);
    output[int(uint(o) + 2u)] = long(int(short(int(uint(a) + 1u))) > int(a) ? 1 : 0);
    output[int(uint(o) + 3u)] = long(int(b) > 0 && int(short(int(uint(a) + uint(b)))) < int(a) ? 1 : 0);
    output[int(uint(o) + 4u)] = long(int(short(int(uint(a) * 2u))) / 2);
    output[int(uint(o) + 5u)] = long(int(uint(a) * uint(a) * uint(a) * uint(a)));
    output[int(uint(o) + 6u)] = long(int(-uint(a)));
    output[int(uint(o) + 7u)] = long(int(uint(a) * 65537u * uint(b)));
    short x = a;
    x += int(b);
    x *= uint(b);
    x -= int(a);
    output[int(uint(o) + 8u)] = long(x);
    short y = a;
    y++;
    output[int(uint(o) + 9u)] = long(y);
    y = a;
    y--;
    output[int(uint(o) + 10u)] = long(y);
    y = a;
    y += 32767;
    output[int(uint(o) + 11u)] = long(y);
}
