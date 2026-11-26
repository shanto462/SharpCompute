// SharpCompute.Tests.Conformance.Semantics.CompoundUserTargetShader (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_CompoundUserTargetShader, compile options None
#include <metal_stdlib>

using namespace metal;

struct TargetKey
{
    int V;
};
static_assert(sizeof(TargetKey) == 4, "SharpCompute: size of 'TargetKey' does not match its C# layout");
static_assert(__builtin_offsetof(TargetKey, V) == 0, "SharpCompute: offset of 'TargetKey.V' does not match its C# layout");

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

static TargetKey TargetKey_new(int v);
static int Next(device int* ticks);
static int Skip(device int* ticks);
static int TargetKey_Item(TargetKey __this, int i);
static int TargetKey_op_Implicit(TargetKey key);
static TargetKey TargetKey_op_Addition(TargetKey a, int b);

static TargetKey TargetKey_new(int v)
{
    TargetKey __this = TargetKey{};
    __this.V = v;
    return __this;
}

static int Next(device int* ticks)
{
    ticks[0] = int(uint(ticks[0]) + 1u);
    return ticks[0];
}

static int Skip(device int* ticks)
{
    return int(uint(::Next(ticks)) + 1u);
}

static int TargetKey_Item(TargetKey __this, int i)
{
    return int(uint(__this.V) + uint(i));
}

static int TargetKey_op_Implicit(TargetKey key)
{
    return key.V;
}

static TargetKey TargetKey_op_Addition(TargetKey a, int b)
{
    a.V = int(uint(a.V) + uint(b));
    return a;
}

kernel void SharpCompute_Tests_Conformance_Semantics_CompoundUserTargetShader(
    device int* ints [[buffer(0)]],
    device ushort* shorts [[buffer(1)]],
    device long* longs [[buffer(2)]],
    device int* ticks [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    if (int(__tid.x) != 0)
    {
        return;
    }
    TargetKey near = ::TargetKey_new(50);
    TargetKey far = ::TargetKey_new(80);
    reinterpret_cast<device uint&>(ints[::Next(ticks)]) += uint(2147483647);
    reinterpret_cast<device uint&>(ints[::Next(ticks)])++;
    reinterpret_cast<device uint&>(ints[::Next(ticks)]) *= 3u;
    reinterpret_cast<device uint&>(ints[::Skip(ticks)]) += uint(2147483647);
    reinterpret_cast<device uint&>(ints[::Skip(ticks)])++;
    reinterpret_cast<device uint&>(ints[::Skip(ticks)]) *= 3u;
    reinterpret_cast<device uint&>(ints[::TargetKey_Item(near, ::Next(ticks))]) += uint(2147483647);
    reinterpret_cast<device uint&>(ints[::TargetKey_Item(near, ::Next(ticks))])++;
    reinterpret_cast<device uint&>(ints[::TargetKey_Item(near, ::Next(ticks))]) *= 3u;
    reinterpret_cast<device uint&>(ints[::TargetKey_op_Implicit(::TargetKey_new(::Next(ticks)))]) += uint(2147483647);
    reinterpret_cast<device uint&>(ints[::TargetKey_op_Implicit(::TargetKey_new(::Next(ticks)))])++;
    reinterpret_cast<device uint&>(ints[::TargetKey_op_Implicit(::TargetKey_new(::Next(ticks)))]) *= 3u;
    reinterpret_cast<device uint&>(ints[(::TargetKey_op_Addition(far, ::Next(ticks))).V]) += uint(2147483647);
    reinterpret_cast<device uint&>(ints[(::TargetKey_op_Addition(far, ::Next(ticks))).V])++;
    reinterpret_cast<device uint&>(ints[(::TargetKey_op_Addition(far, ::Next(ticks))).V]) *= 3u;
    shorts[::Next(ticks)] += 1000;
    shorts[::Next(ticks)]++;
    shorts[::Next(ticks)] *= 65535u;
    shorts[::Skip(ticks)] += 1000;
    shorts[::Skip(ticks)]++;
    shorts[::Skip(ticks)] *= 65535u;
    shorts[::TargetKey_Item(near, ::Next(ticks))] += 1000;
    shorts[::TargetKey_Item(near, ::Next(ticks))]++;
    shorts[::TargetKey_Item(near, ::Next(ticks))] *= 65535u;
    shorts[::TargetKey_op_Implicit(::TargetKey_new(::Next(ticks)))] += 1000;
    shorts[::TargetKey_op_Implicit(::TargetKey_new(::Next(ticks)))]++;
    shorts[::TargetKey_op_Implicit(::TargetKey_new(::Next(ticks)))] *= 65535u;
    shorts[(::TargetKey_op_Addition(far, ::Next(ticks))).V] += 1000;
    shorts[(::TargetKey_op_Addition(far, ::Next(ticks))).V]++;
    shorts[(::TargetKey_op_Addition(far, ::Next(ticks))).V] *= 65535u;
    reinterpret_cast<device ulong&>(longs[::Next(ticks)]) += ulong(9223372036854775807L);
    reinterpret_cast<device ulong&>(longs[::Next(ticks)])++;
    reinterpret_cast<device ulong&>(longs[::Next(ticks)]) *= 3UL;
    reinterpret_cast<device ulong&>(longs[::Skip(ticks)]) += ulong(9223372036854775807L);
    reinterpret_cast<device ulong&>(longs[::Skip(ticks)])++;
    reinterpret_cast<device ulong&>(longs[::Skip(ticks)]) *= 3UL;
    reinterpret_cast<device ulong&>(longs[::TargetKey_Item(near, ::Next(ticks))]) += ulong(9223372036854775807L);
    reinterpret_cast<device ulong&>(longs[::TargetKey_Item(near, ::Next(ticks))])++;
    reinterpret_cast<device ulong&>(longs[::TargetKey_Item(near, ::Next(ticks))]) *= 3UL;
    reinterpret_cast<device ulong&>(longs[::TargetKey_op_Implicit(::TargetKey_new(::Next(ticks)))]) += ulong(9223372036854775807L);
    reinterpret_cast<device ulong&>(longs[::TargetKey_op_Implicit(::TargetKey_new(::Next(ticks)))])++;
    reinterpret_cast<device ulong&>(longs[::TargetKey_op_Implicit(::TargetKey_new(::Next(ticks)))]) *= 3UL;
    reinterpret_cast<device ulong&>(longs[(::TargetKey_op_Addition(far, ::Next(ticks))).V]) += ulong(9223372036854775807L);
    reinterpret_cast<device ulong&>(longs[(::TargetKey_op_Addition(far, ::Next(ticks))).V])++;
    reinterpret_cast<device ulong&>(longs[(::TargetKey_op_Addition(far, ::Next(ticks))).V]) *= 3UL;
    int used = int(reinterpret_cast<device uint&>(ints[::Next(ticks)]) += uint(2147483647));
    ints[120] = used;
    int post = int(reinterpret_cast<device uint&>(ints[::Next(ticks)])++);
    ints[121] = post;
    --reinterpret_cast<device uint&>(ints[::Next(ticks)]);
    reinterpret_cast<device uint&>(ints[::Skip(ticks)]) -= 9u;
    long widePost = long(reinterpret_cast<device ulong&>(longs[::Next(ticks)])++);
    longs[120] = widePost;
    ushort smallUsed = shorts[::Next(ticks)] *= 65535u;
    shorts[120] = smallUsed;
    TargetKey pure = ::TargetKey_new(100);
    reinterpret_cast<device uint&>(ints[::TargetKey_op_Implicit(pure)]) += 2u;
    reinterpret_cast<device uint&>(ints[::TargetKey_Item(pure, 1)]) -= 1u;
    reinterpret_cast<device uint&>(ints[(::TargetKey_op_Addition(pure, 2)).V]) *= 5u;
    reinterpret_cast<device uint&>(ints[(::TargetKey_op_Addition(pure, 3)).V])++;
}
