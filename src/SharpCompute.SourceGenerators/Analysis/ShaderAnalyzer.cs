using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// Turns one <c>[GeneratedComputeShaderDescriptor]</c> struct into a <see cref="ShaderResult"/>:
/// validates the declaration, collects the captured state, translates <c>Execute</c> to MSL, and
/// lays out the binding ABI: buffers at <c>[[buffer(0..n-1)]]</c>, then one packed constants struct;
/// textures at <c>[[texture(0..m-1)]]</c>, or from 1 in a pixel shader, whose output is texture 0.
/// </summary>
internal sealed class ShaderAnalyzer
{
    /// <summary>Metal exposes buffer argument table indices 0..30.</summary>
    private const int MaxBoundArguments = 31;

    /// <summary>A compute encoder's texture argument table has the same 31 entries, counted separately from buffers.</summary>
    private const int MaxBoundTextures = 31;

    private const int MaxThreadsPerThreadgroup = 1024;

    private readonly SemanticModel _semanticModel;
    private readonly TypeDeclarationSyntax _declaration;
    private readonly INamedTypeSymbol _symbol;
    private readonly DiagnosticBag _diagnostics = new();
    private readonly CancellationToken _cancellationToken;
    private readonly Location _location;
    private bool _canEmit = true;
    private int _threadGroupSizeX;
    private int _threadGroupSizeY;
    private int _threadGroupSizeZ;
    private bool _isPixelShader;

    private ShaderAnalyzer(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        _semanticModel = context.SemanticModel;
        _declaration = (TypeDeclarationSyntax)context.TargetNode;
        _symbol = (INamedTypeSymbol)context.TargetSymbol;
        _cancellationToken = cancellationToken;
        _location = _declaration.Identifier.GetLocation();
    }

    public static ShaderResult Analyze(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var analyzer = new ShaderAnalyzer(context, cancellationToken);
        ShaderModel? model;

        try
        {
            model = analyzer.Run();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // Never crash the compiler: surface the failure as a diagnostic on the shader instead.
            analyzer._diagnostics.Report(DiagnosticDescriptors.InternalError, analyzer._location, analyzer._symbol.Name, e.GetType().Name + ": " + e.Message);
            model = analyzer.TryCreateStub();
        }

        return new ShaderResult(model, analyzer._diagnostics.ToEquatableArray());
    }

    private ShaderModel? Run()
    {
        bool implementsInterface = CheckDeclaration();
        ReadThreadGroupSize();

        if (!implementsInterface)
        {
            return null;
        }

        // Locals may not shadow the user structs that the MSL refers to by name, but those names are
        // only known once everything is translated. In the rare case of a clash, translate again
        // with the struct names reserved.
        var reservedLocalNames = new HashSet<string>(StringComparer.Ordinal);
        Translation translation;

        for (int attempt = 0; ; attempt++)
        {
            translation = Translate(reservedLocalNames);

            if (attempt == 1 || !translation.StructNames.Any(translation.LocalNames.Contains))
            {
                break;
            }

            reservedLocalNames.UnionWith(translation.StructNames);
        }

        _diagnostics.AddRange(translation.Diagnostics);

        if (!_canEmit)
        {
            return null;
        }

        return translation.Model is not null && !_diagnostics.HasErrors ? translation.Model : CreateModel(isStub: true);
    }

    private ShaderModel? TryCreateStub()
    {
        try
        {
            return _canEmit && ImplementsComputeShader() ? CreateModel(isStub: true) : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Declaration checks
    // ---------------------------------------------------------------------------------------------

    private bool ImplementsComputeShader() => ComputeInterface() is not null || PixelInterface() is not null;

    /// <summary>The non-generic <c>IComputeShader</c>, when implemented.</summary>
    private INamedTypeSymbol? ComputeInterface() =>
        _symbol.AllInterfaces.FirstOrDefault(static i => !i.IsGenericType && TypeResolver.IsSharpComputeType(i, "IComputeShader"));

    /// <summary>The pixel shader interface <c>IComputeShader&lt;TPixel&gt;</c>, when implemented.</summary>
    private INamedTypeSymbol? PixelInterface() =>
        _symbol.AllInterfaces.FirstOrDefault(static i => i is { IsGenericType: true, Arity: 1 } && TypeResolver.IsSharpComputeType(i, "IComputeShader"));

    private bool CheckDeclaration()
    {
        string name = _symbol.Name;

        if (_declaration is RecordDeclarationSyntax)
        {
            Unsupported("a record struct");
        }

        if (_symbol.IsRefLikeType)
        {
            Unsupported("a ref struct");
        }

        if (_symbol.IsFileLocal)
        {
            Unsupported("a file-local type");
        }

        if (_symbol.TypeParameters.Length > 0)
        {
            Unsupported("generic");
        }

        if (!_declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            _diagnostics.Report(DiagnosticDescriptors.ShaderMustBePartial, _location, name);
            _canEmit = false;
        }

        for (INamedTypeSymbol? containing = _symbol.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            var declarations = containing.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax(_cancellationToken))
                .OfType<TypeDeclarationSyntax>()
                .ToList();

            if (containing.IsGenericType)
            {
                Unsupported($"nested in the generic type '{containing.Name}'");
            }

            if (declarations.Count > 0 && !declarations.Any(static d => d.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                _diagnostics.Report(DiagnosticDescriptors.ContainingTypeMustBePartial, declarations[0].Identifier.GetLocation(), containing.Name, name);
                _canEmit = false;
            }
        }

        bool implementsInterface = ImplementsComputeShader();

        if (!implementsInterface)
        {
            _diagnostics.Report(DiagnosticDescriptors.MissingComputeShaderInterface, _location, name);
            _canEmit = false;
        }
        else if (PixelInterface() is { } pixel)
        {
            _isPixelShader = true;
            ITypeSymbol pixelType = pixel.TypeArguments[0];

            if (ComputeInterface() is not null)
            {
                _diagnostics.Report(DiagnosticDescriptors.ConflictingShaderInterfaces, _location, name, TypeResolver.Display(pixelType));
                implementsInterface = false;
            }
            else if (pixelType.TypeKind != TypeKind.Error && !IsFloat4(pixelType))
            {
                _diagnostics.Report(DiagnosticDescriptors.UnsupportedPixelType, _location, name, TypeResolver.Display(pixelType));
                implementsInterface = false;
            }
        }

        if (!_symbol.IsReadOnly)
        {
            _diagnostics.Report(DiagnosticDescriptors.ShaderShouldBeReadOnly, _location, name);
        }

        return implementsInterface;

        void Unsupported(string what)
        {
            _diagnostics.Report(DiagnosticDescriptors.UnsupportedShaderDeclaration, _location, name, what);
            _canEmit = false;
        }
    }

    /// <summary>
    /// The pixel type must be the <c>[MslType("float4")]</c> vector: the output texture is a
    /// <c>texture2d&lt;float, access::write&gt;</c>, whose <c>write</c> takes a <c>float4</c>.
    /// </summary>
    private bool IsFloat4(ITypeSymbol type) =>
        new TypeResolver(_semanticModel.Compilation, new NameAllocator()).Resolve(type) is { Kind: GpuTypeKind.Vector, MslName: "float4" };

    private void ReadThreadGroupSize()
    {
        if (TypeResolver.FindAttribute(_symbol, "ThreadGroupSizeAttribute") is not { } attribute)
        {
            // No attribute: report 0,0,0 and let the runtime pick a size per dispatch rank.
            return;
        }

        var arguments = attribute.ConstructorArguments;
        int x = arguments.Length > 0 && arguments[0].Value is int xValue ? xValue : 0;
        int y = arguments.Length > 1 && arguments[1].Value is int yValue ? yValue : 1;
        int z = arguments.Length > 2 && arguments[2].Value is int zValue ? zValue : 1;
        Location location = attribute.ApplicationSyntaxReference?.GetSyntax(_cancellationToken).GetLocation() ?? _location;
        string? problem = null;

        if (x <= 0 || y <= 0 || z <= 0)
        {
            problem = "every dimension must be at least 1";
        }
        else if ((long)x * y * z > MaxThreadsPerThreadgroup)
        {
            problem = $"the total of {((long)x * y * z).ToString(CultureInfo.InvariantCulture)} threads exceeds Metal's limit of {MaxThreadsPerThreadgroup.ToString(CultureInfo.InvariantCulture)} threads per threadgroup";
        }

        if (problem is not null)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.InvalidThreadGroupSize,
                location,
                x.ToString(CultureInfo.InvariantCulture),
                y.ToString(CultureInfo.InvariantCulture),
                z.ToString(CultureInfo.InvariantCulture),
                problem);
            return;
        }

        _threadGroupSizeX = x;
        _threadGroupSizeY = y;
        _threadGroupSizeZ = z;
    }

    // ---------------------------------------------------------------------------------------------
    // Translation
    // ---------------------------------------------------------------------------------------------

    private sealed class Translation
    {
        public DiagnosticBag Diagnostics { get; } = new();

        public HashSet<string> StructNames { get; } = new(StringComparer.Ordinal);

        public HashSet<string> LocalNames { get; set; } = new(StringComparer.Ordinal);

        public ShaderModel? Model { get; set; }
    }

    /// <summary>A member of the <c>__Constants</c> struct, on both the MSL and the C# side.</summary>
    private sealed class ConstantMember
    {
        public ConstantMember(string name, GpuType type, string value)
        {
            Name = name;
            Type = type;
            Value = value;
        }

        public string Name { get; }

        public GpuType Type { get; }

        /// <summary>The C# expression that produces the value in <c>SetArguments</c>.</summary>
        public string Value { get; }

        public int Offset { get; set; }
    }

    private Translation Translate(HashSet<string> reservedLocalNames)
    {
        var translation = new Translation();
        DiagnosticBag diagnostics = translation.Diagnostics;
        var globalNames = new NameAllocator();
        string entryPoint = globalNames.Allocate(EntryPointName());
        var types = new TypeResolver(_semanticModel.Compilation, globalNames);
        CaptureSet captures = CollectCaptures(types, diagnostics);

        if (FindExecute() is not { } execute)
        {
            diagnostics.Report(DiagnosticDescriptors.MissingExecuteMethod, _location, _symbol.Name);
            return translation;
        }

        var translator = new MslTranslator(_semanticModel, _symbol, types, captures, diagnostics, globalNames, reservedLocalNames, _isPixelShader, _cancellationToken);
        translator.CollectGroupShared();
        string? body = translator.TranslateKernel(execute, out Builtins builtins, out IReadOnlyList<GroupSharedField> groupShared);

        translation.LocalNames = translator.LocalNames;
        translation.StructNames.UnionWith(types.Structs.Select(static s => s.MslName));

        if (body is null)
        {
            diagnostics.Report(DiagnosticDescriptors.MissingExecuteMethod, _location, _symbol.Name);
            return translation;
        }

        // The binding ABI: buffers at 0..n-1 in declaration order, then one constants block at n.
        bool usesInstanceAccess = captures.All.Any(static c => c.IsParameter);
        var buffers = captures.Buffers.ToList();
        var textures = captures.Textures.ToList();
        var constants = new List<ConstantMember>();

        foreach (Capture capture in captures.Constants)
        {
            constants.Add(new ConstantMember(capture.MslName, capture.Type, Access(capture, usesInstanceAccess)));
        }

        GpuType? lengthType = buffers.Any(static b => b.LengthUsed)
            ? types.Resolve(_semanticModel.Compilation.GetSpecialType(SpecialType.System_UInt32))
            : null;

        foreach (Capture buffer in buffers)
        {
            if (buffer.LengthUsed && lengthType is not null)
            {
                constants.Add(new ConstantMember(buffer.LengthName, lengthType, "(uint)" + Access(buffer, usesInstanceAccess) + ".Length"));
            }
        }

        int boundArguments = buffers.Count + (constants.Count > 0 ? 1 : 0);

        if (boundArguments > MaxBoundArguments)
        {
            diagnostics.Report(
                DiagnosticDescriptors.TooManyBoundArguments,
                _location,
                _symbol.Name,
                boundArguments.ToString(CultureInfo.InvariantCulture),
                MaxBoundArguments.ToString(CultureInfo.InvariantCulture));
        }

        // Textures have an argument table of their own. A pixel shader's output takes index 0 of it.
        int firstTextureIndex = _isPixelShader ? 1 : 0;

        if (firstTextureIndex + textures.Count > MaxBoundTextures)
        {
            diagnostics.Report(
                DiagnosticDescriptors.TooManyTextures,
                _location,
                _symbol.Name,
                (firstTextureIndex + textures.Count).ToString(CultureInfo.InvariantCulture),
                MaxBoundTextures.ToString(CultureInfo.InvariantCulture),
                _isPixelShader ? " (including the output texture of a pixel shader)" : string.Empty);
        }

        // Threadgroup memory is one fixed allocation per threadgroup; the arrays the kernel declares
        // (the ones it uses) must fit in it together.
        long threadgroupMemory = groupShared.Sum(static g => g.ByteLength);

        if (threadgroupMemory > MslTranslator.MaxThreadgroupMemoryLength)
        {
            diagnostics.Report(
                DiagnosticDescriptors.ThreadgroupMemoryTooLarge,
                _location,
                _symbol.Name,
                threadgroupMemory.ToString(CultureInfo.InvariantCulture),
                string.Join(" + ", groupShared.Select(static g => $"{g.Symbol.Name}: {g.ByteLength.ToString(CultureInfo.InvariantCulture)}")),
                MslTranslator.MaxThreadgroupMemoryLength.ToString(CultureInfo.InvariantCulture));
        }

        if (diagnostics.HasErrors || translator.SawInvalidCode || types.SawErrorType)
        {
            // Either we reported why, or the user's code does not compile and the C# compiler will.
            return translation;
        }

        string? prelude = null;

        // The prelude holds the __ms_* helper functions and the __ms_* boundary types (a matrix
        // with a Packed form): either makes the shader depend on it.
        bool usesPrelude = translator.UsesPrelude ||
            buffers.Any(static b => b.Type.Element!.NeedsExplicitPacking) ||
            constants.Any(static c => c.Type.NeedsExplicitPacking) ||
            types.Structs.Any(static s => s.Fields.Any(static f => f.Type.NeedsExplicitPacking));

        if (usesPrelude && (prelude = ReadPrelude()) is null)
        {
            diagnostics.Report(
                DiagnosticDescriptors.UnsupportedMethodCall,
                _location,
                "__ms_*",
                "the shader uses an MSL prelude helper, but the referenced SharpCompute assembly has no 'SharpCompute.MslPrelude.Source' constant");
            return translation;
        }

        int constantsSize = LayOutConstants(constants);

        // The grid window takes the first free buffer index. A shader that uses all of them still
        // works for whole-grid dispatches; it just cannot be split (GraphicsDevice.ForSplit rejects it).
        int gridWindowIndex = boundArguments < MaxBoundArguments ? boundArguments : -1;
        var program = new MslProgram(entryPoint, _isPixelShader, prelude, translator.Samplers, translator.Globals, types.Structs, buffers, textures, firstTextureIndex, constants, constantsSize, translator.Helpers, groupShared, body, builtins, gridWindowIndex);
        string msl = MslSourceBuilder.Build(program);
        string shaderName = TypeName();

        translation.Model = CreateModel(
            isStub: false,
            entryPoint,
            msl,
            new EquatableArray<BufferArgument>(buffers.Select((b, i) => new BufferArgument(i, Access(b, usesInstanceAccess), shaderName + "." + b.Symbol.Name))),
            new EquatableArray<BufferArgument>(textures.Select((t, i) => new BufferArgument(firstTextureIndex + i, Access(t, usesInstanceAccess), shaderName + "." + t.Symbol.Name))),
            new EquatableArray<ConstantArgument>(constants.Select(static c => new ConstantArgument(c.Type.CSharpName, CSharpIdentifier(c.Name), c.Value))),
            constantsIndex: buffers.Count,
            usesInstanceAccess,
            gridWindowIndex,
            translator.UsesAtomics,
            usesThreadgroupMemory: groupShared.Count > 0 || translator.UsesBarriers,
            threadgroupMemoryLength: (int)threadgroupMemory);

        return translation;
    }

    /// <summary>
    /// Assigns offsets with the rules of <c>[StructLayout(Sequential, Pack = 4)]</c>. Every supported
    /// type has an alignment of at most 4, so this is also the natural layout of the MSL struct.
    /// </summary>
    private static int LayOutConstants(List<ConstantMember> constants)
    {
        const int Pack = 4;
        int offset = 0;
        int alignment = 1;

        foreach (ConstantMember member in constants)
        {
            int memberAlignment = Math.Min(member.Type.Alignment, Pack);

            offset = TypeResolver.AlignUp(offset, memberAlignment);
            member.Offset = offset;
            offset += member.Type.Size;
            alignment = Math.Max(alignment, memberAlignment);
        }

        return TypeResolver.AlignUp(offset, alignment);
    }

    /// <summary>Reads <c>SharpCompute.MslPrelude.Source</c> from the referenced SharpCompute assembly.</summary>
    private string? ReadPrelude() =>
        _semanticModel.Compilation.GetTypeByMetadataName("SharpCompute.MslPrelude")?
            .GetMembers("Source")
            .OfType<IFieldSymbol>()
            .FirstOrDefault(static f => f.IsConst)?
            .ConstantValue as string;

    /// <summary>The shader's name including containing types but not the namespace, e.g. <c>Outer.Copy</c>.</summary>
    private string TypeName()
    {
        string name = _symbol.Name;

        for (INamedTypeSymbol? type = _symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            name = type.Name + "." + name;
        }

        return name;
    }

    private static string Access(Capture capture, bool usesInstanceAccess)
    {
        string name = CSharpIdentifier(capture.Symbol.Name);

        // Primary-constructor parameters can only be named from instance members.
        return usesInstanceAccess ? (capture.IsParameter ? name : "this." + name) : "shader." + name;
    }

    private static string CSharpIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;

    private IMethodSymbol? FindExecute()
    {
        INamedTypeSymbol? shaderInterface = _isPixelShader ? PixelInterface() : ComputeInterface();
        IMethodSymbol? member = shaderInterface?.GetMembers("Execute").OfType<IMethodSymbol>().FirstOrDefault();

        if (member is null || _symbol.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation)
        {
            return null;
        }

        return SymbolEqualityComparer.Default.Equals(implementation.ContainingType, _symbol) ? implementation : null;
    }

    // ---------------------------------------------------------------------------------------------
    // Captured state
    // ---------------------------------------------------------------------------------------------

    private CaptureSet CollectCaptures(TypeResolver types, DiagnosticBag diagnostics)
    {
        var captures = new CaptureSet();
        NameAllocator bufferNames = captures.ResourceNames;
        var constantNames = new NameAllocator();

        var fields = _symbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(static f => !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared)
            .ToList();

        // Primary-constructor parameters come first: they are declared before any field.
        IMethodSymbol? primaryConstructor = _symbol.InstanceConstructors.FirstOrDefault(
            c => c.DeclaringSyntaxReferences.Any(r => r.GetSyntax(_cancellationToken) is TypeDeclarationSyntax));

        if (primaryConstructor is not null && primaryConstructor.Parameters.Length > 0)
        {
            ParameterUses uses = FindParameterUses(primaryConstructor);

            foreach (IParameterSymbol parameter in primaryConstructor.Parameters)
            {
                var syntax = parameter.DeclaringSyntaxReferences.Select(r => r.GetSyntax(_cancellationToken)).OfType<ParameterSyntax>().FirstOrDefault();

                if (uses.Initializers.TryGetValue(parameter, out (Location Location, string Member) use))
                {
                    if (uses.Members.Contains(parameter))
                    {
                        // Read by a member, so the compiler captures it, and copied into a member: stored twice.
                        diagnostics.Report(DiagnosticDescriptors.PrimaryConstructorParameterStoredInField, use.Location, parameter.Name, use.Member);
                        captures.AddFailed(parameter);
                    }

                    // Only used to initialize a member: the member is the captured state, the parameter is not.
                    continue;
                }

                if (fields.FirstOrDefault(f => f.Name == parameter.Name) is { } sameName)
                {
                    // The field hides the parameter, so the generated code could not name it.
                    diagnostics.Report(
                        DiagnosticDescriptors.PrimaryConstructorParameterStoredInField,
                        syntax?.Identifier.GetLocation() ?? _location,
                        parameter.Name,
                        $"the field '{sameName.Name}'");
                    captures.AddFailed(parameter);
                    continue;
                }

                AddCapture(parameter, parameter.Type, "Primary constructor parameter", syntax?.Type?.GetLocation() ?? syntax?.GetLocation() ?? _location);
            }
        }

        foreach (IFieldSymbol field in fields)
        {
            if (MslTranslator.IsGroupShared(field))
            {
                // Not captured state but a misdeclared [GroupShared] array (it must be static), which
                // the translator reports as such; a second diagnostic about its type would only distract.
                captures.AddFailed(field);
                continue;
            }

            SyntaxNode? syntax = field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(_cancellationToken);
            Location location = syntax is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration }
                ? declaration.Type.GetLocation()
                : syntax?.GetLocation() ?? _location;

            AddCapture(field, field.Type, "Field", location);
        }

        return captures;

        void AddCapture(ISymbol symbol, ITypeSymbol typeSymbol, string kind, Location location)
        {
            GpuType? type = types.Resolve(typeSymbol, out TypeFailure? failure);

            if (type is { IsResource: false, CanCrossBoundary: false })
            {
                // Fine as a local (bool, bool vectors, types without a packed form), but not as state.
                type = null;
                failure = new TypeFailure(DiagnosticDescriptors.UnsupportedCaptureType, types.BoundaryReason(typeSymbol));
            }

            if (type is null)
            {
                if (TypeResolver.IsStructFailure(failure) || ReferenceEquals(failure?.Descriptor, DiagnosticDescriptors.UnsupportedTextureElementType))
                {
                    // A problem with a user struct or a texture's pixel type: report it as such, at the capture that uses it.
                    diagnostics.Report(failure!.Descriptor, location, failure.Arguments);
                }
                else if (failure is not null)
                {
                    diagnostics.Report(DiagnosticDescriptors.UnsupportedCaptureType, location, kind, symbol.Name, TypeResolver.Display(typeSymbol), failure.Reason);
                }

                captures.AddFailed(symbol);
                return;
            }

            // Buffers and textures are kernel parameters and share one scope; constants are members of __Constants.
            NameAllocator names = type.IsResource ? bufferNames : constantNames;
            captures.Add(new Capture(symbol, type, names.Allocate(MslNames.Escape(symbol.Name))));
        }
    }

    /// <summary>Where primary-constructor parameters are read.</summary>
    private sealed class ParameterUses
    {
        /// <summary>Parameters read by a field or property initializer, with the first such use.</summary>
        public Dictionary<IParameterSymbol, (Location Location, string Member)> Initializers { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>Parameters read anywhere else in the struct (methods, accessors…), which makes the compiler capture them.</summary>
        public HashSet<IParameterSymbol> Members { get; } = new(SymbolEqualityComparer.Default);
    }

    /// <summary>
    /// Finds where primary-constructor parameters are read. A parameter that initializes a member and
    /// is also read by a member would be stored twice (in the member and as captured state), which the
    /// contract forbids; one that only initializes a member is not captured at all.
    /// </summary>
    private ParameterUses FindParameterUses(IMethodSymbol primaryConstructor)
    {
        var result = new ParameterUses();
        var names = new HashSet<string>(primaryConstructor.Parameters.Select(static p => p.Name), StringComparer.Ordinal);

        foreach (SyntaxReference reference in _symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(_cancellationToken) is not TypeDeclarationSyntax declaration)
            {
                continue;
            }

            SemanticModel model = declaration.SyntaxTree == _semanticModel.SyntaxTree
                ? _semanticModel
                : _semanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);

            foreach (MemberDeclarationSyntax member in declaration.Members)
            {
                EqualsValueClauseSyntax? initializerOfProperty = null;

                switch (member)
                {
                    case FieldDeclarationSyntax field:
                        foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                        {
                            ScanInitializer(variable.Initializer, $"the field '{variable.Identifier.ValueText}'");
                        }

                        continue;

                    case PropertyDeclarationSyntax property:
                        initializerOfProperty = property.Initializer;
                        ScanInitializer(property.Initializer, $"the property '{property.Identifier.ValueText}'");
                        break;
                }

                foreach (IdentifierNameSyntax identifier in member.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if ((initializerOfProperty is null || !initializerOfProperty.Span.Contains(identifier.Span)) &&
                        Resolve(identifier) is { } parameter)
                    {
                        result.Members.Add(parameter);
                    }
                }
            }

            void ScanInitializer(EqualsValueClauseSyntax? initializer, string memberDescription)
            {
                if (initializer is null)
                {
                    return;
                }

                foreach (IdentifierNameSyntax identifier in initializer.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (Resolve(identifier) is { } parameter && !result.Initializers.ContainsKey(parameter))
                    {
                        result.Initializers.Add(parameter, (identifier.GetLocation(), memberDescription));
                    }
                }
            }

            IParameterSymbol? Resolve(IdentifierNameSyntax identifier) =>
                names.Contains(identifier.Identifier.ValueText) &&
                model.GetSymbolInfo(identifier, _cancellationToken).Symbol is IParameterSymbol parameter &&
                SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, primaryConstructor)
                    ? parameter
                    : null;
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Model
    // ---------------------------------------------------------------------------------------------

    private List<string> NameParts()
    {
        var parts = new List<string>();

        for (INamedTypeSymbol? type = _symbol; type is not null; type = type.ContainingType)
        {
            parts.Insert(0, type.Name);
        }

        for (INamespaceSymbol? ns = _symbol.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
        {
            parts.Insert(0, ns.Name);
        }

        return parts;
    }

    /// <summary>
    /// The kernel name: the fully qualified type name with <c>.</c> replaced by <c>_</c>. That is
    /// unique as long as no part contains an underscore or a non-ASCII character; otherwise a hash
    /// of the exact name is appended so that <c>A.B_C</c> and <c>A_B.C</c> stay distinct.
    /// </summary>
    private string EntryPointName()
    {
        List<string> parts = NameParts();
        string fullName = string.Join(".", parts);
        string name = string.Join("_", parts.Select(MslNames.Sanitize));

        if (!fullName.All(static c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.'))
        {
            uint hash = 2166136261;

            foreach (char c in fullName)
            {
                hash = unchecked((hash ^ c) * 16777619);
            }

            name += "_" + hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        return MslNames.Escape(name);
    }

    private ShaderModel CreateModel(
        bool isStub,
        string? entryPoint = null,
        string? mslSource = null,
        EquatableArray<BufferArgument> buffers = default,
        EquatableArray<BufferArgument> textures = default,
        EquatableArray<ConstantArgument> constants = default,
        int constantsIndex = 0,
        bool usesInstanceAccess = false,
        int gridWindowIndex = -1,
        bool usesAtomics = false,
        bool usesThreadgroupMemory = false,
        int threadgroupMemoryLength = 0)
    {
        var types = new List<TypeDeclarationInfo>();

        for (INamedTypeSymbol? type = _symbol; type is not null; type = type.ContainingType)
        {
            string keyword = type.TypeKind switch
            {
                TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
                TypeKind.Interface => "interface",
                _ => type.IsRecord ? "record" : "class",
            };

            types.Insert(0, new TypeDeclarationInfo(keyword, CSharpIdentifier(type.Name)));
        }

        string? containingNamespace = _symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted))
            : null;

        return new ShaderModel(
            HintName: string.Join(".", NameParts()) + ".SharpCompute.g.cs",
            Namespace: containingNamespace,
            Types: new EquatableArray<TypeDeclarationInfo>(types.ToArray()),
            FullyQualifiedName: _symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsStub: isStub,
            EntryPoint: entryPoint ?? string.Empty,
            MslSource: mslSource ?? string.Empty,
            ThreadGroupSizeX: _threadGroupSizeX,
            ThreadGroupSizeY: _threadGroupSizeY,
            ThreadGroupSizeZ: _threadGroupSizeZ,
            Buffers: buffers,
            Textures: textures,
            Constants: constants,
            ConstantsIndex: constantsIndex,
            UsesInstanceAccess: usesInstanceAccess,
            GridWindowIndex: gridWindowIndex,
            UsesAtomics: usesAtomics,
            UsesThreadgroupMemory: usesThreadgroupMemory,
            ThreadgroupMemoryLength: threadgroupMemoryLength);
    }

    // ---------------------------------------------------------------------------------------------
    // MSL assembly
    // ---------------------------------------------------------------------------------------------

    /// <summary>Everything that goes into one MSL translation unit.</summary>
    private sealed class MslProgram
    {
        public MslProgram(
            string entryPoint,
            bool isPixelShader,
            string? prelude,
            IReadOnlyList<string> samplers,
            IReadOnlyList<GlobalConstant> globals,
            IReadOnlyList<GpuType> structs,
            List<Capture> buffers,
            List<Capture> textures,
            int firstTextureIndex,
            List<ConstantMember> constants,
            int constantsSize,
            IReadOnlyList<HelperFunction> helpers,
            IReadOnlyList<GroupSharedField> groupShared,
            string body,
            Builtins builtins,
            int gridWindowIndex)
        {
            EntryPoint = entryPoint;
            IsPixelShader = isPixelShader;
            Prelude = prelude;
            Samplers = samplers;
            Globals = globals;
            Structs = structs;
            Buffers = buffers;
            Textures = textures;
            FirstTextureIndex = firstTextureIndex;
            Constants = constants;
            ConstantsSize = constantsSize;
            Helpers = helpers;
            GroupShared = groupShared;
            Body = body;
            Builtins = builtins;
            GridWindowIndex = gridWindowIndex;
        }

        public string EntryPoint { get; }

        public bool IsPixelShader { get; }

        /// <summary><c>MslPrelude.Source</c>, when a <c>__ms_*</c> helper is used.</summary>
        public string? Prelude { get; }

        /// <summary>The <c>constexpr sampler</c> declarations of the samplers the shader uses.</summary>
        public IReadOnlyList<string> Samplers { get; }

        public IReadOnlyList<GlobalConstant> Globals { get; }

        public IReadOnlyList<GpuType> Structs { get; }

        public List<Capture> Buffers { get; }

        public List<Capture> Textures { get; }

        /// <summary>The texture index of the first captured texture: 1 in a pixel shader, whose output is texture 0.</summary>
        public int FirstTextureIndex { get; }

        public List<ConstantMember> Constants { get; }

        public int ConstantsSize { get; }

        public IReadOnlyList<HelperFunction> Helpers { get; }

        /// <summary>The <c>[GroupShared]</c> arrays the kernel declares as threadgroup memory, in declaration order.</summary>
        public IReadOnlyList<GroupSharedField> GroupShared { get; }

        public string Body { get; }

        public Builtins Builtins { get; }

        /// <summary>The buffer index of the hidden grid window, or -1 when there is no free index.</summary>
        public int GridWindowIndex { get; }
    }

    private static class MslSourceBuilder
    {
        public static string Build(MslProgram program)
        {
            var writer = new CodeWriter();
            List<Capture> buffers = program.Buffers;
            List<ConstantMember> constants = program.Constants;

            writer.Line("#include <metal_stdlib>");
            writer.Line();
            writer.Line("using namespace metal;");

            if (program.Prelude is { } prelude)
            {
                writer.Line();
                writer.Lines(prelude.Replace("\r\n", "\n").TrimEnd('\n'));
            }

            // Before anything the user declared, so that no user name can hide `sampler`, `coord`,
            // `filter` or `address` here.
            if (program.Samplers.Count > 0)
            {
                writer.Line();

                foreach (string sampler in program.Samplers)
                {
                    writer.Line(sampler);
                }
            }

            if (program.Globals.Count > 0)
            {
                writer.Line();

                foreach (GlobalConstant global in program.Globals)
                {
                    writer.Line($"constant {global.Type} {global.Name} = {global.Initializer};");
                }
            }

            // Drift guards: sizes and offsets below are the ones of the C# types. If MSL lays a type
            // out differently, the pipeline fails to compile instead of silently corrupting data.
            var assertedVectors = new HashSet<string>(StringComparer.Ordinal);

            foreach (GpuType element in buffers.Select(static b => b.Type.Element!).Concat(program.GroupShared.Select(static g => g.Element)))
            {
                if (element.Kind == GpuTypeKind.Vector && assertedVectors.Add(element.BoundaryName))
                {
                    if (assertedVectors.Count == 1)
                    {
                        writer.Line();
                    }

                    writer.Line(SizeAssert(element.BoundaryName, element.Size));
                }
            }

            foreach (GpuType type in program.Structs)
            {
                writer.Line();
                writer.Line("struct " + type.MslName);
                writer.Line("{");
                writer.Indent++;

                foreach (GpuField field in type.Fields)
                {
                    writer.Line(field.Type.BoundaryName + " " + field.MslName + ";");
                }

                writer.Indent--;
                writer.Line("};");
                writer.Line(SizeAssert(type.MslName, type.Size));

                foreach (GpuField field in type.Fields)
                {
                    writer.Line(OffsetAssert(type.MslName, field.MslName, field.Offset));
                }
            }

            if (constants.Count > 0)
            {
                writer.Line();
                writer.Line("struct " + MslNames.ConstantsStruct);
                writer.Line("{");
                writer.Indent++;

                foreach (ConstantMember member in constants)
                {
                    writer.Line(member.Type.BoundaryName + " " + member.Name + ";");
                }

                writer.Indent--;
                writer.Line("};");
                writer.Line(SizeAssert(MslNames.ConstantsStruct, program.ConstantsSize));

                foreach (ConstantMember member in constants)
                {
                    writer.Line(OffsetAssert(MslNames.ConstantsStruct, member.Name, member.Offset));
                }
            }

            // Pixel shaders always receive the thread position (the pixel) and the grid (the texture size).
            Builtins builtins = program.IsPixelShader
                ? program.Builtins | Builtins.ThreadPositionInGrid | Builtins.ThreadsPerGrid
                : program.Builtins;

            // A kernel that reads grid positions or the grid size can run on a window of the grid (one
            // part of a CPU+GPU split): the runtime binds the window and selects the specialization
            // with function constant 0. Normal dispatches use the other specialization, where the
            // window argument does not exist and the prologue folds away, so they pay nothing for it.
            bool windowed = program.GridWindowIndex >= 0 && (builtins & BuiltinInfo.Windowed) != 0;

            if (windowed)
            {
                writer.Line();
                writer.Line("struct " + MslNames.GridWindowStruct);
                writer.Line("{");
                writer.Indent++;
                writer.Line("packed_uint3 offset;");
                writer.Line("packed_uint3 size;");
                writer.Line("packed_uint3 groupOffset;");
                writer.Indent--;
                writer.Line("};");
                writer.Line(SizeAssert(MslNames.GridWindowStruct, 36));
                writer.Line(OffsetAssert(MslNames.GridWindowStruct, "offset", 0));
                writer.Line(OffsetAssert(MslNames.GridWindowStruct, "size", 12));
                writer.Line(OffsetAssert(MslNames.GridWindowStruct, "groupOffset", 24));
                writer.Line();
                writer.Line($"constant bool {MslNames.Windowed} [[function_constant(0)]];");
            }

            if (program.Helpers.Count > 0)
            {
                writer.Line();

                foreach (HelperFunction helper in program.Helpers)
                {
                    writer.Line(helper.Signature + ";");
                }

                foreach (HelperFunction helper in program.Helpers)
                {
                    writer.Line();
                    writer.Line(helper.Signature);
                    WriteBody(writer, helper.Body);
                }
            }

            var parameters = new List<string>();

            if (program.IsPixelShader)
            {
                // Texture index 0 is reserved for the output; the runtime binds it before SetArguments.
                parameters.Add($"texture2d<float, access::write> {MslNames.Output} [[texture(0)]]");
            }

            for (int i = 0; i < program.Textures.Count; i++)
            {
                Capture texture = program.Textures[i];
                parameters.Add($"{texture.Type.ParameterTypeName} {texture.MslName} [[texture({(program.FirstTextureIndex + i).ToString(CultureInfo.InvariantCulture)})]]");
            }

            for (int i = 0; i < buffers.Count; i++)
            {
                parameters.Add($"{buffers[i].Type.ParameterTypeName} {buffers[i].MslName} [[buffer({i.ToString(CultureInfo.InvariantCulture)})]]");
            }

            if (constants.Count > 0)
            {
                parameters.Add($"constant {MslNames.ConstantsStruct}& {MslNames.Constants} [[buffer({buffers.Count.ToString(CultureInfo.InvariantCulture)})]]");
            }

            if (windowed)
            {
                parameters.Add($"constant {MslNames.GridWindowStruct}& {MslNames.GridWindow} [[buffer({program.GridWindowIndex.ToString(CultureInfo.InvariantCulture)}), function_constant({MslNames.Windowed})]]");
            }

            var prologue = new List<string>();

            foreach (Builtins builtin in BuiltinInfo.All)
            {
                if ((builtins & builtin) == 0)
                {
                    continue;
                }

                if (!windowed || (builtin & BuiltinInfo.Windowed) == 0)
                {
                    parameters.Add($"{BuiltinInfo.Type(builtin)} {BuiltinInfo.Name(builtin)} [[{BuiltinInfo.Attribute(builtin)}]]");
                    continue;
                }

                string raw = BuiltinInfo.RawName(builtin);
                string windowValue = BuiltinInfo.WindowOffset(builtin) is { } offset
                    ? $"{raw} + uint3({MslNames.GridWindow}.{offset})"
                    : $"uint3({MslNames.GridWindow}.size)";

                parameters.Add($"uint3 {raw} [[{BuiltinInfo.Attribute(builtin)}]]");
                prologue.Add($"    const uint3 {BuiltinInfo.Name(builtin)} = {MslNames.Windowed} ? {windowValue} : {raw};");
            }

            writer.Line();

            if (parameters.Count == 0)
            {
                writer.Line("kernel void " + program.EntryPoint + "()");
            }
            else
            {
                writer.Line("kernel void " + program.EntryPoint + "(");
                writer.Indent++;

                for (int i = 0; i < parameters.Count; i++)
                {
                    writer.Line(parameters[i] + (i == parameters.Count - 1 ? ")" : ","));
                }

                writer.Indent--;
            }

            // Threadgroup memory must be declared in the kernel function itself; helpers receive pointers
            // to these arrays. Like the prologue, the declarations precede everything the user wrote.
            foreach (GroupSharedField field in program.GroupShared)
            {
                prologue.Add("    " + field.Declaration);
            }

            WriteBody(writer, prologue.Count == 0 ? program.Body : string.Join("\n", prologue) + "\n" + program.Body);
            return writer.ToString();
        }

        /// <summary>Writes an already indented function body between braces.</summary>
        private static void WriteBody(CodeWriter writer, string body)
        {
            writer.Line("{");

            foreach (string line in body.Split('\n'))
            {
                if (line.Length > 0)
                {
                    writer.Line(line);
                }
            }

            writer.Line("}");
        }

        private static string SizeAssert(string type, int size) =>
            $"static_assert(sizeof({type}) == {size.ToString(CultureInfo.InvariantCulture)}, \"SharpCompute: size of '{type}' does not match its C# layout\");";

        private static string OffsetAssert(string type, string member, int offset) =>
            $"static_assert(__builtin_offsetof({type}, {member}) == {offset.ToString(CultureInfo.InvariantCulture)}, \"SharpCompute: offset of '{type}.{member}' does not match its C# layout\");";
    }
}
