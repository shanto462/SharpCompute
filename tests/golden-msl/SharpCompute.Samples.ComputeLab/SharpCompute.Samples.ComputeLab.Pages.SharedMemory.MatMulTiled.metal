// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.MatMulTiled (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_MatMulTiled, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Tile = 16;

struct __Constants
{
    int n;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, n) == 0, "SharpCompute: offset of '__Constants.n' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_MatMulTiled(
    device float* a [[buffer(0)]],
    device float* b [[buffer(1)]],
    device float* c [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup float tileA[256];
    threadgroup float tileB[256];
    int column = int(__tid.x);
    int row = int(__tid.y);
    int x = int(__gtid.x);
    int y = int(__gtid.y);
    int tiles = (int(uint(__c.n) + uint(::Tile) - 1u)) / ::Tile;
    float sum = 0.0f;
    for (int t = 0; t < tiles; t = int(uint(t) + 1u))
    {
        int k = int((uint(t) * uint(::Tile)) + uint(x));
        int r = int((uint(t) * uint(::Tile)) + uint(y));
        tileA[int((uint(y) * uint(::Tile)) + uint(x))] = row < __c.n && k < __c.n ? a[int((uint(row) * uint(__c.n)) + uint(k))] : 0.0f;
        tileB[int((uint(y) * uint(::Tile)) + uint(x))] = r < __c.n && column < __c.n ? b[int((uint(r) * uint(__c.n)) + uint(column))] : 0.0f;
        metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
        for (int j = 0; j < ::Tile; j = int(uint(j) + 1u))
        {
            sum += tileA[int((uint(y) * uint(::Tile)) + uint(j))] * tileB[int((uint(j) * uint(::Tile)) + uint(x))];
        }
        metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    }
    if (row < __c.n && column < __c.n)
    {
        c[int((uint(row) * uint(__c.n)) + uint(column))] = sum;
    }
}
