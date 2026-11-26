// SharpCompute.Samples.Paint.PackSnapshot (SharpCompute.Samples.Paint): entry point SharpCompute_Samples_Paint_PackSnapshot, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline uint4 __ms_sat_uint4(float4 x) { float4 y = metal::select(float4(0.0f), x, x > 0.0f); uint4 r = uint4(metal::min(y, float4(4294967040.0f))); return metal::select(r, uint4(4294967295u), x >= 4294967296.0f); }
inline uint4 __ms_sat_uint4(half4 x) { return __ms_sat_uint4(float4(x)); }

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

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

kernel void SharpCompute_Samples_Paint_PackSnapshot(
    device packed_float4* source [[buffer(0)]],
    device uint* destination [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    uint4 q = __ms_sat_uint4((metal::clamp(source[i], 0.0f, 1.0f) * 255.0f) + float4(0.5f, 0.5f, 0.5f, 0.5f));
    destination[i] = q.x | (q.y << 8) | (q.z << 16) | (q.w << 24);
}
