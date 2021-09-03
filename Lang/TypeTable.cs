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

        public static bool Add(string name, IType type)
        {
            if (Types.TryAdd(name, type))
            {
                type.TypeIndex = Count++;
                // Set a temporary value of null before the type data is fully determined
                TypeInfos.Add(IntPtr.Zero);
                // CreateTypeInfo(type);
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
            var typeInfo = new TypeInfo {Name = Allocator.MakeString(function.Name), Type = TypeKind.Function};
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

            var typeInfoPointer = Allocator.Allocate(TypeInfoSize);
            TypeInfos.Add(typeInfoPointer);
            Marshal.StructureToPtr(typeInfo, typeInfoPointer, false);

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

        private const int TypeInfoSize = 80;
        [StructLayout(LayoutKind.Explicit, Size=TypeInfoSize)]
        public struct TypeInfo
        {
            [FieldOffset(0)] public String Name;
            [FieldOffset(12)] public TypeKind Type;
            [FieldOffset(16)] public uint Size;
            [FieldOffset(20)] public Array Fields;
            [FieldOffset(32)] public Array EnumValues;
            [FieldOffset(44)] public IntPtr ReturnType;
            [FieldOffset(52)] public Array Arguments;
            [FieldOffset(64)] public IntPtr PointerType;
            [FieldOffset(72)] public IntPtr ElementType;
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
            var typeInfo = new TypeInfo {Name = Allocator.MakeString(type.Name), Type = type.TypeKind, Size = type.Size};

            switch (type)
            {
                case StructAst structAst when structAst.Fields.Count > 0:
                    typeInfo.Fields.Length = structAst.Fields.Count;
                    var typeFields = new TypeField[typeInfo.Fields.Length];

                    for (var i = 0; i < typeInfo.Fields.Length; i++)
                    {
                        var field = structAst.Fields[i];
                        var typeField = new TypeField {Name = Allocator.MakeString(field.Name), Offset = field.Offset, TypeInfo = TypeInfos[field.Type.TypeIndex]};
                        typeFields[i] = typeField;
                    }

                    var typeFieldsArraySize = typeInfo.Fields.Length * TypeFieldSize;
                    var typeFieldsPointer = Allocator.Allocate(typeFieldsArraySize);
                    fixed (TypeField* pointer = &typeFields[0])
                    {
                        Buffer.MemoryCopy(pointer, typeFieldsPointer.ToPointer(), typeFieldsArraySize, typeFieldsArraySize);
                    }
                    typeInfo.Fields.Data = typeFieldsPointer;
                    break;
                case EnumAst enumAst:
                    typeInfo.EnumValues.Length = enumAst.Values.Count;
                    var enumValues = new EnumValue[typeInfo.EnumValues.Length];

                    for (var i = 0; i < typeInfo.EnumValues.Length; i++)
                    {
                        var value = enumAst.Values[i];
                        var enumValue = new EnumValue {Name = Allocator.MakeString(value.Name), Value = value.Value};
                        enumValues[i] = enumValue;
                    }

                    var enumValuesArraySize = typeInfo.EnumValues.Length * EnumValueSize;
                    var enumValuesPointer = Allocator.Allocate(enumValuesArraySize);
                    fixed (EnumValue* pointer = &enumValues[0])
                    {
                        Buffer.MemoryCopy(pointer, enumValuesPointer.ToPointer(), enumValuesArraySize, enumValuesArraySize);
                    }
                    typeInfo.EnumValues.Data = enumValuesPointer;
                    break;
                case PrimitiveAst primitive when primitive.TypeKind == TypeKind.Pointer:
                    typeInfo.PointerType = TypeInfos[primitive.PointerType.TypeIndex];
                    break;
                case ArrayType arrayType:
                    typeInfo.ElementType = TypeInfos[arrayType.ElementType.TypeIndex];
                    break;
            }

            var typeInfoPointer = Allocator.Allocate(TypeInfoSize);
            TypeInfos[type.TypeIndex] = typeInfoPointer;
            Marshal.StructureToPtr(typeInfo, typeInfoPointer, false);
        }
    }
}
