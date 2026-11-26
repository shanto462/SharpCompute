// SharpCompute.Tests.Shaders.SmallIntegerOps (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SmallIntegerOps, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }

constant int Stride = 56;
constant short Bias = short(-1234);

struct __Constants
{
    uint __length_bytes;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __length_bytes) == 0, "SharpCompute: offset of '__Constants.__length_bytes' does not match its C# layout");

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

static int Widen(char x, ushort y);
static uchar Halve(uchar x);
static void Split(short value, thread uchar& low, thread char& high);

static int Widen(char x, ushort y)
{
    return int((uint(x) * 3u) + uint(y));
}

static uchar Halve(uchar x)
{
    return uchar(int(x) >> 1);
}

static void Split(short value, thread uchar& low, thread char& high)
{
    low = uchar(value);
    high = char(int(value) >> 8);
}

kernel void SharpCompute_Tests_Shaders_SmallIntegerOps(
    const device uchar* bytes [[buffer(0)]],
    const device char* sbytes [[buffer(1)]],
    const device short* shorts [[buffer(2)]],
    const device ushort* ushorts [[buffer(3)]],
    device int* output [[buffer(4)]],
    constant __Constants& __c [[buffer(5)]],
    constant __GridWindow& __window [[buffer(6), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int j = (int((uint(i) * 7u) + 3u)) % __ms_nonzero(int(__c.__length_bytes));
    int o = int(uint(i) * uint(::Stride));
    uchar b = bytes[i];
    uchar b2 = bytes[j];
    char sb = sbytes[i];
    char sb2 = sbytes[j];
    short s = shorts[i];
    short s2 = shorts[j];
    ushort us = ushorts[i];
    ushort us2 = ushorts[j];
    int count = i % 40;
    output[int(uint(o) + 0u)] = int(uint(b) + uint(b2));
    output[int(uint(o) + 1u)] = int(uint(b) - uint(b2));
    output[int(uint(o) + 2u)] = int(uint(b) * uint(b2));
    output[int(uint(o) + 3u)] = int(uint(sb) + uint(sb2));
    output[int(uint(o) + 4u)] = int(uint(sb) * uint(sb2));
    output[int(uint(o) + 5u)] = int(uint(s) * uint(s2));
    output[int(uint(o) + 6u)] = int(uint(us) + uint(us2));
    output[int(uint(o) + 7u)] = int(uint(us) - uint(us2));
    output[int(uint(o) + 8u)] = int(b2) != 0 ? int(b) / __ms_nonzero(int(b2)) : -1;
    output[int(uint(o) + 9u)] = int(b2) != 0 ? int(b) % __ms_nonzero(int(b2)) : -1;
    output[int(uint(o) + 10u)] = int(sb2) != 0 ? int(sb) / __ms_nonzero(int(sb2)) : -1;
    output[int(uint(o) + 11u)] = int(sb2) != 0 ? int(sb) % __ms_nonzero(int(sb2)) : -1;
    output[int(uint(o) + 12u)] = int(s2) != 0 ? int(s) / __ms_nonzero(int(s2)) : -1;
    output[int(uint(o) + 13u)] = int(us2) != 0 ? int(us) % __ms_nonzero(int(us2)) : -1;
    output[int(uint(o) + 14u)] = int(-uint(b));
    output[int(uint(o) + 15u)] = ~int(b);
    output[int(uint(o) + 16u)] = int(-uint(sb));
    output[int(uint(o) + 17u)] = ~int(us);
    output[int(uint(o) + 18u)] = int(b) << count;
    output[int(uint(o) + 19u)] = int(sb) >> count;
    output[int(uint(o) + 20u)] = int(uint(int(sb)) >> count);
    output[int(uint(o) + 21u)] = int(us) << count;
    output[int(uint(o) + 22u)] = int(s) >> count;
    output[int(uint(o) + 23u)] = (int(b) & int(b2)) | (int(us) ^ int(s));
    output[int(uint(o) + 24u)] = int(uchar(int(uint(b) + uint(b2))));
    output[int(uint(o) + 25u)] = int(char(int(uint(sb) + uint(sb2))));
    output[int(uint(o) + 26u)] = int(short(int(uint(s) + uint(s2))));
    output[int(uint(o) + 27u)] = int(ushort(int(uint(us) + uint(us2))));
    output[int(uint(o) + 28u)] = int(uchar(int(uint(b) * uint(b2))));
    output[int(uint(o) + 29u)] = int(short(int(uint(s) * uint(s2))));
    output[int(uint(o) + 30u)] = int(char(b));
    output[int(uint(o) + 31u)] = int(uchar(sb));
    output[int(uint(o) + 32u)] = int(ushort(s));
    output[int(uint(o) + 33u)] = int(short(us));
    output[int(uint(o) + 34u)] = int(uchar(s));
    output[int(uint(o) + 35u)] = int(char(us));
    output[int(uint(o) + 36u)] = int(uint(us) * uint(us2));
    uchar cb = b;
    cb += int(b2);
    cb <<= 1;
    cb ^= 90;
    cb >>= 2;
    output[int(uint(o) + 37u)] = int(cb);
    char csb = sb;
    csb -= int(sb2);
    csb *= 3u;
    csb >>= 1;
    output[int(uint(o) + 38u)] = int(csb);
    short cs = s;
    cs += int(s2);
    cs *= uint(s2);
    cs |= 4112;
    cs <<= 3;
    output[int(uint(o) + 39u)] = int(cs);
    ushort cus = us;
    cus -= int(us2);
    cus /= 3;
    cus %= 1000;
    cus &= 1023;
    output[int(uint(o) + 40u)] = int(cus);
    uchar ib = b;
    ib++;
    ++ib;
    char isb = sb;
    isb--;
    short ks = s;
    ks++;
    ushort kus = us;
    --kus;
    output[int(uint(o) + 41u)] = int(ib);
    output[int(uint(o) + 42u)] = int(isb);
    output[int(uint(o) + 43u)] = int(ks);
    output[int(uint(o) + 44u)] = int(kus);
    output[int(uint(o) + 45u)] = (int(b) < int(b2) ? 1 : 0) | (int(sb) <= int(sb2) ? 2 : 0) | (int(s) > int(s2) ? 4 : 0) | (int(us) >= int(us2) ? 8 : 0) | (int(b) == int(b2) ? 16 : 0) | (int(sb) != int(sb2) ? 32 : 0) | (int(sb) < int(b) ? 64 : 0) | (int(s) < int(us) ? 128 : 0);
    uchar picked = int(b) > 100 ? b : uchar(200);
    output[int(uint(o) + 46u)] = int(picked);
    output[int(uint(o) + 47u)] = int(b) & 15;
    output[int(uint(o) + 48u)] = int(uint(s) + uint(::Bias));
    output[int(uint(o) + 49u)] = int(255u - uint(b) + uint(-128) + 32767u + 65535u);
    output[int(uint(o) + 50u)] = ::Widen(sb, us);
    output[int(uint(o) + 51u)] = int(::Halve(b));
    uchar low;
    char high;
    ::Split(s, low, high);
    output[int(uint(o) + 52u)] = int(low);
    output[int(uint(o) + 53u)] = int(high);
    int kind;
    switch (uchar(int(b) & 3))
    {
        case uchar(0):
        {
            kind = 10;
            break;
        }
        case uchar(1):
        {
            kind = 20;
            break;
        }
        default:
        {
            kind = 30;
            break;
        }
    }
    output[int(uint(o) + 54u)] = kind;
    output[int(uint(o) + 55u)] = int(uint(short(0)) + uint(uchar(0)));
}
