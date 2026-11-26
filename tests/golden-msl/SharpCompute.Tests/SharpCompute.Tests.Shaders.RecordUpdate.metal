// SharpCompute.Tests.Shaders.RecordUpdate (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_RecordUpdate, compile options None
#include <metal_stdlib>

using namespace metal;

struct MixedRecord
{
    uchar Tag;
    long Key;
    short Delta;
    ulong Hash;
    char Sign;
    ushort Count;
};
static_assert(sizeof(MixedRecord) == 40, "SharpCompute: size of 'MixedRecord' does not match its C# layout");
static_assert(__builtin_offsetof(MixedRecord, Tag) == 0, "SharpCompute: offset of 'MixedRecord.Tag' does not match its C# layout");
static_assert(__builtin_offsetof(MixedRecord, Key) == 8, "SharpCompute: offset of 'MixedRecord.Key' does not match its C# layout");
static_assert(__builtin_offsetof(MixedRecord, Delta) == 16, "SharpCompute: offset of 'MixedRecord.Delta' does not match its C# layout");
static_assert(__builtin_offsetof(MixedRecord, Hash) == 24, "SharpCompute: offset of 'MixedRecord.Hash' does not match its C# layout");
static_assert(__builtin_offsetof(MixedRecord, Sign) == 32, "SharpCompute: offset of 'MixedRecord.Sign' does not match its C# layout");
static_assert(__builtin_offsetof(MixedRecord, Count) == 34, "SharpCompute: offset of 'MixedRecord.Count' does not match its C# layout");

struct __Constants
{
    MixedRecord bias;
};
static_assert(sizeof(__Constants) == 40, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, bias) == 0, "SharpCompute: offset of '__Constants.bias' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_RecordUpdate(
    device MixedRecord* records [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    MixedRecord record = records[i];
    record.Tag += int(__c.bias.Tag);
    record.Key = long((ulong(record.Key) * 31UL) + ulong(__c.bias.Key));
    record.Delta -= int(__c.bias.Delta);
    record.Hash ^= __c.bias.Hash >> (i & 63);
    record.Sign = char(int(uint(record.Sign) + uint(__c.bias.Sign)));
    record.Count++;
    records[i] = record;
    records[i].Key = long(ulong(records[i].Key) + ulong(record.Tag));
    records[i].Count += int(ushort(i));
}
