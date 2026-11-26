// SharpCompute.Tests.CodeReuse.MoveBodies (SharpCompute.Tests): entry point SharpCompute_Tests_CodeReuse_MoveBodies, compile options None
#include <metal_stdlib>

using namespace metal;

struct Body
{
    packed_float2 Position;
    packed_float2 Velocity;
    float Mass;
};
static_assert(sizeof(Body) == 20, "SharpCompute: size of 'Body' does not match its C# layout");
static_assert(__builtin_offsetof(Body, Position) == 0, "SharpCompute: offset of 'Body.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Body, Velocity) == 8, "SharpCompute: offset of 'Body.Velocity' does not match its C# layout");
static_assert(__builtin_offsetof(Body, Mass) == 16, "SharpCompute: offset of 'Body.Mass' does not match its C# layout");

struct __Constants
{
    Body anchor;
    float dt;
};
static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, anchor) == 0, "SharpCompute: offset of '__Constants.anchor' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, dt) == 20, "SharpCompute: offset of '__Constants.dt' does not match its C# layout");

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

static void Body_Step(thread Body& __this, float dt);
static void Body_Kick(thread Body& __this, float2 impulse);
static void Body_Step_device(device Body& __this, float dt);
static void Body_Kick_device(device Body& __this, float2 impulse);
static float2 Body_Momentum(Body __this);
static void Body_Step_threadgroup(threadgroup Body& __this, float dt);
static void Body_Step_copy(Body __this, float dt);
static void Body_Kick_copy(Body __this, float2 impulse);
static Body Body_new(float2 position, float2 velocity);
static Body Body_op_UnaryNegation(Body body);
static Body Body_op_Addition(Body a, Body b);
static Body Body_new_1(float2 position);
static Body Body_op_Implicit(float2 position);
static float Body_Energy(Body __this);
static float Body_op_Explicit(Body body);

static void Body_Step(thread Body& __this, float dt)
{
    __this.Position += __this.Velocity * dt;
}

static void Body_Kick(thread Body& __this, float2 impulse)
{
    __this.Velocity += impulse / __this.Mass;
    ::Body_Step(__this, 0.5f);
}

static void Body_Step_device(device Body& __this, float dt)
{
    __this.Position += __this.Velocity * dt;
}

static void Body_Kick_device(device Body& __this, float2 impulse)
{
    __this.Velocity += impulse / __this.Mass;
    ::Body_Step_device(__this, 0.5f);
}

static float2 Body_Momentum(Body __this)
{
    return __this.Velocity * __this.Mass;
}

static void Body_Step_threadgroup(threadgroup Body& __this, float dt)
{
    __this.Position += __this.Velocity * dt;
}

static void Body_Step_copy(Body __this, float dt)
{
    __this.Position += __this.Velocity * dt;
}

static void Body_Kick_copy(Body __this, float2 impulse)
{
    __this.Velocity += impulse / __this.Mass;
    ::Body_Step(__this, 0.5f);
}

static Body Body_new(float2 position, float2 velocity)
{
    Body __this = Body{};
    __this.Mass = 1.0f;
    __this.Position = position;
    __this.Velocity = velocity;
    return __this;
}

static Body Body_op_UnaryNegation(Body body)
{
    return ::Body_new(body.Position, -body.Velocity);
}

static Body Body_op_Addition(Body a, Body b)
{
    return ::Body_new((a.Position + b.Position) * 0.5f, a.Velocity + b.Velocity);
}

static Body Body_new_1(float2 position)
{
    Body __this = Body{};
    __this = ::Body_new(position, float2(0.0f, 0.0f));
    __this.Mass = 2.0f;
    return __this;
}

static Body Body_op_Implicit(float2 position)
{
    return ::Body_new_1(position);
}

static float Body_Energy(Body __this)
{
    return 0.5f * __this.Mass * metal::dot(__this.Velocity, __this.Velocity);
}

static float Body_op_Explicit(Body body)
{
    return body.Mass;
}

kernel void SharpCompute_Tests_CodeReuse_MoveBodies(
    device Body* bodies [[buffer(0)]],
    const device Body* seeds [[buffer(1)]],
    device float* report [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup Body scratch[8];
    int i = int(__tid.x);
    Body local = seeds[i];
    ::Body_Kick(local, float2(0.5f, -0.25f));
    ::Body_Step_device(bodies[i], __c.dt);
    ::Body_Kick_device(bodies[i], ::Body_Momentum(local));
    scratch[int(__gtid.x)] = local;
    ::Body_Step_threadgroup(scratch[int(__gtid.x)], __c.dt * 2.0f);
    ::Body_Step_copy(seeds[i], 100.0f);
    ::Body_Kick_copy(__c.anchor, float2(100.0f, 100.0f));
    Body merged = ::Body_op_UnaryNegation(::Body_op_Addition(bodies[i], scratch[int(__gtid.x)]));
    merged = ::Body_op_Addition(merged, __c.anchor);
    Body placed = ::Body_op_Implicit(local.Position);
    Body fresh = ::Body_new(merged.Position, placed.Velocity);
    report[int((uint(i) * 4u) + 0u)] = ::Body_Energy(merged) + ::Body_Energy(__c.anchor);
    report[int((uint(i) * 4u) + 1u)] = ::Body_op_Explicit(placed) + placed.Position.x + fresh.Mass;
    report[int((uint(i) * 4u) + 2u)] = seeds[i].Position.x + ::Body_Energy(seeds[i]) + __c.anchor.Position.y;
    report[int((uint(i) * 4u) + 3u)] = scratch[int(__gtid.x)].Position.y + ::Body_Momentum(merged).x;
}
