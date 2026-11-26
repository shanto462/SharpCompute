using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>Whole-shader snapshots of the MSL (and the interesting parts of the C#) for the reference samples.</summary>
public class ShaderSnapshotTests
{
    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    [Fact]
    public void MultiplyByTwo()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace MyApp.Shaders;

            [ThreadGroupSize(64, 1, 1)]
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct MultiplyByTwo : IComputeShader
            {
                public readonly ReadWriteBuffer<float> buffer;
                public readonly float factor;

                public MultiplyByTwo(ReadWriteBuffer<float> buffer, float factor) { this.buffer = buffer; this.factor = factor; }

                public void Execute() => buffer[ThreadIds.X] *= factor;
            }
            """);

        ShaderDescriptor descriptor = run.Verify("MyApp.Shaders.MultiplyByTwo");

        Assert.Equal("MyApp_Shaders_MultiplyByTwo", descriptor.EntryPoint);
        Assert.Equal((64, 1, 1), (descriptor.ThreadGroupSizeX, descriptor.ThreadGroupSizeY, descriptor.ThreadGroupSizeZ));
        Assert.Equal(
            Normalize("""
            #include <metal_stdlib>

            using namespace metal;

            struct __Constants
            {
                float factor;
            };
            static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, factor) == 0, "SharpCompute: offset of '__Constants.factor' does not match its C# layout");

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

            kernel void MyApp_Shaders_MultiplyByTwo(
                device float* buffer [[buffer(0)]],
                constant __Constants& __c [[buffer(1)]],
                constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
                uint3 __tid_in [[thread_position_in_grid]])
            {
                const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
                buffer[int(__tid.x)] *= __c.factor;
            }
            """),
            Normalize(descriptor.MslSource));

        // One file per shader, named after the fully qualified type.
        GeneratedSourceResult source = Assert.Single(run.Sources);
        Assert.Equal("MyApp.Shaders.MultiplyByTwo.SharpCompute.g.cs", source.HintName);

        string code = source.SourceText.ToString();
        Assert.Contains("partial struct MultiplyByTwo : global::SharpCompute.IComputeShaderDescriptor<global::MyApp.Shaders.MultiplyByTwo>", code);
        Assert.Contains("static int global::SharpCompute.IComputeShaderDescriptor<global::MyApp.Shaders.MultiplyByTwo>.GridWindowIndex => 2;", code);
        Assert.Equal(2, descriptor.GridWindowIndex);
        Assert.Contains(
            Normalize("""
                    static void global::SharpCompute.IComputeShaderDescriptor<global::MyApp.Shaders.MultiplyByTwo>.SetArguments(in global::MyApp.Shaders.MultiplyByTwo shader, ref global::SharpCompute.ShaderArgumentWriter writer)
                    {
                        writer.SetBuffer(0, shader.buffer ?? throw new global::System.ArgumentNullException("MultiplyByTwo.buffer", "The buffer 'MultiplyByTwo.buffer' is null. Create the shader with its constructor (not 'default') and pass a buffer."));
                        __Constants __constants = default;
                        __constants.factor = shader.factor;
                        writer.SetBytes(1, in __constants);
                    }

                    /// <summary>The C# mirror of the MSL <c>__Constants</c> struct bound after the buffers.</summary>
                    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
                    private struct __Constants
                    {
                        public float factor;
                    }
            """),
            code);
    }

    [Fact]
    public void Saxpy_WithConstantsAndBufferLengths()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace MyApp;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Saxpy : IComputeShader
            {
                public readonly ReadWriteBuffer<float> y;
                public readonly ReadOnlyBuffer<float> x;
                public readonly float a;

                public void Execute()
                {
                    int i = ThreadIds.X;

                    if (i >= x.Length || i >= y.Length)
                    {
                        return;
                    }

                    y[i] = a * x[i] + y[i];
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("MyApp.Saxpy");

        // No [ThreadGroupSize]: the runtime picks a size per dispatch rank.
        Assert.Equal((0, 0, 0), (descriptor.ThreadGroupSizeX, descriptor.ThreadGroupSizeY, descriptor.ThreadGroupSizeZ));
        Assert.Equal(
            Normalize("""
            #include <metal_stdlib>

            using namespace metal;

            struct __Constants
            {
                float a;
                uint __length_y;
                uint __length_x;
            };
            static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, a) == 0, "SharpCompute: offset of '__Constants.a' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, __length_y) == 4, "SharpCompute: offset of '__Constants.__length_y' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, __length_x) == 8, "SharpCompute: offset of '__Constants.__length_x' does not match its C# layout");

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

            kernel void MyApp_Saxpy(
                device float* y [[buffer(0)]],
                const device float* x [[buffer(1)]],
                constant __Constants& __c [[buffer(2)]],
                constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
                uint3 __tid_in [[thread_position_in_grid]])
            {
                const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
                int i = int(__tid.x);
                if (i >= int(__c.__length_x) || i >= int(__c.__length_y))
                {
                    return;
                }
                y[i] = __c.a * x[i] + y[i];
            }
            """),
            Normalize(descriptor.MslSource));

        Assert.Contains(
            Normalize("""
                        writer.SetBuffer(0, shader.y ?? throw new global::System.ArgumentNullException("Saxpy.y", "The buffer 'Saxpy.y' is null. Create the shader with its constructor (not 'default') and pass a buffer."));
                        writer.SetBuffer(1, shader.x ?? throw new global::System.ArgumentNullException("Saxpy.x", "The buffer 'Saxpy.x' is null. Create the shader with its constructor (not 'default') and pass a buffer."));
                        __Constants __constants = default;
                        __constants.a = shader.a;
                        __constants.__length_y = (uint)shader.y.Length;
                        __constants.__length_x = (uint)shader.x.Length;
                        writer.SetBytes(2, in __constants);
            """),
            run.Source("Saxpy.SharpCompute.g.cs"));

        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }

    [Fact]
    public void Mandelbrot_2DLoopWithHelperFunctions()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace Fractals;

            [ThreadGroupSize(16, 16)]
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Mandelbrot : IComputeShader
            {
                private const int MaxIterations = 256;

                public readonly ReadWriteBuffer<uint> output;
                public readonly Float2 origin;
                public readonly float scale;

                private static float LengthSquared(float x, float y) => x * x + y * y;

                private static int Iterate(float cx, float cy, int limit)
                {
                    float x = 0, y = 0;
                    int i = 0;

                    while (i < limit && LengthSquared(x, y) <= 4f)
                    {
                        float t = x * x - y * y + cx;
                        y = 2 * x * y + cy;
                        x = t;
                        i++;
                    }

                    return i;
                }

                public void Execute()
                {
                    Int2 id = ThreadIds.XY;
                    int width = DispatchSize.X;
                    float cx = origin.X + id.X * scale;
                    float cy = origin.Y + id.Y * scale;
                    int n = Iterate(cx, cy, MaxIterations);

                    output[id.Y * width + id.X] = n == MaxIterations ? 0u : (uint)n;
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Fractals.Mandelbrot");

        Assert.Equal((16, 16, 1), (descriptor.ThreadGroupSizeX, descriptor.ThreadGroupSizeY, descriptor.ThreadGroupSizeZ));
        Assert.Equal(
            Normalize("""
            #include <metal_stdlib>

            using namespace metal;

            constant int MaxIterations = 256;

            struct __Constants
            {
                packed_float2 origin;
                float scale;
            };
            static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, origin) == 0, "SharpCompute: offset of '__Constants.origin' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, scale) == 8, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");

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

            static float LengthSquared(float x, float y);
            static int Iterate(float cx, float cy, int limit);

            static float LengthSquared(float x, float y)
            {
                return x * x + y * y;
            }

            static int Iterate(float cx, float cy, int limit)
            {
                float x = 0.0f;
                float y = 0.0f;
                int i = 0;
                while (i < limit && ::LengthSquared(x, y) <= 4.0f)
                {
                    float t = x * x - y * y + cx;
                    y = 2.0f * x * y + cy;
                    x = t;
                    i = int(uint(i) + 1u);
                }
                return i;
            }

            kernel void Fractals_Mandelbrot(
                device uint* output [[buffer(0)]],
                constant __Constants& __c [[buffer(1)]],
                constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
                uint3 __tid_in [[thread_position_in_grid]],
                uint3 __grid_in [[threads_per_grid]])
            {
                const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
                const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
                int2 id = int2(__tid.xy);
                int width = int(__grid.x);
                float cx = __c.origin.x + float(id.x) * __c.scale;
                float cy = __c.origin.y + float(id.y) * __c.scale;
                int n = ::Iterate(cx, cy, ::MaxIterations);
                output[int(uint(id.y) * uint(width) + uint(id.x))] = n == ::MaxIterations ? 0u : uint(n);
            }
            """),
            Normalize(descriptor.MslSource));

        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }

    [Fact]
    public void UserStructElements_UsePackedFieldsAndDriftGuards()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            """
            using System;
            using SharpCompute;
            using TestVectors;

            namespace Physics;

            public struct Particle
            {
                public Vec3 Position;
                public float Mass;
                public Vec3 Velocity;
                public Half Charge;
                public IVec2 Cell;
                public int Age { get; set; }
            }

            public struct Settings
            {
                public float DeltaTime;
                public Vec2 Bounds;
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Integrate : IComputeShader
            {
                public readonly ReadWriteBuffer<Particle> particles;
                public readonly ReadOnlyBuffer<Vec3> forces;
                public readonly Settings settings;
                public readonly Half damping;
                public readonly Vec3 gravity;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    Particle p = particles[i];
                    Vec3 acceleration = (forces[i] + gravity) / p.Mass;

                    p.Velocity += acceleration * settings.DeltaTime;
                    p.Position += p.Velocity * settings.DeltaTime;
                    p.Position.X = Fn.Clamp(p.Position.X, settings.Bounds.X, settings.Bounds.Y);
                    p.Age++;
                    particles[i] = p;
                    particles[i].Velocity.XY = p.Velocity.XY * (float)damping;
                    particles[i].Cell = (IVec2)p.Position.XY;
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Physics.Integrate");

        Assert.Equal(
            Normalize("""
            #include <metal_stdlib>

            using namespace metal;

            // SharpCompute float and half conversions (MslPrelude.Conversions).
            inline int2 __ms_sat_int2(float2 x) { float2 y = metal::select(float2(0.0f), x, x == x); int2 r = int2(metal::clamp(y, float2(-2147483648.0f), float2(2147483520.0f))); return metal::select(r, int2(2147483647), x >= 2147483648.0f); }
            inline int2 __ms_sat_int2(half2 x) { return __ms_sat_int2(float2(x)); }

            static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");

            struct Particle
            {
                packed_float3 Position;
                float Mass;
                packed_float3 Velocity;
                half Charge;
                packed_int2 Cell;
                int Age;
            };
            static_assert(sizeof(Particle) == 44, "SharpCompute: size of 'Particle' does not match its C# layout");
            static_assert(__builtin_offsetof(Particle, Position) == 0, "SharpCompute: offset of 'Particle.Position' does not match its C# layout");
            static_assert(__builtin_offsetof(Particle, Mass) == 12, "SharpCompute: offset of 'Particle.Mass' does not match its C# layout");
            static_assert(__builtin_offsetof(Particle, Velocity) == 16, "SharpCompute: offset of 'Particle.Velocity' does not match its C# layout");
            static_assert(__builtin_offsetof(Particle, Charge) == 28, "SharpCompute: offset of 'Particle.Charge' does not match its C# layout");
            static_assert(__builtin_offsetof(Particle, Cell) == 32, "SharpCompute: offset of 'Particle.Cell' does not match its C# layout");
            static_assert(__builtin_offsetof(Particle, Age) == 40, "SharpCompute: offset of 'Particle.Age' does not match its C# layout");

            struct Settings
            {
                float DeltaTime;
                packed_float2 Bounds;
            };
            static_assert(sizeof(Settings) == 12, "SharpCompute: size of 'Settings' does not match its C# layout");
            static_assert(__builtin_offsetof(Settings, DeltaTime) == 0, "SharpCompute: offset of 'Settings.DeltaTime' does not match its C# layout");
            static_assert(__builtin_offsetof(Settings, Bounds) == 4, "SharpCompute: offset of 'Settings.Bounds' does not match its C# layout");

            struct __Constants
            {
                Settings settings;
                half damping;
                packed_float3 gravity;
            };
            static_assert(sizeof(__Constants) == 28, "SharpCompute: size of '__Constants' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, settings) == 0, "SharpCompute: offset of '__Constants.settings' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, damping) == 12, "SharpCompute: offset of '__Constants.damping' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, gravity) == 16, "SharpCompute: offset of '__Constants.gravity' does not match its C# layout");

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

            kernel void Physics_Integrate(
                device Particle* particles [[buffer(0)]],
                const device packed_float3* forces [[buffer(1)]],
                constant __Constants& __c [[buffer(2)]],
                constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
                uint3 __tid_in [[thread_position_in_grid]])
            {
                const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
                int i = int(__tid.x);
                Particle p = particles[i];
                float3 acceleration = (forces[i] + __c.gravity) / p.Mass;
                p.Velocity += acceleration * __c.settings.DeltaTime;
                p.Position += p.Velocity * __c.settings.DeltaTime;
                p.Position.x = metal::clamp(p.Position.x, __c.settings.Bounds.x, __c.settings.Bounds.y);
                p.Age = int(uint(p.Age) + 1u);
                particles[i] = p;
                particles[i].Velocity.xy = p.Velocity.xy * float(__c.damping);
                particles[i].Cell = __ms_sat_int2(p.Position.xy);
            }
            """),
            Normalize(descriptor.MslSource));

        // The numbers asserted in MSL must be the ones the CLR really uses.
        descriptor.AssertLayoutMatchesClr("Particle", run.LoadType("Physics.Particle"));
        descriptor.AssertLayoutMatchesClr("Settings", run.LoadType("Physics.Settings"));
        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }
}
