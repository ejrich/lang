using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lang
{
    public static class TypeTable
    {
        public static int Count { get; set; }
        public static Dictionary<string, IType> Types { get; } = new();
        public static Dictionary<string, List<FunctionAst>> Functions { get; } = new();

        public static bool Add(string name, IType type)
        {
            if (Types.TryAdd(name, type))
            {
                type.TypeIndex = Count++;
                CreateTypeInfo(type);
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
            CreateTypeInfo(function);

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

        private static void CreateTypeInfo(IType type)
        {
            // Create TypeInfo pointer
            var typeInfoPointer = Marshal.AllocHGlobal(TypeInfoSize);
            // TypeInfos[type.TypeIndex] = typeInfoPointer;

            // var typeNameField = typeInfoType.GetField("name");
            // typeNameField.SetValue(typeInfo, GetString(type.Name));
            // var typeKindField = typeInfoType.GetField("type");
            // typeKindField.SetValue(typeInfo, type.TypeKind);
            // var typeSizeField = typeInfoType.GetField("size");
            // typeSizeField.SetValue(typeInfo, type.Size);

            // Set fields and enum values on TypeInfo objects
            // var typeFieldArrayType = _types["Array.TypeField"];
            // var typeFieldType = _types["TypeField"];
            // var typeFieldSize = Marshal.SizeOf(typeFieldType);

            // var enumValueArrayType = _types["Array.EnumValue"];
            // var enumValueType = _types["EnumValue"];
            // var enumValueSize = Marshal.SizeOf(enumValueType);

            // var argumentArrayType = _types["Array.ArgumentType"];
            // var argumentType = _types["ArgumentType"];
            // var argumentSize = Marshal.SizeOf(argumentType);

            // switch (type)
            // {
            //     case StructAst structAst:
            //         var typeFieldArray = Activator.CreateInstance(typeFieldArrayType);
            //         InitializeConstArray(typeFieldArray, typeFieldArrayType, typeFieldSize, structAst.Fields.Count);

            //         var typeFieldsField = typeInfoType.GetField("fields");
            //         typeFieldsField.SetValue(typeInfo, typeFieldArray);

            //         var typeFieldArrayDataField = typeFieldArrayType.GetField("data");
            //         var typeFieldsDataPointer = GetPointer(typeFieldArrayDataField.GetValue(typeFieldArray));

            //         for (var i = 0; i < structAst.Fields.Count; i++)
            //         {
            //             var field = structAst.Fields[i];
            //             var typeField = Activator.CreateInstance(typeFieldType);

            //             var typeFieldName = typeFieldType.GetField("name");
            //             typeFieldName.SetValue(typeField, GetString(field.Name));
            //             var typeFieldOffset = typeFieldType.GetField("offset");
            //             typeFieldOffset.SetValue(typeField, field.Offset);
            //             var typeFieldInfo = typeFieldType.GetField("type_info");
            //             var typePointer = _typeInfoPointers[field.TypeDefinition.GenericName];
            //             typeFieldInfo.SetValue(typeField, typePointer);

            //             var arrayPointer = IntPtr.Add(typeFieldsDataPointer, typeFieldSize * i);
            //             Marshal.StructureToPtr(typeField, arrayPointer, false);
            //         }
            //         break;
            //     case EnumAst enumAst:
            //         var enumValueArray = Activator.CreateInstance(enumValueArrayType);
            //         InitializeConstArray(enumValueArray, enumValueArrayType, enumValueSize, enumAst.Values.Count);

            //         var enumValuesField = typeInfoType.GetField("enum_values");
            //         enumValuesField.SetValue(typeInfo, enumValueArray);

            //         var enumValuesArrayDataField = enumValueArrayType.GetField("data");
            //         var enumValuesDataPointer = GetPointer(enumValuesArrayDataField.GetValue(enumValueArray));

            //         for (var i = 0; i < enumAst.Values.Count; i++)
            //         {
            //             var value = enumAst.Values[i];
            //             var enumValue = Activator.CreateInstance(enumValueType);

            //             var enumValueName = enumValueType.GetField("name");
            //             enumValueName.SetValue(enumValue, GetString(value.Name));
            //             var enumValueValue = enumValueType.GetField("value");
            //             enumValueValue.SetValue(enumValue, value.Value);

            //             var arrayPointer = IntPtr.Add(enumValuesDataPointer, enumValueSize * i);
            //             Marshal.StructureToPtr(enumValue, arrayPointer, false);
            //         }
            //         break;
            //     case FunctionAst function:
            //         var returnTypeField = typeInfoType.GetField("return_type");
            //         returnTypeField.SetValue(typeInfo, _typeInfoPointers[function.ReturnTypeDefinition.GenericName]);

            //         var argumentArray = Activator.CreateInstance(argumentArrayType);
            //         var argumentCount = function.Flags.HasFlag(FunctionFlags.Varargs) ? function.Arguments.Count - 1 : function.Arguments.Count;
            //         InitializeConstArray(argumentArray, argumentArrayType, argumentSize, argumentCount);

            //         var argumentsField = typeInfoType.GetField("arguments");
            //         argumentsField.SetValue(typeInfo, argumentArray);

            //         var argumentArrayDataField = argumentArrayType.GetField("data");
            //         var argumentArrayDataPointer = GetPointer(argumentArrayDataField.GetValue(argumentArray));

            //         for (var i = 0; i < argumentCount; i++)
            //         {
            //             var argument = function.Arguments[i];
            //             var argumentValue = Activator.CreateInstance(argumentType);

            //             var argumentName = argumentType.GetField("name");
            //             argumentName.SetValue(argumentValue, GetString(argument.Name));
            //             var argumentTypeField = argumentType.GetField("type_info");

            //             var argumentTypeInfoPointer = argument.TypeDefinition.TypeKind switch
            //             {
            //                 TypeKind.Type => _typeInfoPointers["s32"],
            //                 TypeKind.Params => _typeInfoPointers[$"Array.{argument.TypeDefinition.Generics[0].GenericName}"],
            //                 _ => _typeInfoPointers[argument.TypeDefinition.GenericName]
            //             };
            //             argumentTypeField.SetValue(argumentValue, argumentTypeInfoPointer);

            //             var arrayPointer = IntPtr.Add(argumentArrayDataPointer, argumentSize * i);
            //             Marshal.StructureToPtr(argumentValue, arrayPointer, false);
            //         }
            //         break;
            // }
        }
    }
}
