# Sharing code between shaders

A shader is not limited to its own methods. It can call static methods of any type, generic methods,
and the members of your structs. All of it is translated to MSL from its C# source and emitted once
in every shader that uses it. The same code runs on the CPU.

- [Example](#example)
- [C# and MSL](#c-and-msl)
- [Rules](#rules)
- [Demo](#demo)

## Example

```csharp
public static class ShaderMath
{
    public const float Tau = 6.2831853f;                                  // inlined where it is used
    public static readonly Float3 SunDirection = Gpu.Normalize(new Float3(0.55f, 0.75f, -0.45f)); // an MSL constant

    public static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        return x;
    }

    public static T Pick<T>(bool condition, T whenTrue, T whenFalse) => condition ? whenTrue : whenFalse;
}

public struct Ray
{
    public Float3 Origin;
    public Float3 Direction;

    public Ray(Float3 origin, Float3 direction)
    {
        Origin = origin;
        Direction = Gpu.Normalize(direction);
    }

    public readonly Float3 At(float distance) => Origin + (Direction * distance);
}

public struct Sphere
{
    public Float3 Center;
    public float Radius;

    public readonly float Intersect(Ray ray)
    {
        Float3 offset = ray.Origin - Center;
        float b = Gpu.Dot(offset, ray.Direction);
        float discriminant = (b * b) - (Gpu.Dot(offset, offset) - (Radius * Radius));

        return discriminant < 0f ? -1f : -b - Gpu.Sqrt(discriminant);
    }
}

public struct RandomStream(uint seed)
{
    public float Next()                       // modifies the struct it is called on
    {
        seed = ShaderMath.Hash(seed + 0x9E3779B9u);
        return (seed >> 8) * (1f / 16777216f);
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct Depth(ReadWriteBuffer<float> depth, ReadOnlyBuffer<Sphere> spheres, int width) : IComputeShader
{
    private float Aspect => (float)width / DispatchSize.Y;          // a computed property of the shader

    public void Execute()
    {
        RandomStream random = new((uint)((ThreadIds.Y * width) + ThreadIds.X));
        Float2 point = (ThreadIds.Normalized.XY * 2f) - 1f;
        Ray ray = new(new Float3(0f, 1f, -5f), new Float3(point.X * Aspect, -point.Y, 2f + (random.Next() * 0.01f)));
        float nearest = -1f;

        for (int i = 0; i < spheres.Length; i++)
        {
            float distance = spheres[i].Intersect(ray);                // a method, called on a buffer element
            nearest = ShaderMath.Pick(distance > 0f && (nearest < 0f || distance < nearest), distance, nearest);
        }

        depth[(ThreadIds.Y * width) + ThreadIds.X] = nearest < 0f ? 0f : ray.At(nearest).Z;
    }
}
```

## C# and MSL

| C# | MSL |
|---|---|
| A static method of any class or struct: `ShaderMath.Hash(x)`, an extension method `x.Squared()` | `static uint ShaderMath_Hash(uint x)`, named after its type so that two types can each have a `Hash` |
| A C# 14 static extension member or extension operator: `Float3.Unit`, `a ^ b` | a function named after the containing class, like any static method; instance extension members are not supported ([Extension members](shader-language.md#extension-members)) |
| A generic method: `ShaderMath.Pick(c, a, b)` | one function per set of type arguments: `ShaderMath_Pick_float`, `ShaderMath_Pick_float3` |
| A `const` or `static readonly` field of any type | a `const` is inlined; a `static readonly` field is a program-scope `constant` (`ShaderMath_SunDirection`) |
| A method or computed property of a struct: `ray.At(t)`, `body.Energy`, an indexer | a function that takes the struct first: `Ray_At(Ray __this, float distance)` |
| A method that modifies its struct: `random.Next()`, `particles[i].Step(dt)` | the struct by reference: `thread RandomStream& __this` for a local, `device Particle& __this` for a buffer element, `threadgroup` for a `[GroupShared]` element; one function per address space used |
| A constructor: `new Ray(origin, direction)`, a primary constructor, a `record struct` | a function that returns the struct (`Ray_new`): zeroed, then field initializers or `: this(...)`, then the body |
| An operator or conversion a struct declares: `a + b`, `-a`, `a += b`, `(float)a` | a function: `Complex_op_Addition(a, b)` |
| An instance method, computed property or indexer of the shader, called on `this` | a function that receives the captured state it reads as hidden parameters |

## Rules

- **The source has to be in the same project as the shader.** A helper is translated from its C#
  source, which a referenced assembly does not contain: calling a method of another assembly is build
  error `SCG0016`. To share a file between projects, link it into each one
  (`<Compile Include="..\Shared\*.cs" LinkBase="Shared" />`), as Glowfall and Pulsewake do with
  [`samples/SharpCompute.Samples.Shared`](../samples/SharpCompute.Samples.Shared).
- **Shared code can do what the shader can.** `ThreadIds`, `DispatchSize` and the group built-ins,
  atomics on a buffer it is passed, barriers and SIMD-group operations all work in a static method of
  another type, and reach it the way they reach the shader's own helpers. What it cannot see is the
  shader's captured state: pass what it needs as arguments (buffers and textures included).
- **It runs on the CPU unchanged.** Nothing here is special to the generator, so `ForCpu` runs the
  same methods, and so can your own code and tests: `sphere.Intersect(ray)` is just C#.
- **A method that does not modify its struct takes a copy**, wherever the struct lives (a local, a
  buffer element, captured state, threadgroup memory). One that does modify it takes a reference.
  Where C# would call it on a copy (a temporary, a `readonly` field, an `in` parameter, an element of
  a `ReadOnlyBuffer`, captured state of a `readonly` shader), the MSL calls it on a copy too, so what
  it changes is discarded in both. Whether a method modifies its struct is read from its body, not
  from the `readonly` keyword, so structs need no annotations.
- **The usual rules apply.** Recursion is `SCG0014`, a local function that captures is `SCG0025`, and
  `counter.Next() + counter.Next()` is `SCG0021` (a method that modifies its struct writes it, and MSL
  does not say which call comes first), as is `data[0] += Bump(0)` when `Bump` writes `data` (what a
  helper does to buffers counts at its call; see [Evaluation order](shader-language.md#evaluation-order)).
- **Two more errors.** Assigning a computed property is `SCG0045` (only getters are translated), and
  a generic method called with a type MSL does not have, such as `double`, is `SCG0046` at the call.
- **Not translated:** property setters with a body, object initializers (`new T { ... }`), `with`
  expressions, user-defined `++`/`--`, generic structs and interface calls. A by-reference
  (`ref`/`out`) argument has to be a local or a parameter, not a buffer element.

## Demo

`scripts/demo-computelab.sh --page shared-code` shows it live: a small path-traced scene whose two
shaders are built from one static class and four structs, with the GPU time and a `ForCpu` check of
every 8th pixel.

![ComputeLab's "Shared code" page: a path-traced scene of matte and mirror spheres on a checkerboard under a blue sky, traced on the GPU by a shader built from a ShaderMath static class and Ray, Sphere, Camera and RandomStream structs; next to it the members that are used, and the same scene traced by ForCpu at every 8th pixel](screenshots/computelab-shared-code.png)
