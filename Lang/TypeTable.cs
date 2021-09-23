using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lang
{
    public unsafe static class TypeTable
    {
        public static int Count { get; set; }
        public static Dictionary<string, IType> Types { get; } = new();
        public static Dictionary<string, List<FunctionAst>> Functions { get; } = new();

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
        public static StructAst AnyType;

        public static bool Add(string name, IType type)
        {
            if (Types.TryAdd(name, type))
            {
                type.TypeIndex = Count++;
                // Set a temporary value of null before the type data is fully determined
                TypeInfos.Add(IntPtr.Zero);
                return true;
            }
            return false;
        }

        public static List<FunctionAst> AddFunction(string name, FunctionAst function)
        {
            if (!Functions.TryGetValue(name, out var functions))
            {
                Functions[name] = functions = new List<FunctionAst>();
            }
            function.TypeIndex = Count++;
            function.OverloadIndex = functions.Count;
            functions.Add(function);

            // Add function to type infos
            if (ErrorReporter.Errors.Count == 0)
            {
                var typeInfo = new FunctionTypeInfo {Name = Allocator.MakeString(function.Name), Type = TypeKind.Function};
                typeInfo.ReturnType = TypeInfos[function.ReturnType.TypeIndex];

                var argumentCount = function.Flags.HasFlag(FunctionFlags.Varargs) ? function.Arguments.Count - 1 : function.Arguments.Count;
                if (argumentCount > 0)
                {
                    typeInfo.Arguments.Length = argumentCount;
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
                    typeInfo.Arguments.Data = argumentTypesPointer;
                }

                var typeInfoPointer = Allocator.Allocate(FunctionTypeInfoSize);
                TypeInfos.Add(typeInfoPointer);
                Marshal.StructureToPtr(typeInfo, typeInfoPointer, false);
            }
            else
            {
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

        private const int TypeInfoSize = 20;
        [StructLayout(LayoutKind.Explicit, Size=TypeInfoSize)]
        public struct TypeInfo
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public TypeKind Type;
            [FieldOffset(16)] public uint Size;
        }

        // TODO Use Marshal.SizeOf instead
        private const int IntegerTypeInfoSize = 21;
        [StructLayout(LayoutKind.Explicit, Size=IntegerTypeInfoSize)]
        public struct IntegerTypeInfo
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public TypeKind Type;
            [FieldOffset(16)] public uint Size;
            [FieldOffset(20)] public bool Signed;
        }

        private const int PointerTypeInfoSize = 28;
        [StructLayout(LayoutKind.Explicit, Size=TypeInfoSize)]
        public struct PointerTypeInfo
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public TypeKind Type;
            [FieldOffset(16)] public uint Size;
            [FieldOffset(20)] public IntPtr PointerType;
        }

        private const int CArrayTypeInfoSize = 32;
        [StructLayout(LayoutKind.Explicit, Size=CArrayTypeInfoSize)]
        public struct CArrayTypeInfo
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public TypeKind Type;
            [FieldOffset(16)] public uint Size;
            [FieldOffset(20)] public uint Length;
            [FieldOffset(24)] public IntPtr ElementType;
        }

        private const int EnumTypeInfoSize = 40;
        [StructLayout(LayoutKind.Explicit, Size=EnumTypeInfoSize)]
        public struct EnumTypeInfo
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public TypeKind Type;
            [FieldOffset(16)] public uint Size;
            [FieldOffset(20)] public IntPtr BaseType;
            [FieldOffset(28)] public Array Values;
        }

        private const int StructTypeInfoSize = 32;
        [StructLayout(LayoutKind.Explicit, Size=StructTypeInfoSize)]
        public struct StructTypeInfo
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public TypeKind Type;
            [FieldOffset(16)] public uint Size;
            [FieldOffset(20)] public Array Fields;
        }

        private const int FunctionTypeInfoSize = 40;
        [StructLayout(LayoutKind.Explicit, Size=FunctionTypeInfoSize)]
        public struct FunctionTypeInfo
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public TypeKind Type;
            [FieldOffset(16)] public uint Size;
            [FieldOffset(20)] public IntPtr ReturnType;
            [FieldOffset(28)] public Array Arguments;
        }

        [StructLayout(LayoutKind.Explicit, Size=12)]
        public struct Array
        {
            [FieldOffset(0)] public int Length;
            [FieldOffset(4)] public IntPtr Data;
        }

        private const int TypeFieldSize = 24;
        [StructLayout(LayoutKind.Explicit, Size=TypeFieldSize)]
        public struct TypeField
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public uint Offset;
            [FieldOffset(16)] public IntPtr TypeInfo;
        }

        private const int EnumValueSize = 16;
        [StructLayout(LayoutKind.Explicit, Size=EnumValueSize)]
        public struct EnumValue
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public int Value;
        }

        private const int ArgumentTypeSize = 20;
        [StructLayout(LayoutKind.Explicit, Size=ArgumentTypeSize)]
        public struct ArgumentType
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public IntPtr TypeInfo;
        }

        public static void CreateTypeInfo(IType type)
        {
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
                    var integerTypeInfo = new IntegerTypeInfo {Name = name, Type = type.TypeKind, Size = type.Size, Signed = integerType.Signed};
                    Marshal.StructureToPtr(integerTypeInfo, typeInfoPointer, false);
                    break;
                case TypeKind.Pointer:
                    typeInfoPointer = Allocator.Allocate(PointerTypeInfoSize);
                    var pointerType = (PrimitiveAst)type;
                    var pointerTypeInfo = new PointerTypeInfo {Name = name, Type = type.TypeKind, Size = type.Size, PointerType = TypeInfos[pointerType.PointerType.TypeIndex]};
                    Marshal.StructureToPtr(pointerTypeInfo, typeInfoPointer, false);
                    break;
                case TypeKind.CArray:
                    typeInfoPointer = Allocator.Allocate(CArrayTypeInfoSize);
                    var arrayType = (ArrayType)type;
                    var arrayTypeInfo = new CArrayTypeInfo {Name = name, Type = type.TypeKind, Size = type.Size, ElementType = TypeInfos[arrayType.ElementType.TypeIndex]};
                    Marshal.StructureToPtr(arrayTypeInfo, typeInfoPointer, false);
                    break;
                case TypeKind.Enum:
                    typeInfoPointer = Allocator.Allocate(EnumTypeInfoSize);
                    var enumType = (EnumAst)type;
                    var enumTypeInfo = new EnumTypeInfo {Name = name, Type = type.TypeKind, Size = type.Size, BaseType = TypeInfos[enumType.BaseType.TypeIndex]};

                    enumTypeInfo.Values.Length = enumType.Values.Count;
                    var enumValues = new EnumValue[enumTypeInfo.Values.Length];

                    for (var i = 0; i < enumTypeInfo.Values.Length; i++)
                    {
                        var value = enumType.Values[i];
                        var enumValue = new EnumValue {Name = Allocator.MakeString(value.Name), Value = value.Value};
                        enumValues[i] = enumValue;
                    }

                    var enumValuesArraySize = enumTypeInfo.Values.Length * EnumValueSize;
                    var enumValuesPointer = Allocator.Allocate(enumValuesArraySize);
                    fixed (EnumValue* pointer = &enumValues[0])
                    {
                        Buffer.MemoryCopy(pointer, enumValuesPointer.ToPointer(), enumValuesArraySize, enumValuesArraySize);
                    }
                    enumTypeInfo.Values.Data = enumValuesPointer;

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

                        for (var i = 0; i < structTypeInfo.Fields.Length; i++)
                        {
                            var field = structType.Fields[i];
                            var typeField = new TypeField {Name = Allocator.MakeString(field.Name), Offset = field.Offset, TypeInfo = TypeInfos[field.Type.TypeIndex]};
                            typeFields[i] = typeField;
                        }

                        var typeFieldsArraySize = structTypeInfo.Fields.Length * TypeFieldSize;
                        var typeFieldsPointer = Allocator.Allocate(typeFieldsArraySize);
                        fixed (TypeField* pointer = &typeFields[0])
                        {
                            Buffer.MemoryCopy(pointer, typeFieldsPointer.ToPointer(), typeFieldsArraySize, typeFieldsArraySize);
                        }
                        structTypeInfo.Fields.Data = typeFieldsPointer;
                    }

                    Marshal.StructureToPtr(structTypeInfo, typeInfoPointer, false);
                    break;
            }

            TypeInfos[type.TypeIndex] = typeInfoPointer;
        }
    }
}
