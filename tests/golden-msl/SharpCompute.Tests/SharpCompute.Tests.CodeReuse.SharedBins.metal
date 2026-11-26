// SharpCompute.Tests.CodeReuse.SharedBins (SharpCompute.Tests): entry point SharpCompute_Tests_CodeReuse_SharedBins, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int binCount;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, binCount) == 0, "SharpCompute: offset of '__Constants.binCount' does not match its C# layout");

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

static int SharedMath_Cell(uint3 __tid, uint3 __grid);
static uint SharedMath_Hash(uint x);
static uint OtherMath_Hash(uint x);
static int SharedMath_Count(device int* __local_bins, int bin);

static int SharedMath_Cell(uint3 __tid, uint3 __grid)
{
    return int((__tid.y * __grid.x) + __tid.x);
}

static uint SharedMath_Hash(uint x)
{
    x ^= x >> 16;
    x *= 2146121005u;
    x ^= x >> 15;
    x *= 2221713035u;
    x ^= x >> 16;
    return x;
}

static uint OtherMath_Hash(uint x)
{
    return x * 2654435761u;
}

static int SharedMath_Count(device int* __local_bins, int bin)
{
    return metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(__local_bins[bin]), 1, metal::memory_order_relaxed);
}

kernel void SharpCompute_Tests_CodeReuse_SharedBins(
    device int* bins [[buffer(0)]],
    device uint* hashes [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int i = ::SharedMath_Cell(__tid, __grid);
    uint a = ::SharedMath_Hash(uint(i));
    uint b = ::OtherMath_Hash(uint(i));
    hashes[i] = a ^ b;
    ::SharedMath_Count(bins, int(a % uint(__c.binCount)));
}
