using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SharpCompute.Build;

/// <summary>
/// Reads the shaders of an assembly from its metadata, without loading the assembly or running any
/// of its code (the assembly may target another runtime, and its dependencies need not be present).
/// </summary>
/// <remarks>
/// A shader is a type that carries <c>SharpCompute.GeneratedShaderRecordAttribute</c>, which holds the
/// entry point and the compile options. Its MSL is not in the attribute: it is the string literal
/// that the type's implementation of <c>IComputeShaderDescriptor&lt;T&gt;.MslSource</c> returns, which
/// the generator emits as <c>=&gt; """..."""</c> and the C# compiler turns into <c>ldstr; ret</c>.
/// A type whose getter is anything else is skipped, and its shader is compiled at run time. Reading
/// the wrong string cannot make a shader run the wrong code either: the kernel is stored under a
/// name that hashes the text it was compiled from, and the runtime asks for the name of the text it has.
/// </remarks>
internal static class ShaderRecordReader
{
    private const string Namespace = "SharpCompute";
    private const string RecordAttribute = "GeneratedShaderRecordAttribute";
    private const string DescriptorInterface = "IComputeShaderDescriptor`1";
    private const string MslSourceGetter = "get_MslSource";

    /// <summary>
    /// The shaders recorded in the assembly at <paramref name="assemblyPath"/>, in metadata order.
    /// <paramref name="skipped"/> receives a line for each record whose MSL could not be read.
    /// </summary>
    /// <exception cref="BadImageFormatException">The file is not a managed assembly.</exception>
    public static IReadOnlyList<ShaderRecord> Read(string assemblyPath, Action<string>? skipped = null)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader pe = new(stream);

        if (!pe.HasMetadata)
        {
            throw new BadImageFormatException($"'{assemblyPath}' is not a managed assembly.");
        }

        MetadataReader metadata = pe.GetMetadataReader();
        List<ShaderRecord> records = [];

        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);

            if (!TryReadRecord(metadata, type, out string entryPoint, out int compileOptions))
            {
                continue;
            }

            string typeName = FullName(metadata, type);

            if (ReadMslSource(metadata, pe, type) is { } mslSource)
            {
                records.Add(new ShaderRecord(typeName, entryPoint, compileOptions, mslSource));
            }
            else
            {
                skipped?.Invoke($"'{typeName}' has a shader record, but its MslSource is not a string literal; it will be compiled at run time.");
            }
        }

        return records;
    }

    /// <summary>Finds the record attribute on <paramref name="type"/> and decodes its two constructor arguments.</summary>
    private static bool TryReadRecord(MetadataReader metadata, TypeDefinition type, out string entryPoint, out int compileOptions)
    {
        entryPoint = string.Empty;
        compileOptions = 0;

        foreach (CustomAttributeHandle handle in type.GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);

            if (!IsType(metadata, AttributeType(metadata, attribute), RecordAttribute))
            {
                continue;
            }

            // ECMA-335 II.23.3: a prolog (0x0001), then the fixed arguments in constructor order. The
            // record's are a string and a ShaderCompileOptions, an enum over int, which is stored as the int.
            BlobReader blob = metadata.GetBlobReader(attribute.Value);

            if (blob.RemainingBytes < 2 || blob.ReadUInt16() != 1 || blob.ReadSerializedString() is not { Length: > 0 } name || blob.RemainingBytes < 4)
            {
                return false;
            }

            entryPoint = name;
            compileOptions = blob.ReadInt32();
            return true;
        }

        return false;
    }

    /// <summary>The type that declares an attribute's constructor.</summary>
    private static EntityHandle AttributeType(MetadataReader metadata, CustomAttribute attribute) =>
        attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default,
        };

    /// <summary>True when <paramref name="type"/> is the SharpCompute type <paramref name="name"/>, referenced or defined here.</summary>
    private static bool IsType(MetadataReader metadata, EntityHandle type, string name)
    {
        switch (type.Kind)
        {
            case HandleKind.TypeReference:
                TypeReference reference = metadata.GetTypeReference((TypeReferenceHandle)type);
                return metadata.StringComparer.Equals(reference.Name, name) && metadata.StringComparer.Equals(reference.Namespace, Namespace);

            case HandleKind.TypeDefinition:
                TypeDefinition definition = metadata.GetTypeDefinition((TypeDefinitionHandle)type);
                return metadata.StringComparer.Equals(definition.Name, name) && metadata.StringComparer.Equals(definition.Namespace, Namespace);

            default:
                return false;
        }
    }

    /// <summary>
    /// The string that the type's <c>IComputeShaderDescriptor&lt;T&gt;.MslSource</c> getter returns, or
    /// null when the type has no such getter or its body is not exactly <c>ldstr; ret</c>.
    /// </summary>
    private static string? ReadMslSource(MetadataReader metadata, PEReader pe, TypeDefinition type)
    {
        // The getter is an explicit interface implementation, so the type's MethodImpl rows say which
        // method it is, whatever name the compiler gave it.
        foreach (MethodImplementationHandle handle in type.GetMethodImplementations())
        {
            MethodImplementation implementation = metadata.GetMethodImplementation(handle);

            if (implementation.MethodDeclaration.Kind != HandleKind.MemberReference || implementation.MethodBody.Kind != HandleKind.MethodDefinition)
            {
                continue;
            }

            MemberReference declaration = metadata.GetMemberReference((MemberReferenceHandle)implementation.MethodDeclaration);

            if (!metadata.StringComparer.Equals(declaration.Name, MslSourceGetter) || !IsDescriptorInterface(metadata, declaration.Parent))
            {
                continue;
            }

            int address = metadata.GetMethodDefinition((MethodDefinitionHandle)implementation.MethodBody).RelativeVirtualAddress;
            return address == 0 ? null : ReadReturnedLiteral(metadata, pe.GetMethodBody(address));
        }

        return null;
    }

    /// <summary>True when <paramref name="parent"/> is an instantiation of <c>SharpCompute.IComputeShaderDescriptor&lt;T&gt;</c>.</summary>
    private static bool IsDescriptorInterface(MetadataReader metadata, EntityHandle parent)
    {
        if (parent.Kind != HandleKind.TypeSpecification)
        {
            return false;
        }

        // II.23.2.14: GENERICINST (CLASS | VALUETYPE) TypeDefOrRefOrSpecEncoded GenArgCount Type*.
        BlobReader signature = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);

        return signature.RemainingBytes >= 3
            && signature.ReadSignatureTypeCode() == SignatureTypeCode.GenericTypeInstance
            && signature.ReadSignatureTypeCode() == SignatureTypeCode.TypeHandle
            && IsType(metadata, signature.ReadTypeHandle(), DescriptorInterface);
    }

    /// <summary>The literal of a method whose whole body is <c>ldstr &lt;token&gt;; ret</c>, or null for any other body.</summary>
    private static string? ReadReturnedLiteral(MetadataReader metadata, MethodBodyBlock body)
    {
        const byte Ldstr = 0x72;
        const byte Ret = 0x2A;

        BlobReader il = body.GetILReader();

        if (il.Length != 6 || il.ReadByte() != Ldstr)
        {
            return null;
        }

        Handle literal = MetadataTokens.Handle(il.ReadInt32());

        return il.ReadByte() == Ret && literal.Kind == HandleKind.UserString
            ? metadata.GetUserString((UserStringHandle)literal)
            : null;
    }

    /// <summary>Namespace, enclosing types and name, for messages.</summary>
    private static string FullName(MetadataReader metadata, TypeDefinition type)
    {
        string name = metadata.GetString(type.Name);

        for (TypeDefinitionHandle outer = type.GetDeclaringType(); !outer.IsNil;)
        {
            type = metadata.GetTypeDefinition(outer);
            name = metadata.GetString(type.Name) + "." + name;
            outer = type.GetDeclaringType();
        }

        string ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
