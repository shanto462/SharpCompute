// SharpCompute.Benchmarks.Shaders.HashRounds (SharpCompute.Benchmarks): entry point SharpCompute_Benchmarks_Shaders_HashRounds, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int rounds;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, rounds) == 0, "SharpCompute: offset of '__Constants.rounds' does not match its C# layout");

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

kernel void SharpCompute_Benchmarks_Shaders_HashRounds(
    const device uint* input [[buffer(0)]],
    device uint* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    uint h = input[i];
    for (int r = 0; r < __c.rounds; r = int(uint(r) + 1u))
    {
        h ^= h >> 16;
        h *= 2246822507u;
        h ^= h >> 13;
        h *= 3266489909u;
        h ^= h >> 16;
        h = (h << 5) | (h >> 27);
        h += uint(r) * 2654435769u;
    }
    output[i] = h;
}
