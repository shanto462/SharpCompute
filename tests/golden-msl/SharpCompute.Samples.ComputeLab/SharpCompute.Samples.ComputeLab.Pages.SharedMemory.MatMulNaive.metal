// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.MatMulNaive (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_MatMulNaive, compile options None
#include <metal_stdlib>

using namespace metal;

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

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_MatMulNaive(
    device float* a [[buffer(0)]],
    device float* b [[buffer(1)]],
    device float* c [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int column = int(__tid.x);
    int row = int(__tid.y);
    float sum = 0.0f;
    for (int k = 0; k < __c.n; k = int(uint(k) + 1u))
    {
        sum += a[int((uint(row) * uint(__c.n)) + uint(k))] * b[int((uint(k) * uint(__c.n)) + uint(column))];
    }
    c[int((uint(row) * uint(__c.n)) + uint(column))] = sum;
}
