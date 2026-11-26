// SharpCompute.Samples.Viewer.Demos.LifeRandomize (SharpCompute.Samples.Viewer): entry point SharpCompute_Samples_Viewer_Demos_LifeRandomize, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    uint seed;
    uint threshold;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, seed) == 0, "SharpCompute: offset of '__Constants.seed' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, threshold) == 4, "SharpCompute: offset of '__Constants.threshold' does not match its C# layout");

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

static uint Hash(uint input);

static uint Hash(uint input)
{
    uint state = (input * 747796405u) + 2891336453u;
    uint word = ((state >> int((state >> 28) + 4u)) ^ state) * 277803737u;
    return (word >> 22) ^ word;
}

kernel void SharpCompute_Samples_Viewer_Demos_LifeRandomize(
    device uint* cells [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    cells[i] = ::Hash(uint(i) ^ (__c.seed * 2654435769u)) < __c.threshold ? 1u : 0u;
}
