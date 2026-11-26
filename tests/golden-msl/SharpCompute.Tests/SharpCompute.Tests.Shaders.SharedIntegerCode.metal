// SharpCompute.Tests.Shaders.SharedIntegerCode (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SharedIntegerCode, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 12;

struct Tally
{
    ulong Total;
    short Delta;
    uchar Count;
};
static_assert(sizeof(Tally) == 16, "SharpCompute: size of 'Tally' does not match its C# layout");
static_assert(__builtin_offsetof(Tally, Total) == 0, "SharpCompute: offset of 'Tally.Total' does not match its C# layout");
static_assert(__builtin_offsetof(Tally, Delta) == 8, "SharpCompute: offset of 'Tally.Delta' does not match its C# layout");
static_assert(__builtin_offsetof(Tally, Count) == 10, "SharpCompute: offset of 'Tally.Count' does not match its C# layout");

struct __Constants
{
    uchar step;
    uint __length_values;
    uint __length_bytes;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, step) == 0, "SharpCompute: offset of '__Constants.step' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __length_values) == 4, "SharpCompute: offset of '__Constants.__length_values' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __length_bytes) == 8, "SharpCompute: offset of '__Constants.__length_bytes' does not match its C# layout");

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

static long IntegerHelpers_Load_long(const device long* data, int index);
static uchar IntegerHelpers_Load_uchar(const device uchar* data, int index);
static ulong IntegerHelpers_Feed(ulong hash, uchar value);
static uchar IntegerHelpers_High(ushort value);
static long IntegerHelpers_Widen(char small, short medium);
static void IntegerHelpers_Swap_long(thread long& a, thread long& b);
static void IntegerHelpers_Swap_uchar(thread uchar& a, thread uchar& b);
static long IntegerHelpers_Pick_long(bool condition, long a, long b);
static uchar IntegerHelpers_Pick_uchar(bool condition, uchar a, uchar b);
static short IntegerHelpers_Pick_short(bool condition, short a, short b);
static ulong IntegerHelpers_Pick_ulong(bool condition, ulong a, ulong b);
static Tally Tally_new(ulong total, short delta);
static void Tally_Add(thread Tally& __this, uchar value);
static Tally Tally_op_Addition(Tally a, Tally b);
static void Tally_Add_device(device Tally& __this, uchar value);
static long Tally_Signed(Tally __this);
static ushort Tally_Scaled(Tally __this, uchar factor);
static long Tally_op_Explicit(Tally tally);

static long IntegerHelpers_Load_long(const device long* data, int index)
{
    return data[index];
}

static uchar IntegerHelpers_Load_uchar(const device uchar* data, int index)
{
    return data[index];
}

static ulong IntegerHelpers_Feed(ulong hash, uchar value)
{
    return (hash ^ ulong(value)) * 1099511628211UL;
}

static uchar IntegerHelpers_High(ushort value)
{
    return uchar(int(value) >> 8);
}

static long IntegerHelpers_Widen(char small, short medium)
{
    return long(ulong(small) * ulong(medium));
}

static void IntegerHelpers_Swap_long(thread long& a, thread long& b)
{
    long held = a;
    a = b;
    b = held;
}

static void IntegerHelpers_Swap_uchar(thread uchar& a, thread uchar& b)
{
    uchar held = a;
    a = b;
    b = held;
}

static long IntegerHelpers_Pick_long(bool condition, long a, long b)
{
    return condition ? a : b;
}

static uchar IntegerHelpers_Pick_uchar(bool condition, uchar a, uchar b)
{
    return condition ? a : b;
}

static short IntegerHelpers_Pick_short(bool condition, short a, short b)
{
    return condition ? a : b;
}

static ulong IntegerHelpers_Pick_ulong(bool condition, ulong a, ulong b)
{
    return condition ? a : b;
}

static Tally Tally_new(ulong total, short delta)
{
    Tally __this = Tally{};
    __this.Total = total;
    __this.Delta = delta;
    __this.Count = uchar(1);
    return __this;
}

static void Tally_Add(thread Tally& __this, uchar value)
{
    __this.Total += ulong(value);
    __this.Count++;
}

static Tally Tally_op_Addition(Tally a, Tally b)
{
    return ::Tally_new(a.Total + b.Total, short(int(uint(a.Delta) + uint(b.Delta))));
}

static void Tally_Add_device(device Tally& __this, uchar value)
{
    __this.Total += ulong(value);
    __this.Count++;
}

static long Tally_Signed(Tally __this)
{
    return long(__this.Total + ulong(__this.Delta));
}

static ushort Tally_Scaled(Tally __this, uchar factor)
{
    return ushort(int(uint(__this.Count) * uint(factor)));
}

static long Tally_op_Explicit(Tally tally)
{
    return ::Tally_Signed(tally);
}

kernel void SharpCompute_Tests_Shaders_SharedIntegerCode(
    const device long* values [[buffer(0)]],
    const device uchar* bytes [[buffer(1)]],
    device Tally* tallies [[buffer(2)]],
    device long* output [[buffer(3)]],
    constant __Constants& __c [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Stride));
    long a = ::IntegerHelpers_Load_long(values, i);
    long a2 = values[(int(uint(i) + 1u)) % int(__c.__length_values)];
    uchar b = ::IntegerHelpers_Load_uchar(bytes, i);
    uchar b2 = bytes[(int(uint(i) + 1u)) % int(__c.__length_bytes)];
    ushort word = ushort(a);
    ulong hash = ::IntegerHelpers_Feed(::IntegerHelpers_Feed(ulong(a), b), ::IntegerHelpers_High(word));
    output[int(uint(o) + 0u)] = long(hash);
    output[int(uint(o) + 1u)] = ::IntegerHelpers_Widen(char(b), short(word));
    ::IntegerHelpers_Swap_long(a, a2);
    ::IntegerHelpers_Swap_uchar(b, b2);
    output[int(uint(o) + 2u)] = long(ulong(a) - 1UL);
    output[int(uint(o) + 3u)] = long(b);
    output[int(uint(o) + 4u)] = ::IntegerHelpers_Pick_long(a > a2, a, a2);
    output[int(uint(o) + 5u)] = long(::IntegerHelpers_Pick_uchar(int(b) > int(b2), b, b2));
    output[int(uint(o) + 6u)] = long(::IntegerHelpers_Pick_short(a > 0L, short(word), short(-1)));
    output[int(uint(o) + 7u)] = long(::IntegerHelpers_Pick_ulong(int(b) > 100, hash, 1UL << 40));
    Tally tally = ::Tally_new(ulong(a), short(-3));
    ::Tally_Add(tally, __c.step);
    ::Tally_Add(tally, b);
    Tally sum = ::Tally_op_Addition(tally, tallies[i]);
    ::Tally_Add_device(tallies[i], __c.step);
    output[int(uint(o) + 8u)] = ::Tally_Signed(sum);
    output[int(uint(o) + 9u)] = long(::Tally_Scaled(sum, __c.step));
    output[int(uint(o) + 10u)] = ::Tally_op_Explicit(tallies[i]);
    output[int(uint(o) + 11u)] = long(int(uint(tally.Count) + uint(sum.Count)));
}
