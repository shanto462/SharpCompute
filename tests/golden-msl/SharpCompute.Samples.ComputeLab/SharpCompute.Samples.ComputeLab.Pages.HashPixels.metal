// SharpCompute.Samples.ComputeLab.Pages.HashPixels (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_HashPixels, compile options None
#include <metal_stdlib>

using namespace metal;

constant ulong Prime = 1099511628211UL;
constant ulong OffsetBasis = 14695981039346656037UL;
constant ulong IndexMask = 1048575UL;

struct __Constants
{
    int size;
    uint seed;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 0, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, seed) == 4, "SharpCompute: offset of '__Constants.seed' does not match its C# layout");

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

static ulong Feed(ulong hash, uint word);
static ulong Key(ulong hash, int index);

static ulong Feed(ulong hash, uint word)
{
    for (int shift = 0; shift < 32; shift = int(uint(shift) + 8u))
    {
        hash = (hash ^ ulong(uchar(word >> shift))) * ::Prime;
    }
    return hash;
}

static ulong Key(ulong hash, int index)
{
    return (hash & ~::IndexMask) | ulong(uint(index));
}

kernel void SharpCompute_Samples_ComputeLab_Pages_HashPixels(
    device ulong* hashes [[buffer(0)]],
    device ulong* largest [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    ulong hash = ::Feed(::Feed(::Feed(::OffsetBasis, uint(x)), uint(y)), __c.seed);
    hash ^= hash >> 32;
    hash *= 15485907386658061715UL;
    hash ^= hash >> 32;
    int i = int((uint(y) * uint(__c.size)) + uint(x));
    hashes[i] = hash;
    metal::atomic_max_explicit((device metal::atomic_ulong*)&(largest[0]), ::Key(hash, i), metal::memory_order_relaxed);
}
