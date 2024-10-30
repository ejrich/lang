using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ol;

[StructLayout(LayoutKind.Explicit, Size=16)]
public struct ArrayStruct
{
    [FieldOffset(0)] public long Length;
    [FieldOffset(8)] public IntPtr Data;
}

public static unsafe class TypeTable
{
    public static int Count;

    public static List<IType> Types { get; } = new();
    public static List<IntPtr> TypeInfos { get; } = new();

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
    public static IType FloatType;
    public static IType Float64Type;
    public static IType TypeType;
    public static StructAst StringType;
    public static IType RawStringType;
    public static StructAst AnyType;
    public static IType TypeInfoPointerType;
    public static IType VoidPointerType;

    public static void Add(IType type)
    {
        lock (TypeInfos)
        {
            type.TypeIndex = Count++;
            Types.Add(type);
            TypeInfos.Add(IntPtr.Zero);
        }
    }

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
        [FieldOffset(20)] public uint Size;
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
        [FieldOffset(32)] public ArrayStruct Values;
        [FieldOffset(48)] public ArrayStruct Attributes;
    }

    private const int StructTypeInfoSize = 56;
    [StructLayout(LayoutKind.Explicit, Size=StructTypeInfoSize)]
    public struct StructTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public ArrayStruct Fields;
        [FieldOffset(40)] public ArrayStruct Attributes;
    }

    private const int UnionTypeInfoSize = 40;
    [StructLayout(LayoutKind.Explicit, Size=UnionTypeInfoSize)]
    public struct UnionTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public ArrayStruct Fields;
    }

    private const int CompoundTypeInfoSize = 40;
    [StructLayout(LayoutKind.Explicit, Size=CompoundTypeInfoSize)]
    public struct CompoundTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public ArrayStruct Types;
    }

    private const int InterfaceTypeInfoSize = 48;
    [StructLayout(LayoutKind.Explicit, Size=InterfaceTypeInfoSize)]
    public struct InterfaceTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public IntPtr ReturnType;
        [FieldOffset(32)] public ArrayStruct Arguments;
    }

    private const int FunctionTypeInfoSize = 64;
    [StructLayout(LayoutKind.Explicit, Size=FunctionTypeInfoSize)]
    public struct FunctionTypeInfo
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public TypeKind Type;
        [FieldOffset(20)] public uint Size;
        [FieldOffset(24)] public IntPtr ReturnType;
        [FieldOffset(32)] public ArrayStruct Arguments;
        [FieldOffset(48)] public ArrayStruct Attributes;
    }

    private const int TypeFieldSize = 48;
    [StructLayout(LayoutKind.Explicit, Size=TypeFieldSize)]
    public struct TypeField
    {
        [FieldOffset(0)] public String Name;
        [FieldOffset(16)] public uint Offset;
        [FieldOffset(24)] public IntPtr TypeInfo;
        [FieldOffset(32)] public ArrayStruct Attributes;
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
        [FieldOffset(16)] public long Value;
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
                var pointerType = (PointerType)type;
                var pointerTypeInfo = new PointerTypeInfo {Name = name, Type = TypeKind.Pointer, Size = 8, PointerType = TypeInfos[pointerType.PointedType.TypeIndex]};
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
                Span<EnumValue> enumValues = stackalloc EnumValue[enumType.Values.Count];

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
                    enumTypeInfo.Attributes = MakeAttributes(enumType.Attributes);
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
                    Span<TypeField> typeFields = stackalloc TypeField[structType.Fields.Count];

                    for (var i = 0; i < structType.Fields.Count; i++)
                    {
                        var field = structType.Fields[i];
                        var typeField = new TypeField {Name = Allocator.MakeString(field.Name), Offset = field.Offset, TypeInfo = TypeInfos[field.Type.TypeIndex]};

                        if (field.Attributes != null)
                        {
                            typeField.Attributes = MakeAttributes(field.Attributes);
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
                    structTypeInfo.Attributes = MakeAttributes(structType.Attributes);
                }

                Marshal.StructureToPtr(structTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.Compound:
                typeInfoPointer = Allocator.Allocate(CompoundTypeInfoSize);
                var compoundType = (CompoundType)type;
                var compoundTypeInfo = new CompoundTypeInfo {Name = name, Type = TypeKind.Compound, Size = type.Size};

                compoundTypeInfo.Types.Length = compoundType.Types.Length;
                Span<IntPtr> types = stackalloc IntPtr[compoundType.Types.Length];

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
                Span<UnionField> unionFields = stackalloc UnionField[union.Fields.Count];

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
                var interfaceTypeInfo = new InterfaceTypeInfo {Name = name, Type = TypeKind.Interface, Size = 8, ReturnType = TypeInfos[interfaceAst.ReturnType.TypeIndex]};

                if (interfaceAst.Arguments.Count > 0)
                {
                    interfaceTypeInfo.Arguments = MakeArguments(interfaceAst.Arguments);
                }

                Marshal.StructureToPtr(interfaceTypeInfo, typeInfoPointer, false);
                break;
            case TypeKind.Function:
                typeInfoPointer = Allocator.Allocate(FunctionTypeInfoSize);
                var function = (FunctionAst)type;
                var functionTypeInfo = new FunctionTypeInfo {Name = name, Type = TypeKind.Function};
                functionTypeInfo.ReturnType = TypeInfos[function.ReturnType.TypeIndex];

                if (function.Flags.HasFlag(FunctionFlags.Varargs))
                {
                    if (function.Arguments.Count > 1)
                    {
                        functionTypeInfo.Arguments = MakeArguments(function.Arguments.GetRange(0, function.Arguments.Count - 1));
                    }
                }
                else if (function.Arguments.Count > 0)
                {
                    functionTypeInfo.Arguments = MakeArguments(function.Arguments);
                }

                if (function.Attributes != null)
                {
                    functionTypeInfo.Attributes = MakeAttributes(function.Attributes);
                }

                Marshal.StructureToPtr(functionTypeInfo, typeInfoPointer, false);
                break;
        }

        TypeInfos[type.TypeIndex] = typeInfoPointer;
    }

    public static ArrayStruct MakeArguments(List<DeclarationAst> argumentAsts)
    {
        Span<ArgumentType> arguments = stackalloc ArgumentType[argumentAsts.Count];

        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = argumentAsts[i];
            arguments[i] = new ArgumentType {Name = Allocator.MakeString(argument.Name), TypeInfo = TypeInfos[argument.Type.TypeIndex]};
        }

        var argumentTypesArraySize = arguments.Length * ArgumentTypeSize;
        var argumentTypesPointer = Allocator.Allocate(argumentTypesArraySize);
        fixed (ArgumentType* pointer = &arguments[0])
        {
            Buffer.MemoryCopy(pointer, argumentTypesPointer.ToPointer(), argumentTypesArraySize, argumentTypesArraySize);
        }

        return new ArrayStruct { Length = arguments.Length, Data = argumentTypesPointer };
    }

    private static ArrayStruct MakeAttributes(List<string> attributesList)
    {
        Span<String> attributes = stackalloc String[attributesList.Count];

        for (var i = 0; i < attributes.Length; i++)
        {
            attributes[i] = Allocator.MakeString(attributesList[i]);
        }

        var attributesArraySize = attributes.Length * Allocator.StringLength;
        var attributesPointer = Allocator.Allocate(attributesArraySize);
        fixed (String* pointer = &attributes[0])
        {
            Buffer.MemoryCopy(pointer, attributesPointer.ToPointer(), attributesArraySize, attributesArraySize);
        }

        return new ArrayStruct { Length = attributes.Length, Data = attributesPointer };
    }
}
