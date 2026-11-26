// SharpCompute.Samples.Textures.PaintBrush (SharpCompute.Samples.Textures): entry point SharpCompute_Samples_Textures_PaintBrush, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    packed_int2 origin;
    packed_float2 center;
    float radius;
    int erase;
};
static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, origin) == 0, "SharpCompute: offset of '__Constants.origin' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, center) == 8, "SharpCompute: offset of '__Constants.center' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radius) == 16, "SharpCompute: offset of '__Constants.radius' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, erase) == 20, "SharpCompute: offset of '__Constants.erase' does not match its C# layout");

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

kernel void SharpCompute_Samples_Textures_PaintBrush(
    texture2d<float, access::read_write> field [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int2 position = int2(uint2(__c.origin) + __tid.xy);
    float2 offset = float2(float(position.x) + 0.5f, float(position.y) + 0.5f) - __c.center;
    if (metal::dot(offset, offset) > __c.radius * __c.radius)
    {
        return;
    }
    int x = (int(uint(position.x % int(field.get_width())) + uint(int(field.get_width())))) % int(field.get_width());
    int y = (int(uint(position.y % int(field.get_height())) + uint(int(field.get_height())))) % int(field.get_height());
    float4 state = field.read(uint2(x, y));
    field.write(__c.erase != 0 ? float4(1.0f, 0.0f, 0.0f, 1.0f) : float4(state.x, 1.0f, 0.0f, 1.0f), uint2(x, y));
    field.fence();
}
