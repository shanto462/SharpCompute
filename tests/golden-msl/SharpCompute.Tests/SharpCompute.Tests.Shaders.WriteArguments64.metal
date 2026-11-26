// SharpCompute.Tests.Shaders.WriteArguments64 (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_WriteArguments64, compile options None
#include <metal_stdlib>

using namespace metal;

struct DispatchArguments
{
    uint ThreadGroupsX;
    uint ThreadGroupsY;
    uint ThreadGroupsZ;
};
static_assert(sizeof(DispatchArguments) == 12, "SharpCompute: size of 'DispatchArguments' does not match its C# layout");
static_assert(__builtin_offsetof(DispatchArguments, ThreadGroupsX) == 0, "SharpCompute: offset of 'DispatchArguments.ThreadGroupsX' does not match its C# layout");
static_assert(__builtin_offsetof(DispatchArguments, ThreadGroupsY) == 4, "SharpCompute: offset of 'DispatchArguments.ThreadGroupsY' does not match its C# layout");
static_assert(__builtin_offsetof(DispatchArguments, ThreadGroupsZ) == 8, "SharpCompute: offset of 'DispatchArguments.ThreadGroupsZ' does not match its C# layout");

struct __Constants
{
    int slot;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, slot) == 0, "SharpCompute: offset of '__Constants.slot' does not match its C# layout");

kernel void SharpCompute_Tests_Shaders_WriteArguments64(
    device int* count [[buffer(0)]],
    device DispatchArguments* arguments [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]])
{
    DispatchArguments result = DispatchArguments{};
    result.ThreadGroupsX = uint((int(uint(count[0]) + 63u)) / 64);
    result.ThreadGroupsY = 1u;
    result.ThreadGroupsZ = 1u;
    arguments[__c.slot] = result;
}
