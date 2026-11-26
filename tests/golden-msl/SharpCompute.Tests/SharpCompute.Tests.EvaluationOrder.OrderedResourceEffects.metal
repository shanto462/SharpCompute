// SharpCompute.Tests.EvaluationOrder.OrderedResourceEffects (SharpCompute.Tests): entry point SharpCompute_Tests_EvaluationOrder_OrderedResourceEffects, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

struct __Constants
{
    uint __length_data;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __length_data) == 0, "SharpCompute: offset of '__Constants.__length_data' does not match its C# layout");

static int Bump(int slot, device int* data);
static int BumpResult(int slot, device int* result);
static int Inc(device int* buffer, int slot);
static int BumpTile(int slot, threadgroup int* tile);
static int Paint(int x, texture2d<float, access::read_write> image);
static int PaintAlias(int x, texture2d<float, access::read_write> image);
static int Next(device int* data);

static int Bump(int slot, device int* data)
{
    data[slot] = int(uint(data[slot]) + 1u);
    return 10;
}

static int BumpResult(int slot, device int* result)
{
    result[slot] = int(uint(result[slot]) + 1u);
    return 10;
}

static int Inc(device int* buffer, int slot)
{
    buffer[slot] = int(uint(buffer[slot]) + 1u);
    return 10;
}

static int BumpTile(int slot, threadgroup int* tile)
{
    tile[slot] = int(uint(tile[slot]) + 1u);
    return 10;
}

static int Paint(int x, texture2d<float, access::read_write> image)
{
    image.write(float4(50.0f, 0.0f, 0.0f, 0.0f), uint2(x, 0));
    image.fence();
    return 10;
}

static int PaintAlias(int x, texture2d<float, access::read_write> image)
{
    texture2d<float, access::read_write> target = image;
    target.write(float4(60.0f, 0.0f, 0.0f, 0.0f), uint2(x, 0));
    target.fence();
    return 10;
}

static int Next(device int* data)
{
    data[35] = int(uint(data[35]) + 1u);
    return int(uint(data[35]) - 100u);
}

kernel void SharpCompute_Tests_EvaluationOrder_OrderedResourceEffects(
    texture2d<float, access::read_write> image [[texture(0)]],
    device int* data [[buffer(0)]],
    device int* result [[buffer(1)]],
    device int* counter [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]])
{
    threadgroup int tile[4];
    data[16] = metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(data[17]), data[18], metal::memory_order_relaxed);
    result[0] = data[19] > 0 ? ::Bump(19, data) : data[19];
    result[1] = data[20] > 0 && ::Bump(20, data) == 10 ? 1 : 0;
    data[0] = ::Bump(0, data);
    data[1] = data[2] = 5;
    data[int(uint(int(__c.__length_data)) - 1u)] = ::Bump(47, data);
    data[24] = int(uint(data[24]) + 1u);
    data[25] = int(uint(data[25]) + 3u);
    data[26] = int(uint(data[26]) + 1u);
    result[5] = int(uint(result[5]) + uint(data[27]));
    reinterpret_cast<device uint&>(result[6]) += uint(::Bump(28, data));
    reinterpret_cast<device uint&>(data[29]) += uint(::BumpResult(7, result));
    result[data[30]] = ::Inc(counter, 1);
    result[int(uint(metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counter[0]), 1, metal::memory_order_relaxed)) + 12u)] = 77;
    data[31] = int(uint(metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counter[0]), 5, metal::memory_order_relaxed)) + uint(data[32]));
    tile[0] = data[33];
    tile[1] = 0;
    int before = tile[0];
    result[10] = int(uint(before) + uint(::BumpTile(0, tile)));
    tile[1] = int(uint(tile[1]) + uint(tile[0]));
    result[11] = tile[1];
    float pixel = image.read(uint2(0, 0)).x;
    result[14] = int(uint(__ms_sat_int(pixel)) + uint(::Paint(0, image)));
    result[15] = __ms_sat_int(image.read(uint2(0, 0)).x);
    texture2d<float, access::read_write> alias = image;
    data[34] = ::PaintAlias(1, image);
    result[13] = __ms_sat_int(alias.read(uint2(1, 0)).x);
    reinterpret_cast<device uint&>(data[::Next(data)]) += 5u;
}
