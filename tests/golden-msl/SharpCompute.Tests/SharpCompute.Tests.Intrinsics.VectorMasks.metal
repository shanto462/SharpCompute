// SharpCompute.Tests.Intrinsics.VectorMasks (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_VectorMasks, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 52;
constant int SelectStride = 5;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");
static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");
static_assert(sizeof(packed_int4) == 16, "SharpCompute: size of 'packed_int4' does not match its C# layout");
static_assert(sizeof(packed_uint4) == 16, "SharpCompute: size of 'packed_uint4' does not match its C# layout");

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

static uint Bits(bool4 mask);
static uint Bits_1(bool2 mask);
static uint Bits_2(bool3 mask);

static uint Bits(bool4 mask)
{
    uint4 bits = metal::select(uint4(0u), uint4(1u, 2u, 4u, 8u), mask);
    return bits.x | bits.y | bits.z | bits.w;
}

static uint Bits_1(bool2 mask)
{
    return ::Bits(bool4(mask, false, false));
}

static uint Bits_2(bool3 mask)
{
    return ::Bits(bool4(mask, false));
}

kernel void SharpCompute_Tests_Intrinsics_VectorMasks(
    const device packed_float4* floatA [[buffer(0)]],
    const device packed_float4* floatB [[buffer(1)]],
    const device packed_half4* halfA [[buffer(2)]],
    const device packed_half4* halfB [[buffer(3)]],
    const device packed_int4* intA [[buffer(4)]],
    const device packed_int4* intB [[buffer(5)]],
    device uint* masks [[buffer(6)]],
    device packed_uint4* selectedBits [[buffer(7)]],
    device packed_half4* selectedHalves [[buffer(8)]],
    constant __GridWindow& __window [[buffer(9), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Stride));
    float4 fa = floatA[i];
    float4 fb = floatB[i];
    half4 ha = halfA[i];
    half4 hb = halfB[i];
    int4 ia = intA[i];
    int4 ib = intB[i];
    uint4 ua = uint4(ia);
    uint4 ub = uint4(ib);
    masks[o] = ::Bits(fa < fb);
    masks[int(uint(o) + 1u)] = ::Bits(fa <= fb);
    masks[int(uint(o) + 2u)] = ::Bits(fa > fb);
    masks[int(uint(o) + 3u)] = ::Bits(fa >= fb);
    masks[int(uint(o) + 4u)] = ::Bits((float4(fa) == float4(fb)));
    masks[int(uint(o) + 5u)] = ::Bits((float4(fa) != float4(fb)));
    masks[int(uint(o) + 6u)] = ::Bits(fa < fb.x);
    masks[int(uint(o) + 7u)] = ::Bits(fa.x >= fb);
    masks[int(uint(o) + 8u)] = ::Bits((float4(fa) == fb.x));
    masks[int(uint(o) + 9u)] = ::Bits(floatA[i] <= floatB[i]);
    masks[int(uint(o) + 10u)] = ::Bits((float4(floatA[i]) != float4(floatB[i])));
    masks[int(uint(o) + 11u)] = ::Bits(ha < hb);
    masks[int(uint(o) + 12u)] = ::Bits(ha <= hb);
    masks[int(uint(o) + 13u)] = ::Bits(ha > hb);
    masks[int(uint(o) + 14u)] = ::Bits(ha >= hb);
    masks[int(uint(o) + 15u)] = ::Bits((half4(ha) == half4(hb)));
    masks[int(uint(o) + 16u)] = ::Bits((half4(ha) != half4(hb)));
    masks[int(uint(o) + 17u)] = ::Bits(ha > hb.x);
    masks[int(uint(o) + 18u)] = ::Bits(ha.x <= hb);
    masks[int(uint(o) + 19u)] = ::Bits((half4(ha) != hb.x));
    masks[int(uint(o) + 20u)] = ::Bits(halfA[i] >= halfB[i]);
    masks[int(uint(o) + 21u)] = ::Bits((half4(halfA[i]) == half4(halfB[i])));
    masks[int(uint(o) + 22u)] = ::Bits(ia < ib);
    masks[int(uint(o) + 23u)] = ::Bits(ia <= ib);
    masks[int(uint(o) + 24u)] = ::Bits(ia > ib);
    masks[int(uint(o) + 25u)] = ::Bits(ia >= ib);
    masks[int(uint(o) + 26u)] = ::Bits((int4(ia) == int4(ib)));
    masks[int(uint(o) + 27u)] = ::Bits((int4(ia) != int4(ib)));
    masks[int(uint(o) + 28u)] = ::Bits(ia < ib.x);
    masks[int(uint(o) + 29u)] = ::Bits(ia.x >= ib);
    masks[int(uint(o) + 30u)] = ::Bits(intA[i] > intB[i]);
    masks[int(uint(o) + 31u)] = ::Bits(ua < ub);
    masks[int(uint(o) + 32u)] = ::Bits(ua <= ub);
    masks[int(uint(o) + 33u)] = ::Bits(ua > ub);
    masks[int(uint(o) + 34u)] = ::Bits(ua >= ub);
    masks[int(uint(o) + 35u)] = ::Bits((uint4(ua) == uint4(ub)));
    masks[int(uint(o) + 36u)] = ::Bits((uint4(ua) != ub.x));
    masks[int(uint(o) + 37u)] = ::Bits(metal::isnan(fa)) | (::Bits(metal::isinf(fa)) << 4) | (::Bits(metal::isfinite(fa)) << 8);
    masks[int(uint(o) + 38u)] = ::Bits(metal::isnan(ha)) | (::Bits(metal::isinf(ha)) << 4) | (::Bits(metal::isfinite(ha)) << 8);
    masks[int(uint(o) + 39u)] = ::Bits_1(metal::isnan(fa.xy)) | (::Bits_2(metal::isinf(ha.yzw)) << 4) | (::Bits(metal::isfinite(halfA[i])) << 8);
    bool4 m = ia < ib;
    bool4 n = (half4(ha) != half4(hb));
    masks[int(uint(o) + 40u)] = ::Bits(m & n);
    masks[int(uint(o) + 41u)] = ::Bits(m | n);
    masks[int(uint(o) + 42u)] = ::Bits(m ^ n);
    masks[int(uint(o) + 43u)] = ::Bits(!m);
    masks[int(uint(o) + 44u)] = ::Bits_1(m.xy & !n.zw) | (::Bits_2(m.xyz ^ n.yzw) << 4);
    masks[int(uint(o) + 45u)] = (metal::any(m) ? 1u : 0u) | (metal::all(m) ? 2u : 0u) | (metal::any(m.xyz) ? 4u : 0u) | (metal::all(m.xyz) ? 8u : 0u) | (metal::any(m.zw) ? 16u : 0u) | (metal::all(m.zw) ? 32u : 0u);
    masks[int(uint(o) + 46u)] = metal::all((fa < fb) | (fa >= fb)) ? 1u : 0u;
    masks[int(uint(o) + 47u)] = ::Bits(metal::select(!n, n, m));
    masks[int(uint(o) + 48u)] = ::Bits_2(metal::select(n.yzw, n.xyz, m.xyz)) | (::Bits_1(metal::select(m.xy, n.xy, m.zw)) << 4);
    int3 three = metal::select(ib.xyz, ia.xyz, n.xyz);
    int2 two = metal::select(ib.xy, ia.zw, m.yz);
    masks[int(uint(o) + 49u)] = uint(three.x ^ three.y ^ three.z);
    masks[int(uint(o) + 50u)] = uint(two.x ^ two.y);
    float2 pair = metal::select(fb.zw, fa.xy, n.xw);
    masks[int(uint(o) + 51u)] = as_type<uint>(pair.x) ^ as_type<uint>(pair.y);
    int s = int(uint(i) * uint(::SelectStride));
    selectedBits[s] = as_type<uint4>(metal::select(fb, fa, m));
    selectedBits[int(uint(s) + 1u)] = uint4(metal::select(ib, ia, n));
    selectedBits[int(uint(s) + 2u)] = metal::select(ub, ua, m ^ n);
    selectedBits[int(uint(s) + 3u)] = as_type<uint4>(metal::select(floatB[i], floatA[i], fa > fb));
    selectedBits[int(uint(s) + 4u)] = as_type<uint4>(metal::select(fa, float4(0.0f), metal::isnan(fa)));
    selectedHalves[int(uint(i) * 2u)] = metal::select(hb, ha, m);
    selectedHalves[int((uint(i) * 2u) + 1u)] = metal::select(halfB[i], halfA[i], ha < hb);
}
