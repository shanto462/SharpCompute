// SharpCompute.Tests.EndToEnd.Keywords (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Keywords, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    float __ms_constant;
    int __ms_device;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __ms_constant) == 0, "SharpCompute: offset of '__Constants.__ms_constant' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __ms_device) == 4, "SharpCompute: offset of '__Constants.__ms_device' does not match its C# layout");

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

kernel void SharpCompute_Tests_EndToEnd_Keywords(
    device float* buffer [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int __ms_kernel = int(__tid.x);
    float __ms_half = buffer[__ms_kernel] * 0.5f;
    float __ms_thread = __ms_half + __c.__ms_constant;
    float mix = __ms_thread * float(__c.__ms_device);
    float __ms_vertex = mix + 1.0f;
    float __ms_fragment = __ms_vertex * 2.0f;
    float __ms_template = __ms_fragment;
    float namespace_ = __ms_template;
    float __ms_auto = namespace_;
    float __ms_signed = __ms_auto;
    float __ms_unsigned = __ms_signed;
    float __ms_register = __ms_unsigned;
    float __ms_typedef = __ms_register;
    float __ms_union = __ms_typedef;
    float __ms_inline = __ms_union;
    float __ms_friend = __ms_inline;
    float __ms_asm = __ms_friend;
    buffer[__ms_kernel] = __ms_asm;
}
