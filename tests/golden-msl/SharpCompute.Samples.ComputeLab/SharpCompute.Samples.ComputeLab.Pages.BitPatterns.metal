// SharpCompute.Samples.ComputeLab.Pages.BitPatterns (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_BitPatterns, compile options None
#include <metal_stdlib>

using namespace metal;

constant int OriginX = 48;
constant int OriginY = 80;
constant uint Background = 4279636238u;
constant int PitchX = 300;
constant int PitchY = 304;

struct __Constants
{
    int width;
    int tick;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, tick) == 4, "SharpCompute: offset of '__Constants.tick' does not match its C# layout");

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

static uint Rgb(uint r, uint g, uint b);
static uint Ramp(uint t);
static uint PopCount(uint u, uint v, uint tick);
static uint Parity(uint u, uint v, uint tick);
static uint Reverse(uint u, uint v, uint tick);
static uint LeadingZeros(uint u, uint v, uint tick);
static uint TrailingZeros(uint u, uint v, uint tick);
static uint FloatBits(uint u, uint v, uint tick);
static uint Classify(uint u, uint v, uint tick);
static uint PackRoundTrip(uint u, uint v, uint tick);

static uint Rgb(uint r, uint g, uint b)
{
    return 4278190080u | (metal::min(b, 255u) << 16) | (metal::min(g, 255u) << 8) | metal::min(r, 255u);
}

static uint Ramp(uint t)
{
    t = metal::min(t, 255u);
    return ::Rgb((t * t) >> 8, (t * 3u) >> 2, 60u + ((t * 3u) >> 2));
}

static uint PopCount(uint u, uint v, uint tick)
{
    return ::Ramp(metal::popcount((u + tick) ^ v) * 32u);
}

static uint Parity(uint u, uint v, uint tick)
{
    bool odd = (metal::popcount((u + tick) & v) & 1u) != 0u;
    return odd ? ::Rgb(240u, 190u, 90u) : ::Rgb(30u, 40u, 70u);
}

static uint Reverse(uint u, uint v, uint tick)
{
    uint reversed = metal::reverse_bits((u + tick) & 255u) >> 24;
    return 255u - v <= reversed ? ::Ramp(reversed) : ::Rgb(20u, 24u, 40u);
}

static uint LeadingZeros(uint u, uint v, uint tick)
{
    uint zeros = metal::clz((u + (tick & 255u)) * v);
    return ::Ramp((32u - zeros) * 15u);
}

static uint TrailingZeros(uint u, uint v, uint tick)
{
    uint zeros = metal::min(metal::ctz(u + tick), 8u);
    return 255u - v <= (zeros * 28u) + 20u ? ::Ramp(60u + (zeros * 24u)) : ::Rgb(20u, 24u, 40u);
}

static uint FloatBits(uint u, uint v, uint tick)
{
    float value = float(int(((u + tick) & 255u) - 128u)) * 0.375f;
    uint bits = as_type<uint>(value);
    uint bit = 31u - (v >> 3);
    bool set = ((bits >> int(bit)) & 1u) != 0u;
    if ((v & 7u) == 7u)
    {
        return ::Background;
    }
    if (bit == 31u)
    {
        return set ? ::Rgb(255u, 90u, 80u) : ::Rgb(70u, 30u, 30u);
    }
    if (bit >= 23u)
    {
        return set ? ::Rgb(110u, 235u, 130u) : ::Rgb(28u, 62u, 36u);
    }
    return set ? ::Rgb(100u, 170u, 255u) : ::Rgb(26u, 40u, 72u);
}

static uint Classify(uint u, uint v, uint tick)
{
    uint row = v >> 3;
    uint exponent = row == 31u ? 0u : 255u - (row * 8u);
    uint mantissa = ((u + (tick & 15u)) >> 4) & 15u;
    float value = as_type<float>((exponent << 23) | (mantissa << 19));
    if (metal::isnan(value))
    {
        return ::Rgb(235u, 70u, 90u);
    }
    if (metal::isinf(value))
    {
        return ::Rgb(255u, 255u, 255u);
    }
    return metal::isfinite(value) ? ::Ramp(40u + ((exponent * 3u) >> 2) + (mantissa * 2u)) : ::Rgb(255u, 0u, 255u);
}

static uint PackRoundTrip(uint u, uint v, uint tick)
{
    float4 color = float4(float(u) / 255.0f, float(v) / 255.0f, float((u + v + tick) & 255u) / 255.0f, 1.0f);
    uint packed = metal::pack_float_to_unorm4x8(color);
    uint again = metal::pack_float_to_unorm4x8(metal::unpack_unorm4x8_to_float(packed));
    return again == packed ? packed : ::Rgb(255u, 0u, 255u);
}

kernel void SharpCompute_Samples_ComputeLab_Pages_BitPatterns(
    device uint* pixels [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    int cx = int(uint(x) - uint(::OriginX));
    int cy = int(uint(y) - uint(::OriginY));
    uint color = ::Background;
    if (cx >= 0 && cy >= 0)
    {
        int tileX = cx / ::PitchX;
        int tileY = cy / ::PitchY;
        uint u = uint(int(uint(cx) - (uint(tileX) * uint(::PitchX))));
        uint v = uint(int(uint(cy) - (uint(tileY) * uint(::PitchY))));
        if (tileX < 4 && tileY < 2 && u < 256u && v < 256u)
        {
            uint t = uint(__c.tick);
            switch (int(uint(tileX) + (uint(tileY) * 4u)))
            {
                case 0:
                {
                    color = ::PopCount(u, v, t);
                    break;
                }
                case 1:
                {
                    color = ::Parity(u, v, t);
                    break;
                }
                case 2:
                {
                    color = ::Reverse(u, v, t);
                    break;
                }
                case 3:
                {
                    color = ::LeadingZeros(u, v, t);
                    break;
                }
                case 4:
                {
                    color = ::TrailingZeros(u, v, t);
                    break;
                }
                case 5:
                {
                    color = ::FloatBits(u, v, t);
                    break;
                }
                case 6:
                {
                    color = ::Classify(u, v, t);
                    break;
                }
                default:
                {
                    color = ::PackRoundTrip(u, v, t);
                    break;
                }
            }
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = color;
}
