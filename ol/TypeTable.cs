using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ol;

public unsafe static class TypeTable
{
    public static int Count;
    public static ConcurrentDictionary<string, IType> Types { get; } = new();
    public static ConcurrentDictionary<string, List<FunctionAst>> Functions { get; } = new();

    public static IType VoidType;
    public static IType BoolType;
    public static IType S8Type;
    public static IType U8Type;
    public static IType S16Type;
    public static IType U16Type;
    public static PrimitiveAst S32Type;
    public static IType U32Type;
    public static IType S64Type;
    public static IType U64Type;
    public static IType Float64Type;
    public static IType TypeType;
    public static StructAst StringType;
    public static IType RawStringType;
    public static StructAst AnyType;

    public static bool Add(string name, IType type)
    {
        if (Types.TryAdd(name, type))
        {
            // Set a temporary value of null before the type data is fully determined
            lock (TypeInfos)
            {
                type.TypeIndex = Count++;
                TypeInfos.Add(IntPtr.Zero);
            }
            return true;
        }
        return false;
    }

    public static List<FunctionAst> AddFunction(string name, FunctionAst function)
    {
        var functions = Functions.GetOrAdd(name, _ => new List<FunctionAst>());
        function.OverloadIndex = functions.Count;
        functions.Add(function);

        lock(TypeInfos)
        {
            function.TypeIndex = Count++;
            TypeInfos.Add(IntPtr.Zero);
        }

        return functions;
    }

    public static IType GetType(TypeDefinition typeDef)
    {
        var typeName = typeDef.Name switch
        {
            "Type" => "s32",
            "Params" => $"Array.{typeDef.Generics[0].GenericName}",
            _ => typeDef.GenericName
        };

        Types.TryGetValue(typeName, out var type);
        return type;
    }

    public static List<IntPtr> TypeInfos { get; } = new();

    private const int TypeInfoSize = 24;
    [StructLayout(LayoutKind.Explicit, Size=TypeInfoSize)]
    public struct TypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
    }

    private const int IntegerTypeInfoSize = 28;
    [StructLayout(LayoutKind.Explicit, Size=IntegerTypeInfoSize)]
    public struct IntegerTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public bool Signed;
    }

    private const int PointerTypeInfoSize = 32;
    [StructLayout(LayoutKind.Explicit, Size=TypeInfoSize)]
    public struct PointerTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size = 8;
        [FieldOffset(24)] public IntPtr PointerType;
    }

    private const int CArrayTypeInfoSize = 40;
    [StructLayout(LayoutKind.Explicit, Size=CArrayTypeInfoSize)]
    public struct CArrayTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public uint Length;
        [FieldOffset(32)] public IntPtr ElementType;
    }

    private const int EnumTypeInfoSize = 64;
    [StructLayout(LayoutKind.Explicit, Size=EnumTypeInfoSize)]
    public struct EnumTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public IntPtr BaseType;
        [FieldOffset(32)] public Array Values;
        [FieldOffset(48)] public Array Attributes;
    }

    private const int StructTypeInfoSize = 56;
    [StructLayout(LayoutKind.Explicit, Size=StructTypeInfoSize)]
    public struct StructTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public Array Fields;
        [FieldOffset(40)] public Array Attributes;
    }

    private const int UnionTypeInfoSize = 40;
    [StructLayout(LayoutKind.Explicit, Size=UnionTypeInfoSize)]
    public struct UnionTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public Array Fields;
    }

    private const int CompoundTypeInfoSize = 40;
    [StructLayout(LayoutKind.Explicit, Size=CompoundTypeInfoSize)]
    public struct CompoundTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public Array Types;
    }

    private const int InterfaceTypeInfoSize = 48;
    [StructLayout(LayoutKind.Explicit, Size=InterfaceTypeInfoSize)]
    public struct InterfaceTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size = 8;
        [FieldOffset(24)] public IntPtr ReturnType;
        [FieldOffset(32)] public Array Arguments;
    }

    private const int FunctionTypeInfoSize = 64;
    [StructLayout(LayoutKind.Explicit, Size=FunctionTypeInfoSize)]
    public struct FunctionTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public IntPtr ReturnType;
        [FieldOffset(32)] public Array Arguments;
        [FieldOffset(48)] public Array Attributes;
    }

    [StructLayout(LayoutKind.Explicit, Size=16)]
    public struct Array
    {
        [FieldOffset(0)] public long Length;
        [FieldOffset(8)] public IntPtr Data;
    }

    private const int TypeFieldSize = 48;
    [StructLayout(LayoutKind.Explicit, Size=TypeFieldSize)]
    public struct TypeField
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public uint Offset;
        [FieldOffset(24)] public IntPtr TypeInfo;
        [FieldOffset(32)] public Array Attributes;
    }

    private const int UnionFieldSize = 24;
    [StructLayout(LayoutKind.Explicit, Size=UnionFieldSize)]
    public struct UnionField
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public IntPtr TypeInfo;
    }

    private const int EnumValueSize = 24;
    [StructLayout(LayoutKind.Explicit, Size=EnumValueSize)]
    public struct EnumValue
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public int Value;
    }

    private const int ArgumentTypeSize = 24;
    [StructLayout(LayoutKind.Explicit, Size=ArgumentTypeSize)]
    public struct ArgumentType
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public IntPtr TypeInfo;
    }

    public static void CreateTypeInfo(IType type)
    {
        if (ErrorReporter.Errors.Count > 0) return;

        var typeInfoPointer = IntPtr.Zero;
        var name = Allocator.MakeString(type.Name);

        switch (type.TypeKind)
        {
            case TypeKind.Void:
            case TypeKind.Boolean:
            case TypeKind.Type:
            case TypeKind.Float:
                typeInfoPointer = Allocator.Allocate(TypeInfoSize);
                var typeInfo = new TypeInfo {Name = name, Type = type.TypeKind, Size = type.Size};
                Marshal.StructureToPtr(typeInfo, typeInfoPointer, false);
                break;
            case TypeKind.Integer:
                typeInfoPointer = Allocator.Allocate(IntegerTypeInfoSize);
                var integerType = (PrimitiveAst)type;
                var integerTypeInfo = new IntegerTypeInfo {Name = name, Type = TypeKind.Integer, Size = type.Size, Signed = integerType.Signed};
                Marshal.StructureToPtr(integerTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.Pointer:
                typeInfoPointer = Allocator.Allocate(PointerTypeInfoSize);
                var pointerType = (PrimitiveAst)type;
                var pointerTypeInfo = new PointerTypeInfo {Name = name, Type = TypeKind.Pointer, PointerType = TypeInfos[pointerType.PointerType.TypeIndex]};
                Marshal.StructureToPtr(pointerTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.CArray:
                typeInfoPointer = Allocator.Allocate(CArrayTypeInfoSize);
                var arrayType = (ArrayType)type;
                var arrayTypeInfo = new CArrayTypeInfo {Name = name, Type = TypeKind.CArray, Size = type.Size, ElementType = TypeInfos[arrayType.ElementType.TypeIndex]};
                Marshal.StructureToPtr(arrayTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.Enum:
                typeInfoPointer = Allocator.Allocate(EnumTypeInfoSize);
                var enumType = (EnumAst)type;
                var enumTypeInfo = new EnumTypeInfo {Name = name, Type = TypeKind.Enum, Size = type.Size, BaseType = TypeInfos[enumType.BaseType.TypeIndex]};

                enumTypeInfo.Values.Length = enumType.Values.Count;
                var enumValues = new EnumValue[enumTypeInfo.Values.Length];

                foreach (var (valueName, value) in enumType.Values)
                {
                    var enumValue = new EnumValue {Name = Allocator.MakeString(valueName), Value = value.Value};
                    enumValues[value.Index] = enumValue;
                }

                var enumValuesArraySize = enumValues.Length * EnumValueSize;
                var enumValuesPointer = Allocator.Allocate(enumValuesArraySize);
                fixed (EnumValue* pointer = &enumValues[0])
                {
                    Buffer.MemoryCopy(pointer, enumValuesPointer.ToPointer(), enumValuesArraySize, enumValuesArraySize);
                }
                enumTypeInfo.Values.Data = enumValuesPointer;

                if (enumType.Attributes != null)
                {
                    enumTypeInfo.Attributes.Length = enumType.Attributes.Count;
                    var attributes = new String[enumTypeInfo.Attributes.Length];

                    for (var i = 0; i < attributes.Length; i++)
                    {
                        attributes[i] = Allocator.MakeString(enumType.Attributes[i]);
                    }

                    var attributesArraySize = attributes.Length * Allocator.StringLength;
                    var attributesPointer = Allocator.Allocate(attributesArraySize);
                    fixed (String* pointer = &attributes[0])
                    {
                        Buffer.MemoryCopy(pointer, attributesPointer.ToPointer(), attributesArraySize, attributesArraySize);
                    }
                    enumTypeInfo.Attributes.Data = attributesPointer;
                }

                Marshal.StructureToPtr(enumTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.String:
            case TypeKind.Array:
            case TypeKind.Struct:
            case TypeKind.Any:
                typeInfoPointer = Allocator.Allocate(StructTypeInfoSize);
                var structType = (StructAst)type;
                var structTypeInfo = new StructTypeInfo {Name = name, Type = type.TypeKind, Size = type.Size};

                if (structType.Fields.Count > 0)
                {
                    structTypeInfo.Fields.Length = structType.Fields.Count;
                    var typeFields = new TypeField[structTypeInfo.Fields.Length];

                    for (var i = 0; i < structType.Fields.Count; i++)
                    {
                        var field = structType.Fields[i];
                        var typeField = new TypeField {Name = Allocator.MakeString(field.Name), Offset = field.Offset, TypeInfo = TypeInfos[field.Type.TypeIndex]};

                        if (field.Attributes != null)
                        {
                            typeField.Attributes.Length = field.Attributes.Count;
                            var attributes = new String[typeField.Attributes.Length];

                            for (var attributeIndex = 0; attributeIndex < attributes.Length; attributeIndex++)
                            {
                                attributes[attributeIndex] = Allocator.MakeString(field.Attributes[attributeIndex]);
                            }

                            var attributesArraySize = attributes.Length * Allocator.StringLength;
                            var attributesPointer = Allocator.Allocate(attributesArraySize);
                            fixed (String* pointer = &attributes[0])
                            {
                                Buffer.MemoryCopy(pointer, attributesPointer.ToPointer(), attributesArraySize, attributesArraySize);
                            }
                            typeField.Attributes.Data = attributesPointer;
                        }

                        typeFields[i] = typeField;
                    }

                    var typeFieldsArraySize = typeFields.Length * TypeFieldSize;
                    var typeFieldsPointer = Allocator.Allocate(typeFieldsArraySize);
                    fixed (TypeField* pointer = &typeFields[0])
                    {
                        Buffer.MemoryCopy(pointer, typeFieldsPointer.ToPointer(), typeFieldsArraySize, typeFieldsArraySize);
                    }
                    structTypeInfo.Fields.Data = typeFieldsPointer;
                }

                if (structType.Attributes != null)
                {
                    structTypeInfo.Attributes.Length = structType.Attributes.Count;
                    var attributes = new String[structTypeInfo.Attributes.Length];

                    for (var i = 0; i < attributes.Length; i++)
                    {
                        attributes[i] = Allocator.MakeString(structType.Attributes[i]);
                    }

                    var attributesArraySize = attributes.Length * Allocator.StringLength;
                    var attributesPointer = Allocator.Allocate(attributesArraySize);
                    fixed (String* pointer = &attributes[0])
                    {
                        Buffer.MemoryCopy(pointer, attributesPointer.ToPointer(), attributesArraySize, attributesArraySize);
                    }
                    structTypeInfo.Attributes.Data = attributesPointer;
                }

                Marshal.StructureToPtr(structTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.Compound:
                typeInfoPointer = Allocator.Allocate(CompoundTypeInfoSize);
                var compoundType = (CompoundType)type;
                var compoundTypeInfo = new CompoundTypeInfo {Name = name, Type = TypeKind.Compound, Size = type.Size};

                compoundTypeInfo.Types.Length = compoundType.Types.Length;
                var types = new IntPtr[compoundTypeInfo.Types.Length];

                for (var i = 0; i < types.Length; i++)
                {
                    var subType = compoundType.Types[i];
                    types[i] = TypeInfos[subType.TypeIndex];
                }

                var typesArraySize = types.Length * sizeof(IntPtr);
                var typesPointer = Allocator.Allocate(typesArraySize);
                fixed (IntPtr* pointer = &types[0])
                {
                    Buffer.MemoryCopy(pointer, typesPointer.ToPointer(), typesArraySize, typesArraySize);
                }
                compoundTypeInfo.Types.Data = typesPointer;

                Marshal.StructureToPtr(compoundTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.Union:
                typeInfoPointer = Allocator.Allocate(UnionTypeInfoSize);
                var union = (UnionAst)type;
                var unionTypeInfo = new UnionTypeInfo {Name = name, Type = TypeKind.Union, Size = type.Size};

                unionTypeInfo.Fields.Length = union.Fields.Count;
                var unionFields = new UnionField[unionTypeInfo.Fields.Length];

                for (var i = 0; i < unionFields.Length; i++)
                {
                    var field = union.Fields[i];
                    unionFields[i] = new UnionField {Name = Allocator.MakeString(field.Name), TypeInfo = TypeInfos[field.Type.TypeIndex]};
                }

                var unionFieldsArraySize = unionFields.Length * UnionFieldSize;
                var unionFieldsPointer = Allocator.Allocate(unionFieldsArraySize);
                fixed (UnionField* pointer = &unionFields[0])
                {
                    Buffer.MemoryCopy(pointer, unionFieldsPointer.ToPointer(), unionFieldsArraySize, unionFieldsArraySize);
                }
                unionTypeInfo.Fields.Data = unionFieldsPointer;

                Marshal.StructureToPtr(unionTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.Interface:
                typeInfoPointer = Allocator.Allocate(InterfaceTypeInfoSize);
                var interfaceAst = (InterfaceAst)type;
                var interfaceTypeInfo = new InterfaceTypeInfo {Name = name, Type = TypeKind.Interface, ReturnType = TypeInfos[interfaceAst.ReturnType.TypeIndex]};

                if (interfaceAst.Arguments.Count > 0)
                {
                    interfaceTypeInfo.Arguments.Length = interfaceAst.Arguments.Count;
                    var arguments = new ArgumentType[interfaceAst.Arguments.Count];

                    for (var i = 0; i < arguments.Length; i++)
                    {
                        var argument = interfaceAst.Arguments[i];
                        var argumentType = new ArgumentType {Name = Allocator.MakeString(argument.Name), TypeInfo = TypeInfos[argument.Type.TypeIndex]};
                        arguments[i] = argumentType;
                    }

                    var argumentTypesArraySize = arguments.Length * ArgumentTypeSize;
                    var argumentTypesPointer = Allocator.Allocate(argumentTypesArraySize);
                    fixed (ArgumentType* pointer = &arguments[0])
                    {
                        Buffer.MemoryCopy(pointer, argumentTypesPointer.ToPointer(), argumentTypesArraySize, argumentTypesArraySize);
                    }
                    interfaceTypeInfo.Arguments.Data = argumentTypesPointer;
                }

                Marshal.StructureToPtr(interfaceTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.Function:
                typeInfoPointer = Allocator.Allocate(FunctionTypeInfoSize);
                var function = (FunctionAst)type;
                var functionTypeInfo = new FunctionTypeInfo {Name = name, Type = TypeKind.Function};
                functionTypeInfo.ReturnType = TypeInfos[function.ReturnType.TypeIndex];

                var argumentCount = function.Flags.HasFlag(FunctionFlags.Varargs) ? function.Arguments.Count - 1 : function.Arguments.Count;
                if (argumentCount > 0)
                {
                    functionTypeInfo.Arguments.Length = argumentCount;
                    var arguments = new ArgumentType[argumentCount];

                    for (var i = 0; i < argumentCount; i++)
                    {
                        var argument = function.Arguments[i];
                        var argumentType = new ArgumentType {Name = Allocator.MakeString(argument.Name), TypeInfo = TypeInfos[argument.Type.TypeIndex]};
                        arguments[i] = argumentType;
                    }

                    var argumentTypesArraySize = argumentCount * ArgumentTypeSize;
                    var argumentTypesPointer = Allocator.Allocate(argumentTypesArraySize);
                    fixed (ArgumentType* pointer = &arguments[0])
                    {
                        Buffer.MemoryCopy(pointer, argumentTypesPointer.ToPointer(), argumentTypesArraySize, argumentTypesArraySize);
                    }
                    functionTypeInfo.Arguments.Data = argumentTypesPointer;
                }

                if (function.Attributes != null)
                {
                    functionTypeInfo.Attributes.Length = function.Attributes.Count;
                    var attributes = new String[functionTypeInfo.Attributes.Length];

                    for (var i = 0; i < attributes.Length; i++)
                    {
                        attributes[i] = Allocator.MakeString(function.Attributes[i]);
                    }

                    var attributesArraySize = attributes.Length * Allocator.StringLength;
                    var attributesPointer = Allocator.Allocate(attributesArraySize);
                    fixed (String* pointer = &attributes[0])
                    {
                        Buffer.MemoryCopy(pointer, attributesPointer.ToPointer(), attributesArraySize, attributesArraySize);
                    }
                    functionTypeInfo.Attributes.Data = attributesPointer;
                }

                Marshal.StructureToPtr(functionTypeInfo, typeInfoPointer, false);
                break;
        }

        TypeInfos[type.TypeIndex] = typeInfoPointer;
    }
}
