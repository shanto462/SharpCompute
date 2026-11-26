// SharpCompute.Tests.Threadgroups.GroupAtomicEveryOperation (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_GroupAtomicEveryOperation, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 9;

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

static uint Mix(uint x);
static int InitialInt(int slot);
static uint InitialUInt(int slot);

static uint Mix(uint x)
{
    x *= 2654435761u;
    x ^= x >> 15;
    return x;
}

static int InitialInt(int slot)
{
    return slot == 2 ? 2147483647 : slot == 3 ? -2147483647 - 1 : slot == 4 ? -1 : 0;
}

static uint InitialUInt(int slot)
{
    return slot == 2 || slot == 4 ? 4294967295u : 0u;
}

kernel void SharpCompute_Tests_Threadgroups_GroupAtomicEveryOperation(
    device int* ints [[buffer(0)]],
    device uint* uints [[buffer(1)]],
    device int* addOrder [[buffer(2)]],
    device uint* uintAddOrder [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gid_in [[threadgroup_position_in_grid]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    threadgroup int sharedInts[9];
    threadgroup uint sharedUInts[9];
    threadgroup int order[256];
    threadgroup uint uintOrder[256];
    int i = int(__tid.x);
    int local = int(__gindex);
    uint u = uint(i);
    int mixed = int(::Mix(u));
    if (local == 0)
    {
        for (int slot = 0; slot < ::Slots; slot = int(uint(slot) + 1u))
        {
            sharedInts[slot] = ::InitialInt(slot);
            sharedUInts[slot] = ::InitialUInt(slot);
        }
    }
    order[local] = 0;
    uintOrder[local] = 0u;
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    reinterpret_cast<threadgroup uint&>(order[metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(sharedInts[0]), 1, metal::memory_order_relaxed)]) += 1u;
    uintOrder[int(metal::atomic_fetch_add_explicit((threadgroup metal::atomic_uint*)&(sharedUInts[0]), 1u, metal::memory_order_relaxed))] += 1u;
    metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_int*)&(sharedInts[1]), 3, metal::memory_order_relaxed);
    metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_uint*)&(sharedUInts[1]), 3u, metal::memory_order_relaxed);
    metal::atomic_fetch_min_explicit((threadgroup metal::atomic_int*)&(sharedInts[2]), mixed, metal::memory_order_relaxed);
    metal::atomic_fetch_min_explicit((threadgroup metal::atomic_uint*)&(sharedUInts[2]), uint(mixed), metal::memory_order_relaxed);
    metal::atomic_fetch_max_explicit((threadgroup metal::atomic_int*)&(sharedInts[3]), mixed, metal::memory_order_relaxed);
    metal::atomic_fetch_max_explicit((threadgroup metal::atomic_uint*)&(sharedUInts[3]), uint(mixed), metal::memory_order_relaxed);
    metal::atomic_fetch_and_explicit((threadgroup metal::atomic_int*)&(sharedInts[4]), ~(1 << (i % 12)), metal::memory_order_relaxed);
    metal::atomic_fetch_and_explicit((threadgroup metal::atomic_uint*)&(sharedUInts[4]), ~(1u << (i % 12)), metal::memory_order_relaxed);
    metal::atomic_fetch_or_explicit((threadgroup metal::atomic_int*)&(sharedInts[5]), 1 << (i % 20), metal::memory_order_relaxed);
    metal::atomic_fetch_or_explicit((threadgroup metal::atomic_uint*)&(sharedUInts[5]), 1u << (i % 20), metal::memory_order_relaxed);
    metal::atomic_fetch_xor_explicit((threadgroup metal::atomic_int*)&(sharedInts[6]), mixed, metal::memory_order_relaxed);
    metal::atomic_fetch_xor_explicit((threadgroup metal::atomic_uint*)&(sharedUInts[6]), uint(mixed), metal::memory_order_relaxed);
    metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(sharedInts[8]), metal::atomic_exchange_explicit((threadgroup metal::atomic_int*)&(sharedInts[7]), int(uint(i) + 1u), metal::memory_order_relaxed), metal::memory_order_relaxed);
    metal::atomic_fetch_add_explicit((threadgroup metal::atomic_uint*)&(sharedUInts[8]), metal::atomic_exchange_explicit((threadgroup metal::atomic_uint*)&(sharedUInts[7]), u + 1u, metal::memory_order_relaxed), metal::memory_order_relaxed);
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    if (local == 0)
    {
        int row = int(__gid.x * uint(::Slots));
        for (int slot = 0; slot < ::Slots; slot = int(uint(slot) + 1u))
        {
            ints[int(uint(row) + uint(slot))] = sharedInts[slot];
            uints[int(uint(row) + uint(slot))] = sharedUInts[slot];
        }
    }
    addOrder[i] = order[local];
    uintAddOrder[i] = uintOrder[local];
}
