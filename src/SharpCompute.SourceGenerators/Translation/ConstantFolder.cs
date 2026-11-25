using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

internal enum ScalarKind
{
    Float,
    Int,
    UInt,
    Bool,
}

/// <summary>
/// A compile-time value of a scalar, vector, or matrix type. Components are stored as doubles that
/// hold exact float/int/uint values; matrices keep the C# constructor's argument order (rows), which
/// is also the order the MSL constructor reads (as columns).
/// </summary>
internal sealed class FoldedValue
{
    public FoldedValue(GpuType type, ScalarKind kind, double[] components)
    {
        Type = type;
        Kind = kind;
        Components = components;
    }

    public GpuType Type { get; }

    public ScalarKind Kind { get; }

    public double[] Components { get; }
}

/// <summary>
/// Evaluates the initializers of program-scope constants at compile time, so that calls to
/// <c>[MslIntrinsic]</c> functions (<c>Gpu.Normalize(new Float3(1, 2, 3))</c>) can be replaced by their
/// value. MSL functions are identified by their MSL name (from <c>[MslIntrinsic]</c>), never by a C#
/// name, and evaluated with the semantics of SharpCompute's CPU implementations (fma chains for dot,
/// cross, mix and reflect; power-of-two prescaling for length and normalize). Each float operation is
/// computed in double and rounded to float, which is exact for the basic operations and sqrt; the
/// transcendental functions agree with the GPU within a few ULP, as they do on the CPU.
/// </summary>
internal sealed class ConstantFolder
{
    private static readonly Regex MatrixName = new(@"^[a-z]+([234])x([234])$", RegexOptions.CultureInvariant);

    private readonly TypeResolver _types;
    private readonly INamedTypeSymbol _shaderType;
    private readonly Func<SyntaxTree, SemanticModel> _models;
    private readonly CancellationToken _cancellationToken;
    private readonly List<IFieldSymbol> _inProgress = new();

    public ConstantFolder(TypeResolver types, INamedTypeSymbol shaderType, Func<SyntaxTree, SemanticModel> models, CancellationToken cancellationToken)
    {
        _types = types;
        _shaderType = shaderType;
        _models = models;
        _cancellationToken = cancellationToken;
    }

    public bool TryEvaluate(IOperation operation, out FoldedValue? value, out string? reason)
    {
        try
        {
            value = Evaluate(operation);
            reason = null;
            return true;
        }
        catch (NotFoldableException e)
        {
            value = null;
            reason = e.Message;
            return false;
        }
    }

    private sealed class NotFoldableException : Exception
    {
        public NotFoldableException(string message)
            : base(message)
        {
        }
    }

    private static NotFoldableException Fail(string reason) => new(reason);

    private static NotFoldableException Fail(IOperation operation, string what) =>
        new($"{what} '{operation.Syntax}' cannot be evaluated at compile time");

    // ---------------------------------------------------------------------------------------------
    // Operations
    // ---------------------------------------------------------------------------------------------

    private FoldedValue Evaluate(IOperation operation)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if (operation.ConstantValue is { HasValue: true, Value: { } constant } && operation.Type is { } constantType)
        {
            return FromConstant(constant, constantType, operation);
        }

        switch (operation)
        {
            case IConversionOperation conversion:
                return Convert(Evaluate(conversion.Operand), Resolve(conversion.Type, conversion), conversion);

            case IObjectCreationOperation creation when creation.Initializer is null:
                return Construct(creation);

            case IDefaultValueOperation:
                return Zero(Resolve(operation.Type, operation), operation);

            case IUnaryOperation unary:
                return Unary(unary);

            case IBinaryOperation binary:
                return Binary(binary.OperatorKind, Evaluate(binary.LeftOperand), Evaluate(binary.RightOperand), Resolve(binary.Type, binary), binary);

            case IConditionalOperation { WhenFalse: { } whenFalse } conditional:
                return Evaluate(conditional.Condition).Components[0] != 0 ? Evaluate(conditional.WhenTrue) : Evaluate(whenFalse);

            case IFieldReferenceOperation field:
                return Field(field);

            case IPropertyReferenceOperation property:
                return Swizzle(property);

            case IInvocationOperation invocation:
                return Invocation(invocation);

            default:
                throw Fail(operation, "the expression");
        }
    }

    private GpuType Resolve(ITypeSymbol? type, IOperation operation) =>
        _types.Resolve(type) ?? throw Fail($"its type '{(type is null ? "?" : TypeResolver.Display(type))}' has no MSL equivalent");

    private static ScalarKind KindOf(GpuType type, IOperation operation)
    {
        if (type.Kind == GpuTypeKind.Bool)
        {
            return ScalarKind.Bool;
        }

        if (type.Kind is not (GpuTypeKind.Scalar or GpuTypeKind.Vector) || type.IsBoolean)
        {
            throw Fail(operation, $"a value of type '{type.MslName}' in");
        }

        if (type.IsFloating)
        {
            // Only 32-bit floats are folded; half arithmetic would need its own rounding.
            return type.Alignment == 4 ? ScalarKind.Float : throw Fail(operation, $"a value of type '{type.MslName}' in");
        }

        return type.IsUnsigned ? ScalarKind.UInt : ScalarKind.Int;
    }

    /// <summary>The number of components: 1 for scalars, n for vectors, n × m for matrices (all 4-byte scalars).</summary>
    private static int Count(GpuType type) => type.Kind == GpuTypeKind.Vector ? type.Size / 4 : 1;

    private static FoldedValue Make(GpuType type, IOperation operation, double[] components)
    {
        ScalarKind kind = KindOf(type, operation);
        return new FoldedValue(type, kind, components.Select(c => Round(kind, c, operation)).ToArray());
    }

    private FoldedValue FromConstant(object value, ITypeSymbol type, IOperation operation)
    {
        GpuType gpuType = Resolve(type, operation);

        double component = value switch
        {
            float f => f,
            int i => i,
            uint u => u,
            bool b => b ? 1 : 0,
            _ => throw Fail(operation, "the constant"),
        };

        return Make(gpuType, operation, new[] { component });
    }

    private static FoldedValue Zero(GpuType type, IOperation operation) => new(type, KindOf(type, operation), new double[Count(type)]);

    private FoldedValue Convert(FoldedValue value, GpuType target, IConversionOperation operation)
    {
        int count = Count(target);

        if (value.Components.Length != count && value.Components.Length != 1)
        {
            throw Fail(operation, "the conversion");
        }

        ScalarKind kind = KindOf(target, operation);
        var components = new double[count];

        for (int i = 0; i < count; i++)
        {
            components[i] = ConvertScalar(value.Components[value.Components.Length == 1 ? 0 : i], value.Kind, kind, operation);
        }

        return new FoldedValue(target, kind, components);
    }

    private static double ConvertScalar(double value, ScalarKind from, ScalarKind to, IOperation operation)
    {
        if (from == to || to == ScalarKind.Float)
        {
            return Round(to, value, operation);
        }

        if (from == ScalarKind.Float)
        {
            // Float to integer truncates; MSL and C# disagree outside the range, so do not fold that.
            double truncated = Math.Truncate(value);
            bool inRange = to == ScalarKind.Int ? truncated is >= int.MinValue and <= int.MaxValue : truncated is >= 0 and <= uint.MaxValue;

            return inRange ? truncated : throw Fail(operation, "the out-of-range conversion");
        }

        // int <-> uint: two's complement reinterpretation.
        return to == ScalarKind.Int ? unchecked((int)(uint)(long)value) : unchecked((uint)(int)(long)value);
    }

    private static double Round(ScalarKind kind, double value, IOperation operation) => kind switch
    {
        ScalarKind.Float => (float)value,
        ScalarKind.Int => unchecked((int)(long)value),
        ScalarKind.UInt => unchecked((uint)(long)value),
        _ => value != 0 ? 1 : 0,
    };

    private FoldedValue Construct(IObjectCreationOperation operation)
    {
        GpuType type = Resolve(operation.Type, operation);

        if (type.Kind is GpuTypeKind.Scalar or GpuTypeKind.Bool && operation.Arguments.Length == 0)
        {
            return Zero(type, operation);
        }

        if (type.Kind != GpuTypeKind.Vector)
        {
            throw Fail(operation, "the constructor call");
        }

        int count = Count(type);
        ScalarKind kind = KindOf(type, operation);
        var components = new List<double>();

        foreach (IArgumentOperation argument in operation.Arguments.OrderBy(static a => a.Parameter?.Ordinal ?? 0))
        {
            FoldedValue value = Evaluate(argument.Value);
            components.AddRange(value.Components.Select(c => ConvertScalar(c, value.Kind, kind, operation)));
        }

        if (components.Count == 0)
        {
            return Zero(type, operation);
        }

        if (components.Count == 1 && count > 1)
        {
            // `new Float3(v)` broadcasts, like `float3(v)`.
            components.AddRange(Enumerable.Repeat(components[0], count - 1));
        }

        return components.Count == count ? new FoldedValue(type, kind, components.ToArray()) : throw Fail(operation, "the constructor call");
    }

    private FoldedValue Unary(IUnaryOperation operation)
    {
        FoldedValue operand = Evaluate(operation.Operand);
        GpuType type = Resolve(operation.Type, operation);

        Func<double, double> apply = (operation.OperatorKind, operand.Kind) switch
        {
            (UnaryOperatorKind.Plus, _) => static x => x,
            (UnaryOperatorKind.Minus, ScalarKind.Float) => static x => -x,
            (UnaryOperatorKind.Minus, ScalarKind.Int) => static x => unchecked(-(int)x),
            (UnaryOperatorKind.BitwiseNegation, ScalarKind.Int) => static x => ~(int)x,
            (UnaryOperatorKind.BitwiseNegation, ScalarKind.UInt) => static x => ~(uint)x,
            (UnaryOperatorKind.Not, ScalarKind.Bool) => static x => x != 0 ? 0 : 1,
            _ => throw Fail(operation, "the operator"),
        };

        return Make(type, operation, operand.Components.Select(apply).ToArray());
    }

    private static FoldedValue Binary(BinaryOperatorKind op, FoldedValue left, FoldedValue right, GpuType result, IOperation operation)
    {
        int count = Math.Max(left.Components.Length, right.Components.Length);

        if ((left.Components.Length != count && left.Components.Length != 1) || (right.Components.Length != count && right.Components.Length != 1))
        {
            throw Fail(operation, "the operator");
        }

        ScalarKind kind = left.Kind;
        var components = new double[count];

        for (int i = 0; i < count; i++)
        {
            double a = left.Components[left.Components.Length == 1 ? 0 : i];
            double b = right.Components[right.Components.Length == 1 ? 0 : i];
            components[i] = BinaryScalar(op, kind, a, b, operation);
        }

        return Make(result, operation, components);
    }

    private static double BinaryScalar(BinaryOperatorKind op, ScalarKind kind, double a, double b, IOperation operation)
    {
        switch (op)
        {
            case BinaryOperatorKind.Equals:
                return a == b ? 1 : 0;
            case BinaryOperatorKind.NotEquals:
                return a != b ? 1 : 0;
            case BinaryOperatorKind.LessThan:
                return a < b ? 1 : 0;
            case BinaryOperatorKind.LessThanOrEqual:
                return a <= b ? 1 : 0;
            case BinaryOperatorKind.GreaterThan:
                return a > b ? 1 : 0;
            case BinaryOperatorKind.GreaterThanOrEqual:
                return a >= b ? 1 : 0;
            case BinaryOperatorKind.ConditionalAnd when kind == ScalarKind.Bool:
                return a != 0 && b != 0 ? 1 : 0;
            case BinaryOperatorKind.ConditionalOr when kind == ScalarKind.Bool:
                return a != 0 || b != 0 ? 1 : 0;
        }

        if (kind == ScalarKind.Float)
        {
            return op switch
            {
                BinaryOperatorKind.Add => F(a + b),
                BinaryOperatorKind.Subtract => F(a - b),
                BinaryOperatorKind.Multiply => F(a * b),
                BinaryOperatorKind.Divide => F(a / b),
                BinaryOperatorKind.Remainder => F(a % b), // C fmod, exact
                _ => throw Fail(operation, "the operator"),
            };
        }

        if (kind == ScalarKind.Int)
        {
            int x = (int)a, y = (int)b;

            return op switch
            {
                BinaryOperatorKind.Add => unchecked(x + y),
                BinaryOperatorKind.Subtract => unchecked(x - y),
                BinaryOperatorKind.Multiply => unchecked(x * y),
                BinaryOperatorKind.Divide when y != 0 && !(x == int.MinValue && y == -1) => x / y,
                BinaryOperatorKind.Remainder when y != 0 && !(x == int.MinValue && y == -1) => x % y,
                BinaryOperatorKind.And => x & y,
                BinaryOperatorKind.Or => x | y,
                BinaryOperatorKind.ExclusiveOr => x ^ y,
                BinaryOperatorKind.LeftShift => x << y,
                BinaryOperatorKind.RightShift => x >> y,
                _ => throw Fail(operation, "the operator"),
            };
        }

        if (kind == ScalarKind.UInt)
        {
            uint x = (uint)a, y = (uint)b;

            return op switch
            {
                BinaryOperatorKind.Add => unchecked(x + y),
                BinaryOperatorKind.Subtract => unchecked(x - y),
                BinaryOperatorKind.Multiply => unchecked(x * y),
                BinaryOperatorKind.Divide when y != 0 => x / y,
                BinaryOperatorKind.Remainder when y != 0 => x % y,
                BinaryOperatorKind.And => x & y,
                BinaryOperatorKind.Or => x | y,
                BinaryOperatorKind.ExclusiveOr => x ^ y,
                BinaryOperatorKind.LeftShift => x << (int)b,
                BinaryOperatorKind.RightShift => x >> (int)b,
                _ => throw Fail(operation, "the operator"),
            };
        }

        return op switch
        {
            BinaryOperatorKind.And => (a != 0) & (b != 0) ? 1 : 0,
            BinaryOperatorKind.Or => (a != 0) | (b != 0) ? 1 : 0,
            BinaryOperatorKind.ExclusiveOr => (a != 0) ^ (b != 0) ? 1 : 0,
            _ => throw Fail(operation, "the operator"),
        };
    }

    private FoldedValue Field(IFieldReferenceOperation operation)
    {
        IFieldSymbol field = operation.Field.OriginalDefinition;

        if (!field.IsStatic || !field.IsReadOnly || !IsShaderOrContainingType(field.ContainingType))
        {
            throw Fail(operation, "the field");
        }

        if (_inProgress.Contains(field, SymbolEqualityComparer.Default))
        {
            throw Fail($"the initializer of '{field.Name}' depends on itself");
        }

        var declarator = field.DeclaringSyntaxReferences.Select(r => r.GetSyntax(_cancellationToken)).OfType<VariableDeclaratorSyntax>().FirstOrDefault();

        if (declarator?.Initializer is not { } initializer ||
            _models(initializer.SyntaxTree).GetOperation(initializer, _cancellationToken) is not IFieldInitializerOperation { Value: { } value })
        {
            throw Fail($"'{field.Name}' has no initializer");
        }

        _inProgress.Add(field);

        try
        {
            return Evaluate(value);
        }
        finally
        {
            _inProgress.RemoveAt(_inProgress.Count - 1);
        }
    }

    private bool IsShaderOrContainingType(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = _shaderType; current is not null; current = current.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, type))
            {
                return true;
            }
        }

        return false;
    }

    private FoldedValue Swizzle(IPropertyReferenceOperation operation)
    {
        if (operation.Instance is null || operation.Property.IsIndexer || _types.Resolve(operation.Instance.Type) is not { Kind: GpuTypeKind.Vector })
        {
            throw Fail(operation, "the property");
        }

        FoldedValue instance = Evaluate(operation.Instance);
        string name = operation.Property.Name.ToLowerInvariant();
        var components = new double[name.Length];

        for (int i = 0; i < name.Length; i++)
        {
            int index = "xyzw".IndexOf(name[i]);

            if (index < 0 || index >= instance.Components.Length)
            {
                throw Fail(operation, "the property");
            }

            components[i] = instance.Components[index];
        }

        return Make(Resolve(operation.Type, operation), operation, components);
    }

    private FoldedValue Invocation(IInvocationOperation operation)
    {
        IMethodSymbol method = operation.TargetMethod;
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        GpuType result = Resolve(method.ReturnType, operation);
        var arguments = new List<FoldedValue>();

        if (operation.Instance is not null)
        {
            arguments.Add(Evaluate(operation.Instance));
        }

        foreach (IArgumentOperation argument in operation.Arguments.OrderBy(static a => a.Parameter?.Ordinal ?? 0))
        {
            if (argument.Parameter is { RefKind: RefKind.Out or RefKind.Ref })
            {
                throw Fail(operation, "the call");
            }

            arguments.Add(Evaluate(argument.Value));
        }

        if (TypeResolver.FindAttribute(definition, "MslBinaryOperatorAttribute") is { } binary &&
            binary.ConstructorArguments.Length == 1 && binary.ConstructorArguments[0].Value is string op && arguments.Count == 2)
        {
            bool swap = binary.NamedArguments.Any(static a => a.Key == "SwapOperands" && a.Value.Value is true);
            return MslOperator(op, swap ? arguments[1] : arguments[0], swap ? arguments[0] : arguments[1], result, operation);
        }

        string? name = TypeResolver.FindAttribute(definition, "MslIntrinsicAttribute") is { } attribute && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string intrinsic
            ? intrinsic
            : method.IsStatic && method.ContainingType is { ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }, Name: "MathF" or "Math" or "Single" }
                ? MathName(method)
                : null;

        if (name is null)
        {
            throw Fail($"it calls '{method.Name}', which is not an [MslIntrinsic] function; MSL does not allow function calls in program-scope initializers");
        }

        return Intrinsic(name, arguments, result, operation);
    }

    private static string? MathName(IMethodSymbol method) => method.Name switch
    {
        "Abs" or "Min" or "Max" or "Clamp" or "Sqrt" or "Sin" or "Cos" or "Tan" or "Asin" or "Acos" or "Atan" or "Atan2"
            or "Sinh" or "Cosh" or "Tanh" or "Exp" or "Log" or "Log2" or "Log10" or "Pow" or "Floor" or "Sign" or "CopySign" => "metal::" + method.Name.ToLowerInvariant(),
        "Ceiling" => "metal::ceil",
        "Truncate" => "metal::trunc",
        "Round" when method.Parameters.Length == 1 => "metal::rint",
        "FusedMultiplyAdd" => "metal::fma",
        "Lerp" => "metal::mix",
        "Exp2" => "metal::exp2",
        "Exp10" => "metal::exp10",
        "ReciprocalSqrtEstimate" => "metal::rsqrt",
        _ => null,
    };

    /// <summary>An MSL binary operator, including the matrix products of <c>*</c>.</summary>
    private static FoldedValue MslOperator(string op, FoldedValue left, FoldedValue right, GpuType result, IOperation operation)
    {
        int leftSize = MatrixSize(left.Type), rightSize = MatrixSize(right.Type);

        if (op == "*" && (leftSize > 0 || rightSize > 0) && left.Components.Length > 1 && right.Components.Length > 1)
        {
            return Make(result, operation, MatrixProduct(left, leftSize, right, rightSize, operation));
        }

        BinaryOperatorKind kind = op switch
        {
            "+" => BinaryOperatorKind.Add,
            "-" => BinaryOperatorKind.Subtract,
            "*" => BinaryOperatorKind.Multiply,
            "/" => BinaryOperatorKind.Divide,
            _ => throw Fail(operation, "the operator call"),
        };

        return Binary(kind, left, right, result, operation);
    }

    /// <summary>The dimension of a square MSL matrix type (<c>float3x3</c> → 3), or 0.</summary>
    private static int MatrixSize(GpuType type)
    {
        Match match = MatrixName.Match(type.MslName);
        return match.Success && match.Groups[1].Value == match.Groups[2].Value ? match.Groups[1].Value[0] - '0' : 0;
    }

    /// <summary>
    /// MSL <c>*</c> with a matrix operand. MSL matrices are column-major; component <c>c * n + r</c> is
    /// column <c>c</c>, row <c>r</c>. Sums are fma chains in index order, as on Apple GPUs.
    /// </summary>
    private static double[] MatrixProduct(FoldedValue left, int leftSize, FoldedValue right, int rightSize, IOperation operation)
    {
        double[] a = left.Components, b = right.Components;

        if (leftSize > 0 && rightSize > 0 && leftSize == rightSize)
        {
            int n = leftSize;
            var product = new double[n * n];

            for (int column = 0; column < n; column++)
            {
                for (int row = 0; row < n; row++)
                {
                    double sum = F(a[row] * b[column * n]);

                    for (int k = 1; k < n; k++)
                    {
                        sum = Fma(a[k * n + row], b[column * n + k], sum);
                    }

                    product[column * n + row] = sum;
                }
            }

            return product;
        }

        if (leftSize > 0 && b.Length == leftSize)
        {
            // m * v: sum over columns of column_k * v[k].
            int n = leftSize;
            var product = new double[n];

            for (int row = 0; row < n; row++)
            {
                double sum = F(a[row] * b[0]);

                for (int k = 1; k < n; k++)
                {
                    sum = Fma(b[k], a[k * n + row], sum);
                }

                product[row] = sum;
            }

            return product;
        }

        if (rightSize > 0 && a.Length == rightSize)
        {
            // v * m: dot(v, column_c) per column.
            int n = rightSize;
            var product = new double[n];

            for (int column = 0; column < n; column++)
            {
                double sum = F(a[0] * b[column * n]);

                for (int k = 1; k < n; k++)
                {
                    sum = Fma(a[k], b[column * n + k], sum);
                }

                product[column] = sum;
            }

            return product;
        }

        throw Fail(operation, "the matrix product");
    }

    // ---------------------------------------------------------------------------------------------
    // Intrinsics
    // ---------------------------------------------------------------------------------------------

    private static FoldedValue Intrinsic(string name, List<FoldedValue> arguments, GpuType result, IOperation operation)
    {
        string function = name.StartsWith("metal::", StringComparison.Ordinal) ? name.Substring(7) : name;
        ScalarKind kind = arguments.Count > 0 ? arguments[0].Kind : ScalarKind.Float;

        if (arguments.Any(a => a.Kind != kind))
        {
            throw Fail(operation, "the call");
        }

        // Geometric functions.
        if (kind == ScalarKind.Float)
        {
            double[]? geometric = (function, arguments.Count) switch
            {
                ("dot", 2) => new[] { Dot(arguments[0].Components, arguments[1].Components) },
                ("length", 1) => new[] { Length(arguments[0].Components) },
                ("distance", 2) => new[] { Length(Subtract(arguments[0].Components, arguments[1].Components)) },
                ("normalize", 1) => Normalize(arguments[0].Components),
                ("cross", 2) when arguments[0].Components.Length == 3 => Cross(arguments[0].Components, arguments[1].Components),
                ("reflect", 2) => Reflect(arguments[0].Components, arguments[1].Components),
                _ => null,
            };

            if (geometric is not null)
            {
                return Make(result, operation, geometric);
            }
        }

        Func<double[], double>? componentwise = kind == ScalarKind.Float ? FloatFunction(function, arguments.Count) : IntegerFunction(function, arguments.Count, kind);

        if (componentwise is null)
        {
            throw Fail($"the MSL function '{name}' cannot be evaluated at compile time; MSL does not allow function calls in program-scope initializers");
        }

        int count = arguments.Max(static a => a.Components.Length);

        if (arguments.Any(a => a.Components.Length != count && a.Components.Length != 1))
        {
            throw Fail(operation, "the call");
        }

        var components = new double[count];
        var values = new double[arguments.Count];

        for (int i = 0; i < count; i++)
        {
            for (int j = 0; j < arguments.Count; j++)
            {
                values[j] = arguments[j].Components[arguments[j].Components.Length == 1 ? 0 : i];
            }

            components[i] = componentwise(values);
        }

        // Some functions return another kind (float.IsNaN → bool, MathF.Sign → int).
        ScalarKind resultKind = KindOf(result, operation);
        return new FoldedValue(result, resultKind, components.Select(c => ConvertScalar(c, kind, resultKind, operation)).ToArray());
    }

    private static Func<double[], double>? FloatFunction(string name, int arity) => (name, arity) switch
    {
        ("abs", 1) => static a => Math.Abs(a[0]),
        ("min", 2) => static a => Min(a[0], a[1]),
        ("max", 2) => static a => Max(a[0], a[1]),
        ("clamp", 3) => static a => Min(Max(a[0], a[1]), a[2]),
        ("saturate", 1) => static a => Min(Max(a[0], 0), 1),
        ("sign", 1) => static a => a[0] > 0 ? 1 : a[0] < 0 ? -1 : 0,
        ("floor", 1) => static a => Math.Floor(a[0]),
        ("ceil", 1) => static a => Math.Ceiling(a[0]),
        ("trunc", 1) => static a => Math.Truncate(a[0]),
        ("round", 1) => static a => PositiveZero(Math.Round(a[0], MidpointRounding.AwayFromZero)),
        ("rint", 1) => static a => Math.Round(a[0], MidpointRounding.ToEven),
        ("fract", 1) => static a => double.IsInfinity(a[0]) ? 0 : Min(a[0] - Math.Floor(a[0]), 0.99999994f),
        ("fmod", 2) => static a => a[0] % a[1],
        ("sqrt", 1) => static a => F(Math.Sqrt(a[0])),
        ("rsqrt", 1) => static a => F(1.0 / Math.Sqrt(a[0])),
        ("pow", 2) => static a => F(Math.Pow(a[0], a[1])),
        ("exp", 1) => static a => F(Math.Exp(a[0])),
        ("exp2", 1) => static a => F(Math.Pow(2, a[0])),
        ("log", 1) => static a => F(Math.Log(a[0])),
        ("log2", 1) => static a => F(Math.Log(a[0]) / Math.Log(2)),
        ("log10", 1) => static a => F(Math.Log10(a[0])),
        ("exp10", 1) => static a => F(Math.Pow(10, a[0])),
        ("sin", 1) => static a => F(Math.Sin(a[0])),
        ("cos", 1) => static a => F(Math.Cos(a[0])),
        ("tan", 1) => static a => F(Math.Tan(a[0])),
        ("asin", 1) => static a => F(Math.Asin(a[0])),
        ("acos", 1) => static a => F(Math.Acos(a[0])),
        ("atan", 1) => static a => PositiveZero(F(Math.Atan(a[0]))),
        ("atan2", 2) => static a => F(Math.Atan2(a[0], a[1])),
        ("sinh", 1) => static a => F(Math.Sinh(a[0])),
        ("cosh", 1) => static a => F(Math.Cosh(a[0])),
        ("tanh", 1) => static a => F(Math.Tanh(a[0])),
        ("copysign", 2) => static a => BitConverter.DoubleToInt64Bits(a[1]) < 0 ? -Math.Abs(a[0]) : Math.Abs(a[0]),
        ("fma", 3) => static a => Fma(a[0], a[1], a[2]),
        ("mix", 3) => static a => Fma(a[2], a[1], Fma(-a[2], a[0], a[0])),
        ("step", 2) => static a => a[1] >= a[0] ? 1 : 0,
        ("smoothstep", 3) => static a => Smoothstep(a[0], a[1], a[2]),
        ("isnan", 1) => static a => double.IsNaN(a[0]) ? 1 : 0,
        ("isinf", 1) => static a => double.IsInfinity(a[0]) ? 1 : 0,
        ("isfinite", 1) => static a => !double.IsNaN(a[0]) && !double.IsInfinity(a[0]) ? 1 : 0,
        ("__ms_radians", 1) => static a => F(a[0] * (float)0.017453292519943295),
        ("__ms_degrees", 1) => static a => F(a[0] * (float)57.29577951308232),
        _ => null,
    };

    private static Func<double[], double>? IntegerFunction(string name, int arity, ScalarKind kind) => (name, arity) switch
    {
        ("abs", 1) => kind == ScalarKind.Int ? static a => a[0] < 0 ? unchecked(-(int)a[0]) : a[0] : static a => a[0],
        ("min", 2) => static a => Math.Min(a[0], a[1]),
        ("max", 2) => static a => Math.Max(a[0], a[1]),
        ("clamp", 3) => static a => Math.Min(Math.Max(a[0], a[1]), a[2]),
        _ => null,
    };

    /// <summary>Rounds to float, as every float operation on the GPU does.</summary>
    private static double F(double value) => (float)value;

    /// <summary>
    /// <c>fma(a, b, c)</c> on floats: the product of two floats is exact in double, so only the sum is
    /// rounded twice (to double, then to float), which differs from a single rounding only in rare ties.
    /// </summary>
    private static double Fma(double a, double b, double c) => F((a * b) + c);

    private static double PositiveZero(double value) => value == 0 ? 0 : value;

    /// <summary><c>fmin</c>: when exactly one argument is NaN, returns the other.</summary>
    private static double Min(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Min(a, b);

    private static double Max(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);

    private static double Smoothstep(double edge0, double edge1, double x)
    {
        double t = Min(Max(F(F(x - edge0) / F(edge1 - edge0)), 0), 1);
        return F(F(t * t) * F(3 - F(2 * t)));
    }

    private static double Dot(double[] x, double[] y)
    {
        double sum = F(x[0] * y[0]);

        for (int i = 1; i < x.Length; i++)
        {
            sum = Fma(x[i], y[i], sum);
        }

        return sum;
    }

    private static double[] Subtract(double[] x, double[] y) => x.Select((v, i) => F(v - y[i])).ToArray();

    /// <summary>The exponent of a finite, non-zero value: <c>ilogb</c>.</summary>
    private static int ILogB(double value)
    {
        value = Math.Abs(value);
        int exponent = 0;

        while (value >= 2)
        {
            value /= 2;
            exponent++;
        }

        while (value < 1)
        {
            value *= 2;
            exponent--;
        }

        return exponent;
    }

    private static double MaxAbs(double[] x) => x.Aggregate(0.0, static (m, v) => Max(m, Math.Abs(v)));

    private static double Length(double[] x)
    {
        double m = MaxAbs(x);

        if (m == 0 || double.IsNaN(m) || double.IsInfinity(m))
        {
            return F(Math.Sqrt(Dot(x, x)));
        }

        int e = ILogB(m);
        double[] s = x.Select(v => v * Math.Pow(2, -e)).ToArray();
        return F(F(Math.Sqrt(Dot(s, s))) * Math.Pow(2, e));
    }

    private static double[] Normalize(double[] x)
    {
        double m = MaxAbs(x);

        if (m == 0)
        {
            return x;
        }

        if (double.IsNaN(m) || double.IsInfinity(m))
        {
            return x.Select(static _ => double.NaN).ToArray();
        }

        int e = ILogB(m);
        double[] s = x.Select(v => v * Math.Pow(2, -e)).ToArray();
        double r = F(1.0 / Math.Sqrt(Dot(s, s)));
        return s.Select(v => F(v * r)).ToArray();
    }

    private static double[] Cross(double[] x, double[] y) => new[]
    {
        Fma(x[1], y[2], -F(x[2] * y[1])),
        Fma(x[2], y[0], -F(x[0] * y[2])),
        Fma(x[0], y[1], -F(x[1] * y[0])),
    };

    private static double[] Reflect(double[] i, double[] n)
    {
        double k = F(-2 * Dot(n, i));
        return i.Select((v, index) => Fma(k, n[index], v)).ToArray();
    }
}
