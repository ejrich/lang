using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Lang.Parsing;

namespace Lang.Runner
{
    public interface IProgramRunner
    {
        void Init(ProgramGraph programGraph);
        void RunProgram(IAst ast);
        bool ExecuteCondition(IAst expression);
    }

    public class ProgramRunner : IProgramRunner
    {
        private ModuleBuilder _moduleBuilder;
        private int _version;

        private ProgramGraph _programGraph;
        private int _typeCount;

        private readonly Dictionary<string, List<int>> _functionIndices = new();
        private readonly List<(Type type, object libraryObject)> _functionLibraries = new();
        private readonly Dictionary<string, ValueType> _globalVariables = new();
        private readonly Dictionary<string, Type> _types = new();
        private readonly Dictionary<string, IntPtr> _typeInfoPointers = new();

        private readonly Dictionary<string, string> _compilerFunctions = new() {
            { "add_dependency", "AddDependency" }
        };

        private class ValueType
        {
            public TypeDefinition Type { get; set; }
            public object Value { get; set; }
        }

        public void Init(ProgramGraph programGraph)
        {
            _programGraph = programGraph;
            // Initialize the runner
            if (_moduleBuilder == null)
            {
                var assemblyName = new AssemblyName("Runner");
                var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
                _moduleBuilder = assemblyBuilder.DefineDynamicModule("Runner");
            }

            var temporaryStructs = new Dictionary<string, TypeBuilder>();
            foreach (var (name, type) in programGraph.Types)
            {
                switch (type)
                {
                    case EnumAst enumAst:
                        if (_types.ContainsKey(enumAst.Name)) break;
                        var enumBuilder = _moduleBuilder.DefineEnum(enumAst.Name, TypeAttributes.Public, typeof(int));
                        foreach (var value in enumAst.Values)
                        {
                            enumBuilder.DefineLiteral(value.Name, value.Value);
                        }
                        _types[enumAst.Name] = enumBuilder.CreateTypeInfo();
                        break;
                    case StructAst structAst:
                        if (_types.ContainsKey(name)) break;
                        var structBuilder = _moduleBuilder.DefineType(name, TypeAttributes.Public | TypeAttributes.SequentialLayout);
                        temporaryStructs[name] = structBuilder;
                        break;
                }
            }

            var fieldIndices = new Dictionary<string, int>();
            while (temporaryStructs.Any())
            {
                foreach (var (name, structBuilder) in temporaryStructs)
                {
                    var indexFound = fieldIndices.TryGetValue(name, out var index);
                    var structAst = programGraph.Types[name] as StructAst;
                    var count = structAst!.Fields.Count;

                    for (; index < count; index++)
                    {
                        var field = structAst.Fields[index];
                        var fieldType = GetTypeFromDefinition(field.Type, temporaryStructs);
                        if (fieldType == null)
                        {
                            break;
                        }
                        var structField = structBuilder.DefineField(field.Name, fieldType, FieldAttributes.Public);
                        if (field.Type.CArray)
                        {
                            var caBuilder = new CustomAttributeBuilder(typeof(MarshalAsAttribute).GetConstructor(new []{typeof(UnmanagedType)}), new object[]{UnmanagedType.ByValArray});
                            // TODO Look more into this and finish it up
                            structField.SetCustomAttribute(caBuilder);
                        }
                    }

                    if (index >= count)
                    {
                        _types[name] = structBuilder.CreateType();
                        if (indexFound)
                        {
                            fieldIndices.Remove(name);
                        }
                        temporaryStructs.Remove(name);
                    }
                    else
                    {
                        fieldIndices[name] = index;
                    }
                }
            }

            TypeBuilder functionTypeBuilder = null;
            foreach (var function in programGraph.Functions.Values.Where(_ => _.Extern))
            {
                var returnType = GetTypeFromDefinition(function.ReturnType);

                if (!_functionIndices.TryGetValue(function.Name, out var functionIndex))
                    _functionIndices[function.Name] = functionIndex = new List<int>();

                if (function.Varargs)
                {
                    for (var i = functionIndex.Count; i < function.VarargsCalls.Count; i++)
                    {
                        functionTypeBuilder ??= _moduleBuilder.DefineType($"Functions{_version}", TypeAttributes.Class | TypeAttributes.Public);
                        var callTypes = function.VarargsCalls[i];
                        var varargs = callTypes.Select(arg => GetTypeFromDefinition(arg, cCall: true)).ToArray();
                        CreateFunction(functionTypeBuilder, function.Name, function.ExternLib, returnType, varargs);
                        functionIndex.Add(_version);
                    }
                }
                else
                {
                    if (!functionIndex.Any())
                    {
                        functionTypeBuilder ??= _moduleBuilder.DefineType($"Functions{_version}", TypeAttributes.Class | TypeAttributes.Public);
                        var args = function.Arguments.Select(arg => GetTypeFromDefinition(arg.Type, cCall: true)).ToArray();
                        CreateFunction(functionTypeBuilder, function.Name, function.ExternLib, returnType, args);
                        functionIndex.Add(_version);
                    }
                }
            }

            if (functionTypeBuilder != null)
            {
                var library = functionTypeBuilder.CreateType();
                var functionObject = Activator.CreateInstance(library);
                _functionLibraries.Add((library, functionObject));
                _version++;
            }

            foreach (var variable in programGraph.Variables)
            {
                if (!_globalVariables.ContainsKey(variable.Name))
                {
                    ExecuteDeclaration(variable, _globalVariables);
                }
            }

            var typeInfoCount = programGraph.Types.Count + programGraph.Functions.Count;
            if (_typeCount != typeInfoCount)
            {
                var typeTable = _globalVariables["__type_table"];

                // Save the previous pointer
                var typeInfoListType = _types[typeTable.Type.GenericName];
                var dataField = typeInfoListType.GetField("data");
                var oldDataPointer = GetPointer(dataField.GetValue(typeTable.Value));

                // Reallocate array
                var typeInfoPointerType = GetTypeFromDefinition(typeTable.Type.Generics[0]);
                var pointerSize = Marshal.SizeOf(typeInfoPointerType);
                InitializeConstList(typeTable.Value, typeInfoListType, typeInfoPointerType, typeInfoCount);
                var typeDataPointer = GetPointer(dataField.GetValue(typeTable.Value));
                Marshal.FreeHGlobal(oldDataPointer);

                // Create TypeInfo pointers
                var newTypeInfos = new List<(IType type, object typeInfo, IntPtr typeInfoPointer)>();
                var typeInfoType = _types["TypeInfo"];
                foreach (var (name, type) in programGraph.Types)
                {
                    if (!_typeInfoPointers.TryGetValue(name, out var typeInfoPointer))
                    {
                        var typeInfo = Activator.CreateInstance(typeInfoType);

                        var typeNameField = typeInfoType.GetField("name");
                        typeNameField.SetValue(typeInfo, GetString(type.Name));
                        var typeKindField = typeInfoType.GetField("type");
                        typeKindField.SetValue(typeInfo, type.TypeKind);
                        var typeSizeField = typeInfoType.GetField("size");
                        typeSizeField.SetValue(typeInfo, type.Size);

                        _typeInfoPointers[name] = typeInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeInfoType));
                        newTypeInfos.Add((type, typeInfo, typeInfoPointer));
                    }

                    var listPointer = IntPtr.Add(typeDataPointer, pointerSize * type.TypeIndex);
                    Marshal.StructureToPtr(typeInfoPointer, listPointer, false);
                }

                foreach (var (name, function) in programGraph.Functions)
                {
                    if (!_typeInfoPointers.TryGetValue(name, out var typeInfoPointer))
                    {
                        var typeInfo = Activator.CreateInstance(typeInfoType);

                        var typeNameField = typeInfoType.GetField("name");
                        typeNameField.SetValue(typeInfo, GetString(function.Name));
                        var typeKindField = typeInfoType.GetField("type");
                        typeKindField.SetValue(typeInfo, function.TypeKind);

                        _typeInfoPointers[name] = typeInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeInfoType));
                        newTypeInfos.Add((function, typeInfo, typeInfoPointer));
                    }

                    var listPointer = IntPtr.Add(typeDataPointer, pointerSize * function.TypeIndex);
                    Marshal.StructureToPtr(typeInfoPointer, listPointer, false);
                }

                // Set fields and enum values on TypeInfo objects
                if (newTypeInfos.Any())
                {
                    var typeFieldListType = _types["List.TypeField"];
                    var typeFieldType = _types["TypeField"];
                    var typeFieldSize = Marshal.SizeOf(typeFieldType);

                    var enumValueListType = _types["List.EnumValue"];
                    var enumValueType = _types["EnumValue"];
                    var enumValueSize = Marshal.SizeOf(enumValueType);

                    var argumentListType = _types["List.ArgumentType"];
                    var argumentType = _types["ArgumentType"];
                    var argumentSize = Marshal.SizeOf(argumentType);

                    foreach (var (type, typeInfo, typeInfoPointer) in newTypeInfos)
                    {
                        switch (type)
                        {
                            case StructAst structAst:
                                var typeFieldList = Activator.CreateInstance(typeFieldListType);
                                InitializeConstList(typeFieldList, typeFieldListType, typeFieldType, structAst.Fields.Count);

                                var typeFieldsField = typeInfoType.GetField("fields");
                                typeFieldsField.SetValue(typeInfo, typeFieldList);

                                var typeFieldListDataField = typeFieldListType.GetField("data");
                                var typeFieldsDataPointer = GetPointer(typeFieldListDataField.GetValue(typeFieldList));

                                for (var i = 0; i < structAst.Fields.Count; i++)
                                {
                                    var field = structAst.Fields[i];
                                    var typeField = Activator.CreateInstance(typeFieldType);

                                    var typeFieldName = typeFieldType.GetField("name");
                                    typeFieldName.SetValue(typeField, GetString(field.Name));
                                    var typeFieldInfo = typeFieldType.GetField("type_info");
                                    var typePointer = _typeInfoPointers[field.Type.GenericName];
                                    typeFieldInfo.SetValue(typeField, typePointer);

                                    var listPointer = IntPtr.Add(typeFieldsDataPointer, typeFieldSize * i);
                                    Marshal.StructureToPtr(typeField, listPointer, false);
                                }
                                break;
                            case EnumAst enumAst:
                                var enumValueList = Activator.CreateInstance(enumValueListType);
                                InitializeConstList(enumValueList, enumValueListType, enumValueType, enumAst.Values.Count);

                                var enumValuesField = typeInfoType.GetField("enum_values");
                                enumValuesField.SetValue(typeInfo, enumValueList);

                                var enumValuesListDataField = enumValueListType.GetField("data");
                                var enumValuesDataPointer = GetPointer(enumValuesListDataField.GetValue(enumValueList));

                                for (var i = 0; i < enumAst.Values.Count; i++)
                                {
                                    var value = enumAst.Values[i];
                                    var enumValue = Activator.CreateInstance(enumValueType);

                                    var enumValueName = enumValueType.GetField("name");
                                    enumValueName.SetValue(enumValue, GetString(value.Name));
                                    var enumValueValue = enumValueType.GetField("value");
                                    enumValueValue.SetValue(enumValue, value.Value);

                                    var listPointer = IntPtr.Add(enumValuesDataPointer, enumValueSize * i);
                                    Marshal.StructureToPtr(enumValue, listPointer, false);
                                }
                                break;
                            case FunctionAst function:
                                var returnTypeField = typeInfoType.GetField("return_type");
                                returnTypeField.SetValue(typeInfo, _typeInfoPointers[function.ReturnType.GenericName]);

                                var argumentList = Activator.CreateInstance(argumentListType);
                                var argumentCount = function.Varargs ? function.Arguments.Count - 1 : function.Arguments.Count;
                                InitializeConstList(argumentList, argumentListType, argumentType, argumentCount);

                                var argumentsField = typeInfoType.GetField("arguments");
                                argumentsField.SetValue(typeInfo, argumentList);

                                var argumentListDataField = argumentListType.GetField("data");
                                var argumentListDataPointer = GetPointer(argumentListDataField.GetValue(argumentList));

                                for (var i = 0; i < argumentCount; i++)
                                {
                                    var argument = function.Arguments[i];
                                    var argumentValue = Activator.CreateInstance(argumentType);

                                    var argumentName = argumentType.GetField("name");
                                    argumentName.SetValue(argumentValue, GetString(argument.Name));
                                    var argumentTypeField = argumentType.GetField("type_info");
                                    if (argument.Type.Name == "Type")
                                    {
                                        argumentTypeField.SetValue(argumentValue, _typeInfoPointers["s32"]);
                                    }
                                    else if (argument.Type.Name == "Params")
                                    {
                                        argumentTypeField.SetValue(argumentValue, _typeInfoPointers[$"List.{argument.Type.Generics[0].GenericName}"]);
                                    }
                                    else
                                    {
                                        argumentTypeField.SetValue(argumentValue, _typeInfoPointers[argument.Type.GenericName]);
                                    }

                                    var listPointer = IntPtr.Add(argumentListDataPointer, argumentSize * i);
                                    Marshal.StructureToPtr(argumentValue, listPointer, false);
                                }
                                break;
                        }
                        Marshal.StructureToPtr(typeInfo, typeInfoPointer, false);
                    }
                }

                _typeCount = programGraph.Types.Count;
            }
        }

        public void RunProgram(IAst ast)
        {
            ExecuteAst(ast, _globalVariables, out _);
        }

        private void CreateFunction(TypeBuilder typeBuilder, string name, string library, Type returnType, Type[] args)
        {
            var method = typeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, args);
            var caBuilder = new CustomAttributeBuilder(typeof(DllImportAttribute).GetConstructor(new []{typeof(string)}), new []{library});
            method.SetCustomAttribute(caBuilder);
        }

        private ValueType ExecuteAst(IAst ast, IDictionary<string, ValueType> variables, out bool returned)
        {
            returned = false;
            ValueType returnValue = null;
            switch (ast)
            {
                case ReturnAst returnAst:
                    returned = true;
                    return ExecuteReturn(returnAst, variables);
                case DeclarationAst declaration:
                    ExecuteDeclaration(declaration, variables);
                    break;
                case AssignmentAst assignment:
                    ExecuteAssignment(assignment, variables);
                    break;
                case ScopeAst scope:
                    returnValue = ExecuteScope(scope.Children, variables, out returned);
                    break;
                case ConditionalAst conditional:
                    returnValue = ExecuteConditional(conditional, variables, out returned);
                    break;
                case WhileAst whileAst:
                    returnValue = ExecuteWhile(whileAst, variables, out returned);
                    break;
                case EachAst each:
                    returnValue = ExecuteEach(each, variables, out returned);
                    break;
                default:
                    return ExecuteExpression(ast, variables);
            }

            return returnValue;
        }

        private ValueType ExecuteReturn(ReturnAst returnAst, IDictionary<string, ValueType> variables)
        {
            return ExecuteExpression(returnAst.Value, variables);
        }

        private void ExecuteDeclaration(DeclarationAst declaration, IDictionary<string, ValueType> variables)
        {
            var value = declaration.Value == null ?
                GetUninitializedValue(declaration.Type, variables, declaration.Assignments) :
                ExecuteExpression(declaration.Value, variables).Value;

            variables[declaration.Name] = new ValueType {Type = declaration.Type, Value = value};
        }

        private object GetUninitializedValue(TypeDefinition typeDef,
            IDictionary<string, ValueType> variables, List<AssignmentAst> values)
        {
            switch (typeDef.PrimitiveType)
            {
                case IntegerType integerType:
                    return 0;
                case FloatType floatType:
                    return floatType.Bytes == 4 ? 0f : 0.0;
                default:
                    switch (typeDef.Name)
                    {
                        case "List":
                            return InitializeList(typeDef, variables);
                        case "*":
                            return IntPtr.Zero;
                    }
                    var instanceType = _types[typeDef.GenericName];
                    var type = _programGraph.Types[typeDef.GenericName];
                    if (type is StructAst structAst)
                    {
                        return InitializeStruct(instanceType, structAst, variables, values);
                    }

                    return Activator.CreateInstance(instanceType);
            }
        }

        private object InitializeStruct(Type type, StructAst structAst,
            IDictionary<string, ValueType> variables, List<AssignmentAst> values = null)
        {
            var assignments = values?.ToDictionary(_ => (_.Reference as IdentifierAst)!.Name);
            var instance = Activator.CreateInstance(type);
            foreach (var field in structAst.Fields)
            {
                var fieldInstance = instance!.GetType().GetField(field.Name);

                if (assignments != null && assignments.TryGetValue(field.Name, out var assignment))
                {
                    var expression = ExecuteExpression(assignment.Value, variables);
                    var value = CastValue(expression.Value, field.Type);

                    fieldInstance!.SetValue(instance, value);
                }
                else if (field.DefaultValue != null)
                {
                    switch (field.DefaultValue)
                    {
                        case ConstantAst constant:
                            var constantValue = GetConstant(field.Type, constant.Value);
                            fieldInstance!.SetValue(instance, constantValue);
                            break;
                        case StructFieldRefAst structField:
                            var enumName = structField.TypeNames[0];
                            var enumDef = (EnumAst)_programGraph.Types[enumName];
                            var value = enumDef.Values[structField.ValueIndices[0]].Value;
                            var enumType = _types[enumName];
                            var enumInstance = Enum.ToObject(enumType, value);
                            fieldInstance!.SetValue(instance, enumInstance);
                            break;
                    }
                }
                else switch (field.Type.Name)
                {
                    case "List":
                        var list = InitializeList(field.Type, variables);
                        fieldInstance!.SetValue(instance, list);
                        break;
                    case "*":
                        break;
                    default:
                    {
                        if (field.Type.PrimitiveType == null)
                        {
                            var fieldType = _types[field.Type.GenericName];
                            var fieldTypeDef = _programGraph.Types[field.Type.GenericName];
                            if (fieldTypeDef is StructAst fieldStructAst)
                            {
                                var value = InitializeStruct(fieldType, fieldStructAst, variables);
                                fieldInstance!.SetValue(instance, value);
                            }
                            else
                            {
                                fieldInstance!.SetValue(instance, Activator.CreateInstance(fieldType));
                            }
                        }
                        break;
                    }
                }
            }

            return instance;
        }

        private object InitializeList(TypeDefinition type, IDictionary<string, ValueType> variables)
        {
            var listType = _types[type.GenericName];
            var genericType = GetTypeFromDefinition(type.Generics[0]);

            object list;
            if (type.CArray)
            {
                var length = 0;
                if (type.Count != null)
                {
                    length = (int)ExecuteExpression(type.Count, variables).Value;
                }
                list = Array.CreateInstance(genericType, length);
            }
            else
            {
                list = Activator.CreateInstance(listType);
                if (type.Count != null)
                {
                    var length = (int)ExecuteExpression(type.Count, variables).Value;
                    InitializeConstList(list, listType, genericType, length);
                }
                else
                {
                    var dataField = listType.GetField("data");
                    var array = Marshal.AllocHGlobal(Marshal.SizeOf(genericType) * 10);
                    dataField!.SetValue(list, array);
                }
            }

            return list;
        }

        private static void InitializeConstList(object list, Type listType, Type genericType, int length)
        {
            var countField = listType.GetField("length");
            countField!.SetValue(list, length);
            var dataField = listType.GetField("data");
            var array = Marshal.AllocHGlobal(Marshal.SizeOf(genericType) * length);
            dataField!.SetValue(list, array);
        }

        private void ExecuteAssignment(AssignmentAst assignment, IDictionary<string, ValueType> variables)
        {
            var expression = ExecuteExpression(assignment.Value, variables);
            if (assignment.Operator != Operator.None)
            {
                var lhs = ExecuteExpression(assignment.Reference, variables);
                expression.Value = RunExpression(lhs, expression, assignment.Operator, lhs.Type);
                expression.Type = lhs.Type;
            }

            switch (assignment.Reference)
            {
                case IdentifierAst identifier:
                {
                    var variable = variables[identifier.Name];
                    variable.Value = expression.Value;
                    break;
                }
                case StructFieldRefAst structField:
                {
                    var variable = ExecuteExpression(structField.Children[0], variables).Value;

                    for (var i = 1; i < structField.Children.Count; i++)
                    {
                        var structName = structField.TypeNames[i-1];
                        var structDefinition = (StructAst) _programGraph.Types[structName];

                        if (structField.Pointers[i-1])
                        {
                            var type = _types[structName];
                            variable = Marshal.PtrToStructure(GetPointer(variable), type);
                        }

                        switch (structField.Children[i])
                        {
                            case IdentifierAst identifier:
                                var field = variable!.GetType().GetField(identifier.Name);
                                if (i == structField.Children.Count - 1)
                                {
                                    field!.SetValue(variable, expression.Value);
                                }
                                else
                                {
                                    variable = field!.GetValue(variable);
                                }
                                break;
                            case IndexAst index:
                                var list = variable!.GetType().GetField(index.Name);
                                var listValue = list!.GetValue(variable);
                                var fieldType = structDefinition.Fields[structField.ValueIndices[i-1]].Type;
                                var (_, _, listPointer) = GetListPointer(index, variables, listValue, fieldType);
                                if (i == structField.Children.Count - 1)
                                {
                                    Marshal.StructureToPtr(expression.Value, listPointer, false);
                                }
                                else
                                {
                                    var elementType = fieldType.Generics[0];
                                    variable = PointerToTargetType(listPointer, elementType);
                                }
                                break;
                        }
                    }
                    break;
                }
                case IndexAst indexAst:
                    var (_, _, pointer) = GetListPointer(indexAst, variables);
                    Marshal.StructureToPtr(expression.Value, pointer, false);
                    break;
            }
        }

        private ValueType ExecuteScope(List<IAst> asts,
            IDictionary<string, ValueType> variables, out bool returned)
        {
            var scopeVariables = new Dictionary<string, ValueType>(variables);

            return ExecuteAsts(asts, scopeVariables, out returned);
        }

        private ValueType ExecuteConditional(ConditionalAst conditional,
            IDictionary<string, ValueType> variables, out bool returned)
        {
            if (ExecuteCondition(conditional.Condition, variables))
            {
                return ExecuteScope(conditional.Children, variables, out returned);
            }

            if (conditional.Else.Any())
            {
                return ExecuteScope(conditional.Else, variables, out returned);
            }

            returned = false;
            return null;
        }

        private ValueType ExecuteWhile(WhileAst whileAst,
            IDictionary<string, ValueType> variables, out bool returned)
        {
            while (ExecuteCondition(whileAst.Condition, variables))
            {
                var value = ExecuteScope(whileAst.Children, variables, out returned);

                if (returned)
                {
                    return value;
                }
            }

            returned = false;
            return null;
        }

        public bool ExecuteCondition(IAst expression)
        {
            return ExecuteCondition(expression, _globalVariables);
        }

        private bool ExecuteCondition(IAst expression, IDictionary<string, ValueType> variables)
        {
            var valueType = ExecuteExpression(expression, variables);
            var value = valueType.Value;
            return valueType.Type.PrimitiveType switch
            {
                IntegerType => (int)value != 0,
                FloatType => (float)value != 0f,
                _ when valueType.Type.Name == "*" => GetPointer(value) != IntPtr.Zero,
                _ => (bool)value
            };
        }

        private ValueType ExecuteEach(EachAst each, IDictionary<string, ValueType> variables, out bool returned)
        {
            var eachVariables = new Dictionary<string, ValueType>(variables);
            if (each.Iteration != null)
            {
                var iterator = ExecuteExpression(each.Iteration, variables);
                var lengthField = iterator.Value.GetType().GetField("length");
                var length = (int)lengthField!.GetValue(iterator.Value)!;

                var elementType = iterator.Type.Generics[0];
                var type = GetTypeFromDefinition(elementType);
                var iterationVariable = new ValueType {Type = elementType};
                eachVariables.Add(each.IterationVariable, iterationVariable);

                var dataField = iterator.Value.GetType().GetField("data");
                var data = dataField!.GetValue(iterator.Value);
                var dataPointer = GetPointer(data!);

                for (var i = 0; i < length; i++)
                {
                    var valuePointer = IntPtr.Add(dataPointer, Marshal.SizeOf(type) * i);
                    iterationVariable.Value = Marshal.PtrToStructure(valuePointer, type);

                    var value = ExecuteAsts(each.Children, eachVariables, out returned);

                    if (returned)
                    {
                        return value;
                    }
                }
            }
            else
            {
                var rangeBegin = ExecuteExpression(each.RangeBegin, variables);
                var rangeEnd = ExecuteExpression(each.RangeEnd, variables);
                var iterationVariable = new ValueType {Type = rangeBegin.Type, Value = rangeBegin.Value};
                eachVariables.Add(each.IterationVariable, iterationVariable);

                while ((bool)RunExpression(iterationVariable, rangeEnd, Operator.LessThanEqual, iterationVariable.Type))
                {
                    var value = ExecuteAsts(each.Children, eachVariables, out returned);

                    if (returned)
                    {
                        return value;
                    }

                    iterationVariable.Value = (int)iterationVariable.Value + 1;
                }
            }

            returned = false;
            return null;
        }

        private ValueType ExecuteAsts(List<IAst> asts,
            IDictionary<string, ValueType> variables, out bool returned)
        {
            foreach (var ast in asts)
            {
                var value = ExecuteAst(ast, variables, out returned);

                if (returned)
                {
                    return value;
                }
            }

            returned = false;
            return null;
        }

        private ValueType ExecuteExpression(IAst ast, IDictionary<string, ValueType> variables)
        {
            switch (ast)
            {
                case ConstantAst constant:
                    return new ValueType {Type = constant.Type, Value = GetConstant(constant.Type, constant.Value)};
                case NullAst nullAst:
                    return new ValueType {Type = nullAst.TargetType, Value = IntPtr.Zero};
                case StructFieldRefAst structField:
                    if (structField.IsEnum)
                    {
                        var enumName = structField.TypeNames[0];
                        var enumDef = (EnumAst)_programGraph.Types[enumName];
                        var value = enumDef.Values[structField.ValueIndices[0]].Value;
                        var enumType = _types[enumName];
                        var enumInstance = Enum.ToObject(enumType, value);
                        return new ValueType {Type = new TypeDefinition {Name = enumName}, Value = enumInstance};
                    }
                    var structVariable = ExecuteExpression(structField.Children[0], variables);
                    return GetStructFieldRef(structField, structVariable.Value, variables);
                case IdentifierAst identifier:
                    return variables[identifier.Name];
                case ChangeByOneAst changeByOne:
                    switch (changeByOne.Value)
                    {
                        case IdentifierAst identifier:
                        {
                            var variable = variables[identifier.Name];

                            var previousValue = new ValueType {Type = variable.Type, Value = variable.Value};
                            variable.Value = PerformOperation(variable.Type, variable.Value, changeByOne.Positive ? 1 : -1, Operator.Add);

                            return changeByOne.Prefix ? variable : previousValue;
                        }
                        case StructFieldRefAst structField:
                        {
                            var variable = ExecuteExpression(structField.Children[0], variables).Value;
                            TypeDefinition fieldType = null;
                            object newValue = null, previousValue = null;

                            for (var i = 1; i < structField.Children.Count; i++)
                            {
                                var structName = structField.TypeNames[i-1];
                                var structDefinition = (StructAst) _programGraph.Types[structName];
                                fieldType = structDefinition.Fields[structField.ValueIndices[i-1]].Type;

                                if (structField.Pointers[i-1])
                                {
                                    var type = _types[structName];
                                    variable = Marshal.PtrToStructure(GetPointer(variable), type);
                                }

                                switch (structField.Children[i])
                                {
                                    case IdentifierAst identifier:
                                        var field = variable!.GetType().GetField(identifier.Name);
                                        var fieldValue = field!.GetValue(variable);
                                        if (i == structField.Children.Count - 1)
                                        {
                                            previousValue = fieldValue;
                                            newValue = PerformOperation(fieldType, previousValue, changeByOne.Positive ? 1 : -1, Operator.Add);
                                            field.SetValue(variable, newValue);
                                        }
                                        else
                                        {
                                            variable = fieldValue;
                                        }
                                        break;
                                    case IndexAst index:
                                        var list = variable!.GetType().GetField(index.Name);
                                        var listValue = list!.GetValue(variable);
                                        var elementType = fieldType.Generics[0];
                                        var (_, _, listPointer) = GetListPointer(index, variables, listValue, fieldType);
                                        variable = PointerToTargetType(listPointer, elementType);
                                        if (i == structField.Children.Count - 1)
                                        {
                                            previousValue = variable;
                                            newValue = PerformOperation(elementType, previousValue, changeByOne.Positive ? 1 : -1, Operator.Add);
                                            Marshal.StructureToPtr(newValue, listPointer, false);
                                        }
                                        break;
                                }
                            }
                            return new ValueType {Type = fieldType, Value = changeByOne.Prefix ? newValue : previousValue};
                        }
                        case IndexAst indexAst:
                        {
                            var (typeDef, elementType, pointer) = GetListPointer(indexAst, variables);

                            var previousValue = Marshal.PtrToStructure(pointer, elementType);
                            var newValue = PerformOperation(typeDef, previousValue, changeByOne.Positive ? 1 : -1, Operator.Add);
                            Marshal.StructureToPtr(newValue, pointer, false);

                            return new ValueType {Type = typeDef, Value = changeByOne.Prefix ? newValue : previousValue};
                        }
                    }
                    break;
                case UnaryAst unary:
                {
                    if (unary.Operator == UnaryOperator.Reference && unary.Value is IndexAst indexAst)
                    {
                        var (typeDef, _, pointer) = GetListPointer(indexAst, variables);

                        var pointerType = new TypeDefinition {Name = "*"};
                        pointerType.Generics.Add(typeDef);

                        return new ValueType {Type = pointerType, Value = pointer};
                    }

                    var valueType = ExecuteExpression(unary.Value, variables);
                    switch (unary.Operator)
                    {
                        case UnaryOperator.Not:
                            var value = (bool)valueType.Value;
                            return new ValueType {Type = valueType.Type, Value = !value};
                        case UnaryOperator.Negate:
                            if (valueType.Type.PrimitiveType is IntegerType)
                            {
                                var intValue = PerformOperation(valueType.Type, valueType.Value, -1, Operator.Multiply);
                                return new ValueType {Type = valueType.Type, Value = intValue};
                            }
                            else
                            {
                                var floatValue = PerformOperation(valueType.Type, valueType.Value, -1.0, Operator.Multiply);
                                return new ValueType {Type = valueType.Type, Value = floatValue};
                            }
                        case UnaryOperator.Dereference:
                        {
                            var pointer = GetPointer(valueType.Value);
                            var pointerType = valueType.Type.Generics[0];
                            var pointerValue = PointerToTargetType(pointer, pointerType);

                            return new ValueType {Type = pointerType, Value = pointerValue};
                        }
                        case UnaryOperator.Reference:
                        {
                            var pointerType = new TypeDefinition {Name = "*"};
                            pointerType.Generics.Add(valueType.Type);
                            var type = GetTypeFromDefinition(valueType.Type);

                            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(type));
                            Marshal.StructureToPtr(valueType.Value, pointer, false);

                            return new ValueType {Type = pointerType, Value = pointer};
                        }
                    }
                    break;
                }
                case CallAst call:
                    var function = _programGraph.Functions[call.Function];
                    if (call.Params)
                    {
                        var arguments = new object[function.Arguments.Count];
                        for (var i = 0; i < function.Arguments.Count - 1; i++)
                        {
                            var value = ExecuteExpression(call.Arguments[i], variables).Value;
                            arguments[i] = value;
                        }

                        var elementType = function.Arguments[^1].Type.Generics[0];
                        var paramsType = GetTypeFromDefinition(elementType);
                        var listType = _types[$"List.{elementType.GenericName}"];
                        var paramsList = Activator.CreateInstance(listType);
                        InitializeConstList(paramsList, listType, paramsType, call.Arguments.Count - function.Arguments.Count + 1);

                        var dataField = listType.GetField("data");
                        var data = dataField!.GetValue(paramsList);
                        var dataPointer = GetPointer(data!);

                        var paramsIndex = 0;
                        for (var i = function.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                        {
                            var value = ExecuteExpression(call.Arguments[i], variables).Value;

                            var valuePointer = IntPtr.Add(dataPointer, Marshal.SizeOf(paramsType) * paramsIndex);
                            Marshal.StructureToPtr(value, valuePointer, false);
                        }

                        arguments[function.Arguments.Count - 1] = paramsList;

                        return CallFunction(call.Function, function, arguments);
                    }
                    else if (function.Varargs)
                    {
                        var arguments = new object[call.Arguments.Count];
                        var types = new Type[call.Arguments.Count];
                        for (var i = 0; i < function.Arguments.Count - 1; i++)
                        {
                            var valueType = ExecuteExpression(call.Arguments[i], variables);
                            arguments[i] = valueType.Value;
                            types[i] = GetTypeFromDefinition(valueType.Type, true);
                        }

                        // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                        // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                        for (var i = function.Arguments.Count - 1; i < call.Arguments.Count; i++)
                        {
                            var valueType = ExecuteExpression(call.Arguments[i], variables);
                            if (valueType.Type.Name == "float")
                            {
                                arguments[i] = Convert.ToDouble(valueType.Value);
                                types[i] = typeof(double);
                            }
                            else
                            {
                                arguments[i] = valueType.Value;
                                types[i] = GetTypeFromDefinition(valueType.Type, true);
                            }
                        }

                        return CallFunction(call.Function, function, arguments, types, call.VarargsIndex);
                    }
                    else
                    {
                        var arguments = new object[call.Arguments.Count];
                        var types = new Type[call.Arguments.Count];
                        for (var i = 0; i < call.Arguments.Count; i++)
                        {
                            var argument = call.Arguments[i];
                            var valueType = ExecuteExpression(argument, variables);
                            arguments[i] = valueType.Value;
                            types[i] = GetTypeFromDefinition(valueType.Type, function.Extern);
                        }

                        return CallFunction(call.Function, function, arguments, types);
                    }
                case ExpressionAst expression:
                    var firstValue = ExecuteExpression(expression.Children[0], variables);
                    var expressionValue = new ValueType {Type = firstValue.Type, Value = firstValue.Value};
                    for (var i = 1; i < expression.Children.Count; i++)
                    {
                        var rhs = ExecuteExpression(expression.Children[i], variables);
                        var nextType = expression.ResultingTypes[i - 1];
                        expressionValue.Value = RunExpression(expressionValue, rhs, expression.Operators[i - 1], nextType);
                        expressionValue.Type = nextType;
                    }
                    return expressionValue;
                case IndexAst indexAst:
                {
                    var (typeDef, elementType, pointer) = GetListPointer(indexAst, variables);
                    return new ValueType {Type = typeDef, Value = PointerToTargetType(pointer, typeDef, elementType)};
                }
                case CastAst cast:
                {
                    var value = ExecuteExpression(cast.Value, variables);
                    return new ValueType {Type = cast.TargetType, Value = CastValue(value.Value, cast.TargetType)};
                }
            }

            return null;
        }

        private object GetConstant(TypeDefinition type, string value)
        {
            switch (type.PrimitiveType)
            {
                case IntegerType integerType:
                    return integerType.Bytes switch
                    {
                        1 => integerType.Signed ? sbyte.Parse(value) : byte.Parse(value),
                        2 => integerType.Signed ? short.Parse(value) : ushort.Parse(value),
                        4 => integerType.Signed ? int.Parse(value) : uint.Parse(value),
                        8 => integerType.Signed ? long.Parse(value) : ulong.Parse(value),
                        _ => integerType.Signed ? int.Parse(value) : uint.Parse(value)
                    };
                case FloatType floatType:
                    if (floatType.Bytes == 4) return float.Parse(value);
                    return double.Parse(value);
                default:
                    if (type.Name == "bool")
                    {
                        return value == "true";
                    }

                    return GetString(value);
            }
        }

        private object GetString(string value)
        {
            var stringType = _types["string"];
            var stringInstance = Activator.CreateInstance(stringType);
            var lengthField = stringType.GetField("length");
            lengthField!.SetValue(stringInstance, value.Length);

            var dataField = stringType.GetField("data");
            var stringPointer = Marshal.StringToHGlobalAnsi(value);
            dataField!.SetValue(stringInstance, stringPointer);

            return stringInstance;
        }

        private ValueType GetStructFieldRef(StructFieldRefAst structField, object structVariable, IDictionary<string, ValueType> variables)
        {
            TypeDefinition fieldType = null;

            for (var i = 1; i < structField.Children.Count; i++)
            {
                var structName = structField.TypeNames[i-1];
                var structDefinition = (StructAst) _programGraph.Types[structName];
                fieldType = structDefinition.Fields[structField.ValueIndices[i-1]].Type;

                if (structField.Pointers[i-1])
                {
                    // TODO Figure this out
                    var type = _types[structName];
                    structVariable = Marshal.PtrToStructure(GetPointer(structVariable), type);
                }

                switch (structField.Children[i])
                {
                    case IdentifierAst identifier:
                        var field = structVariable!.GetType().GetField(identifier.Name);
                        var fieldValue = field!.GetValue(structVariable);
                        structVariable = fieldValue;
                        break;
                    case IndexAst index:
                        var list = structVariable!.GetType().GetField(index.Name);
                        var listValue = list!.GetValue(structVariable);
                        var (_, _, listPointer) = GetListPointer(index, variables, listValue, fieldType);
                        fieldType = fieldType.Generics[0];
                        structVariable = PointerToTargetType(listPointer, fieldType);
                        break;
                }
            }

            return new ValueType {Type = fieldType, Value = structVariable};
        }

        private (TypeDefinition typeDef, Type elementType, IntPtr pointer) GetListPointer(IndexAst indexAst, IDictionary<string, ValueType> variables, object listObject = null, TypeDefinition listTypeDef = null)
        {
            var index = (int)ExecuteExpression(indexAst.Index, variables).Value;

            TypeDefinition elementTypeDef;
            if (listObject == null)
            {
                var variable = variables[indexAst.Name];
                listObject = variable.Value;
                listTypeDef ??= variable.Type;
                elementTypeDef = variable.Type.Generics[0];
            }
            else
            {
                elementTypeDef = listTypeDef.Generics[0];
            }
            var elementType = GetTypeFromDefinition(elementTypeDef);

            IntPtr dataPointer;
            if (listTypeDef.CArray)
            {
                // TODO Implement me
                dataPointer = IntPtr.Zero;
            }
            else
            {
                var dataField = listObject.GetType().GetField("data");
                var data = dataField!.GetValue(listObject);
                dataPointer = GetPointer(data!);
            }

            if (index == 0)
            {
                return (elementTypeDef, elementType, dataPointer);
            }

            var valuePointer = IntPtr.Add(dataPointer, Marshal.SizeOf(elementType) * index);

            return (elementTypeDef, elementType, valuePointer);
        }

        private static IntPtr GetPointer(object value)
        {
            if (value is Pointer)
            {
                unsafe
                {
                    return (IntPtr)Pointer.Unbox(value);
                }
            }
            return (IntPtr)value;
        }

        private object PointerToTargetType(IntPtr pointer, TypeDefinition targetType, Type type = null)
        {
            if (targetType.Name == "*")
            {
                return Marshal.ReadIntPtr(pointer);
            }

            type ??= GetTypeFromDefinition(targetType);
            return Marshal.PtrToStructure(pointer, type);
        }

        private ValueType CallFunction(string functionName, FunctionAst function, object[] arguments, Type[] argumentTypes = null, int callIndex = 0)
        {
            if (function.Extern)
            {
                var args = arguments.Select(GetCArg).ToArray();
                if (function.Varargs)
                {
                    var functionIndex = _functionIndices[functionName][callIndex];
                    var (type, functionObject) = _functionLibraries[functionIndex];
                    var functionDecl = type.GetMethod(functionName, argumentTypes!);
                    var returnValue = functionDecl.Invoke(functionObject, args);
                    return new ValueType {Type = function.ReturnType, Value = returnValue};
                }
                else
                {
                    var functionIndex = _functionIndices[functionName][callIndex];
                    var (type, functionObject) = _functionLibraries[functionIndex];
                    var functionDecl = type.GetMethod(functionName);
                    var returnValue = functionDecl.Invoke(functionObject, args);
                    return new ValueType {Type = function.ReturnType, Value = returnValue};
                }
            }

            if (function.Compiler)
            {
                if (!_compilerFunctions.TryGetValue(function.Name, out var name))
                {
                    _programGraph.Errors.Add(new Translation.TranslationError
                    {
                        Error = $"Undefined compiler function '{function.Name}'",
                        FileIndex = function.FileIndex,
                        Line = function.Line,
                        Column = function.Column
                    });
                    return null;
                }
                var args = arguments.Select(GetManagedArg).ToArray();

                var functionDecl = typeof(ProgramRunner).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
                var returnValue = functionDecl.Invoke(this, args);
                return new ValueType {Type = function.ReturnType, Value = returnValue};
            }

            var variables = new Dictionary<string, ValueType>(_globalVariables);

            for (var i = 0; i < function.Arguments.Count; i++)
            {
                var arg = function.Arguments[i];
                variables[arg.Name] = new ValueType {Type = arg.Type, Value = arguments[i]};
            }

            foreach (var ast in function.Children)
            {
                var value = ExecuteAst(ast, variables, out var returned);

                if (returned)
                {
                    return value;
                }
            }
            return null;
        }

        private static object GetCArg(object argument)
        {
            var type = argument.GetType();
            if (type.Name == "string")
            {
                var dataField = type.GetField("data");
                return GetPointer(dataField!.GetValue(argument));
            }

            return argument;
        }

        private static object GetManagedArg(object argument)
        {
            var type = argument.GetType();
            if (type.Name == "string")
            {
                var dataField = type.GetField("data");
                var pointer = GetPointer(dataField!.GetValue(argument));
                return Marshal.PtrToStringAnsi(pointer);
            }

            return argument;
        }

        private void AddDependency(string library)
        {
            _programGraph.Dependencies.Add(library);
        }

        private static object RunExpression(ValueType lhs, ValueType rhs, Operator op, TypeDefinition targetType)
        {
            // 1. Handle pointer math
            if (lhs.Type.Name == "*")
            {
                return PointerOperation(lhs.Value, rhs.Value, op);
            }
            if (rhs.Type.Name == "*")
            {
                return PointerOperation(rhs.Value, lhs.Value, op);
            }

            // 2. Handle compares, since the lhs and rhs should not be cast to the target type
            switch (op)
            {
                case Operator.And:
                case Operator.Or:
                    var lhsBool = (bool)lhs.Value;
                    var rhsBool = (bool)rhs.Value;
                    return op == Operator.And ? lhsBool && rhsBool : lhsBool || rhsBool;
                case Operator.Equality:
                case Operator.NotEqual:
                case Operator.GreaterThanEqual:
                case Operator.LessThanEqual:
                case Operator.GreaterThan:
                case Operator.LessThan:
                    return Compare(lhs, rhs, op);
            }

            // 3. Cast lhs and rhs to the target types
            var lhsValue = CastValue(lhs.Value, targetType);
            var rhsValue = CastValue(rhs.Value, targetType);

            // 4. Handle the rest of the simple operators
            switch (op)
            {
                case Operator.BitwiseAnd:
                case Operator.BitwiseOr:
                case Operator.Xor:
                    if (targetType.Name == "bool")
                    {
                        var lhsBool = Convert.ToBoolean(lhsValue);
                        var rhsBool = Convert.ToBoolean(rhsValue);
                        switch (op)
                        {
                            case Operator.BitwiseAnd:
                                return lhsBool & rhsBool;
                            case Operator.BitwiseOr:
                                return lhsBool | rhsBool;
                            case Operator.Xor:
                                return lhsBool ^ rhsBool;
                        }
                    }

                    if (targetType.PrimitiveType is IntegerType integerType)
                    {
                        switch (integerType.Bytes)
                        {
                            case 1:
                                if (integerType.Signed)
                                {
                                    var lhsByte = Convert.ToSByte(lhsValue);
                                    var rhsByte = Convert.ToSByte(rhsValue);
                                    switch (op)
                                    {
                                        case Operator.BitwiseAnd:
                                            return lhsByte & rhsByte;
                                        case Operator.BitwiseOr:
                                            return lhsByte | rhsByte;
                                        case Operator.Xor:
                                            return lhsByte ^ rhsByte;
                                    }
                                }
                                else
                                {
                                    var lhsByte = Convert.ToByte(lhsValue);
                                    var rhsByte = Convert.ToByte(rhsValue);
                                    switch (op)
                                    {
                                        case Operator.BitwiseAnd:
                                            return lhsByte & rhsByte;
                                        case Operator.BitwiseOr:
                                            return lhsByte | rhsByte;
                                        case Operator.Xor:
                                            return lhsByte ^ rhsByte;
                                    }
                                }
                                break;
                            case 2:
                                if (integerType.Signed)
                                {
                                    var lhsShort = Convert.ToInt16(lhsValue);
                                    var rhsShort = Convert.ToInt16(rhsValue);
                                    switch (op)
                                    {
                                        case Operator.BitwiseAnd:
                                            return lhsShort & rhsShort;
                                        case Operator.BitwiseOr:
                                            return lhsShort | rhsShort;
                                        case Operator.Xor:
                                            return lhsShort ^ rhsShort;
                                    }
                                }
                                else
                                {
                                    var lhsShort = Convert.ToUInt16(lhsValue);
                                    var rhsShort = Convert.ToUInt16(rhsValue);
                                    switch (op)
                                    {
                                        case Operator.BitwiseAnd:
                                            return lhsShort & rhsShort;
                                        case Operator.BitwiseOr:
                                            return lhsShort | rhsShort;
                                        case Operator.Xor:
                                            return lhsShort ^ rhsShort;
                                    }
                                }
                                break;
                            case 4:
                                if (integerType.Signed)
                                {
                                    var lhsInt = Convert.ToInt32(lhsValue);
                                    var rhsInt = Convert.ToInt32(rhsValue);
                                    switch (op)
                                    {
                                        case Operator.BitwiseAnd:
                                            return lhsInt & rhsInt;
                                        case Operator.BitwiseOr:
                                            return lhsInt | rhsInt;
                                        case Operator.Xor:
                                            return lhsInt ^ rhsInt;
                                    }
                                }
                                else
                                {
                                    var lhsInt = Convert.ToUInt32(lhsValue);
                                    var rhsInt = Convert.ToUInt32(rhsValue);
                                    switch (op)
                                    {
                                        case Operator.BitwiseAnd:
                                            return lhsInt & rhsInt;
                                        case Operator.BitwiseOr:
                                            return lhsInt | rhsInt;
                                        case Operator.Xor:
                                            return lhsInt ^ rhsInt;
                                    }
                                }
                                break;
                            case 8:
                                if (integerType.Signed)
                                {
                                    var lhsLong = Convert.ToInt64(lhsValue);
                                    var rhsLong = Convert.ToInt64(rhsValue);
                                    switch (op)
                                    {
                                        case Operator.BitwiseAnd:
                                            return lhsLong & rhsLong;
                                        case Operator.BitwiseOr:
                                            return lhsLong | rhsLong;
                                        case Operator.Xor:
                                            return lhsLong ^ rhsLong;
                                    }
                                }
                                else
                                {
                                    var lhsLong = Convert.ToUInt64(lhsValue);
                                    var rhsLong = Convert.ToUInt64(rhsValue);
                                    switch (op)
                                    {
                                        case Operator.BitwiseAnd:
                                            return lhsLong & rhsLong;
                                        case Operator.BitwiseOr:
                                            return lhsLong | rhsLong;
                                        case Operator.Xor:
                                            return lhsLong ^ rhsLong;
                                    }
                                }
                                break;
                        }
                    }
                    break;
            }

            // 5. Handle binary operations
            return PerformOperation(targetType, lhsValue, rhsValue, op);
        }

        private static object PointerOperation(object lhs, object rhs, Operator op)
        {
            var lhsPointer = GetPointer(lhs);
            if (op == Operator.Equality)
            {
                if (rhs == null)
                {
                    return lhsPointer == IntPtr.Zero;
                }
                return lhsPointer == GetPointer(rhs);
            }
            if (op == Operator.NotEqual)
            {
                if (rhs == null)
                {
                    return lhsPointer != IntPtr.Zero;
                }
                return lhsPointer == GetPointer(rhs);
            }
            if (op == Operator.Subtract)
            {
                return IntPtr.Subtract(lhsPointer, (int)rhs);
            }
            return IntPtr.Add(lhsPointer, (int)rhs);
        }

        private static object Compare(ValueType lhs, ValueType rhs, Operator op)
        {
            switch (lhs.Type.PrimitiveType)
            {
                case IntegerType lhsInteger:
                    switch (rhs.Type.PrimitiveType)
                    {
                        case IntegerType rhsInteger:
                        {
                            if (lhsInteger.Signed || rhsInteger.Signed)
                            {
                                var lhsValue = Convert.ToInt64(lhs.Value);
                                var rhsValue = Convert.ToInt64(rhs.Value);
                                return IntegerOperations(lhsValue, rhsValue, op);
                            }
                            else
                            {
                                var lhsValue = Convert.ToUInt64(lhs.Value);
                                var rhsValue = Convert.ToUInt64(rhs.Value);
                                return UnsignedIntegerOperations(lhsValue, rhsValue, op);
                            }
                        }
                        case FloatType floatType:
                        {
                            if (floatType.Bytes == 4)
                            {
                                var lhsFloat = Convert.ToSingle(lhs.Value);
                                var rhsFloat = Convert.ToSingle(rhs.Value);
                                return FloatOperations(lhsFloat, rhsFloat, op);
                            }
                            else
                            {
                                var lhsFloat = Convert.ToDouble(lhs.Value);
                                var rhsFloat = Convert.ToDouble(rhs.Value);
                                return DoubleOperations(lhsFloat, rhsFloat, op);
                            }
                        }
                    }
                    break;
                case FloatType lhsFloatType:
                    switch (rhs.Type.PrimitiveType)
                    {
                        case IntegerType:
                        {
                            if (lhsFloatType.Bytes == 4)
                            {
                                var lhsFloat = Convert.ToSingle(lhs.Value);
                                var rhsFloat = Convert.ToSingle(rhs.Value);
                                return FloatOperations(lhsFloat, rhsFloat, op);
                            }
                            else
                            {
                                var lhsFloat = Convert.ToDouble(lhs.Value);
                                var rhsFloat = Convert.ToDouble(rhs.Value);
                                return DoubleOperations(lhsFloat, rhsFloat, op);
                            }
                        }
                        case FloatType rhsFloatType:
                        {
                            if (lhsFloatType.Bytes == 4 && rhsFloatType.Bytes == 4)
                            {
                                var lhsFloat = Convert.ToSingle(lhs.Value);
                                var rhsFloat = Convert.ToSingle(rhs.Value);
                                return FloatOperations(lhsFloat, rhsFloat, op);
                            }
                            else
                            {
                                var lhsFloat = Convert.ToDouble(lhs.Value);
                                var rhsFloat = Convert.ToDouble(rhs.Value);
                                return DoubleOperations(lhsFloat, rhsFloat, op);
                            }
                        }
                    }
                    break;
                case EnumType:
                {
                    var lhsValue = Convert.ToInt64(lhs.Value);
                    var rhsValue = Convert.ToInt64(rhs.Value);
                    return IntegerOperations(lhsValue, rhsValue, op);
                }
            }

            // @Future Operator overloading
            throw new NotImplementedException($"{op} not compatible with types '{lhs.Type.GenericName}' and '{rhs.Type.GenericName}'");
        }

        private static object PerformOperation(TypeDefinition targetType, object lhsValue, object rhsValue, Operator op)
        {
            switch (targetType.PrimitiveType)
            {
                case IntegerType integerType:
                    if (integerType.Signed)
                    {
                        var lhsInt = Convert.ToInt64(lhsValue);
                        var rhsInt = Convert.ToInt64(rhsValue);
                        var result = IntegerOperations(lhsInt, rhsInt, op);
                        return CastValue(result, targetType);
                    }
                    else
                    {
                        var lhsInt = Convert.ToUInt64(lhsValue);
                        var rhsInt = Convert.ToUInt64(rhsValue);
                        var result = UnsignedIntegerOperations(lhsInt, rhsInt, op);
                        return CastValue(result, targetType);
                    }
                case FloatType floatType:
                    if (floatType.Bytes == 4)
                    {
                        var lhsFloat = Convert.ToSingle(lhsValue);
                        var rhsFloat = Convert.ToSingle(rhsValue);
                        var result = FloatOperations(lhsFloat, rhsFloat, op);
                        return CastValue(result, targetType);
                    }
                    else
                    {
                        var lhsFloat = Convert.ToDouble(lhsValue);
                        var rhsFloat = Convert.ToDouble(rhsValue);
                        var result = DoubleOperations(lhsFloat, rhsFloat, op);
                        return CastValue(result, targetType);
                    }
            }

            // @Future Operator overloading
            throw new NotImplementedException($"{op} not compatible with types '{lhsValue.GetType()}' and '{rhsValue.GetType()}'");
        }

        private static object CastValue(object value, TypeDefinition targetType)
        {
            switch (targetType.PrimitiveType)
            {
                case IntegerType integerType:
                    try
                    {
                        return integerType.Bytes switch
                        {
                            1 => integerType.Signed ? Convert.ToSByte(value) : Convert.ToByte(value),
                            2 => integerType.Signed ? Convert.ToInt16(value) : Convert.ToUInt16(value),
                            4 => integerType.Signed ? Convert.ToInt32(value) : Convert.ToUInt32(value),
                            8 => integerType.Signed ? Convert.ToInt64(value) : Convert.ToUInt64(value),
                            _ => integerType.Signed ? Convert.ToInt32(value) : Convert.ToUInt32(value)
                        };
                    }
                    catch (OverflowException)
                    {
                        var bytes = (byte[]) typeof(BitConverter).GetMethod("GetBytes", new [] {value.GetType()}).Invoke(null, new [] {value});

                        return integerType.Bytes switch
                        {
                            1 => integerType.Signed ? (sbyte)bytes[0] : bytes[0],
                            2 => integerType.Signed ? BitConverter.ToInt16(bytes) : (ushort)BitConverter.ToInt16(bytes),
                            4 => integerType.Signed ? BitConverter.ToInt32(bytes) : (uint)BitConverter.ToInt32(bytes),
                            8 => integerType.Signed ? BitConverter.ToInt64(bytes) : (ulong)BitConverter.ToInt64(bytes),
                            _ => integerType.Signed ? BitConverter.ToInt32(bytes) : (uint)BitConverter.ToInt32(bytes),
                        };
                    }
                case FloatType floatType:
                    if (floatType.Bytes == 4) return Convert.ToSingle(value);
                    return Convert.ToDouble(value);
            }

            // @Future Polymorphic type casting
            return value;
        }

        private static object IntegerOperations(long lhs, long rhs, Operator op)
        {
            return op switch
            {
                Operator.Equality => lhs == rhs,
                Operator.NotEqual => lhs != rhs,
                Operator.GreaterThan => lhs > rhs,
                Operator.GreaterThanEqual => lhs >= rhs,
                Operator.LessThan => lhs < rhs,
                Operator.LessThanEqual => lhs <= rhs,
                Operator.Add => lhs + rhs,
                Operator.Subtract => lhs - rhs,
                Operator.Multiply => lhs * rhs,
                Operator.Divide => lhs / rhs,
                Operator.Modulus => lhs % rhs,
                // @Cleanup This branch should never be hit
                _ => null
            };
        }

        private static object UnsignedIntegerOperations(ulong lhs, ulong rhs, Operator op)
        {
            return op switch
            {
                Operator.Equality => lhs == rhs,
                Operator.NotEqual => lhs != rhs,
                Operator.GreaterThan => lhs > rhs,
                Operator.GreaterThanEqual => lhs >= rhs,
                Operator.LessThan => lhs < rhs,
                Operator.LessThanEqual => lhs <= rhs,
                Operator.Add => lhs + rhs,
                Operator.Subtract => lhs - rhs,
                Operator.Multiply => lhs * rhs,
                Operator.Divide => lhs / rhs,
                Operator.Modulus => lhs % rhs,
                // @Cleanup This branch should never be hit
                _ => null
            };
        }

        private static object FloatOperations(float lhs, float rhs, Operator op)
        {
            return op switch
            {
                Operator.Equality => Math.Abs(lhs - rhs) < float.MinValue,
                Operator.NotEqual => Math.Abs(lhs - rhs) > float.MinValue,
                Operator.GreaterThan => lhs > rhs,
                Operator.GreaterThanEqual => lhs >= rhs,
                Operator.LessThan => lhs < rhs,
                Operator.LessThanEqual => lhs <= rhs,
                Operator.Add => lhs + rhs,
                Operator.Subtract => lhs - rhs,
                Operator.Multiply => lhs * rhs,
                Operator.Divide => lhs / rhs,
                Operator.Modulus => lhs % rhs,
                // @Cleanup This branch should never be hit
                _ => null
            };
        }

        private static object DoubleOperations(double lhs, double rhs, Operator op)
        {
            return op switch
            {
                Operator.Equality => Math.Abs(lhs - rhs) < double.Epsilon,
                Operator.NotEqual => Math.Abs(lhs - rhs) > double.Epsilon,
                Operator.GreaterThan => lhs > rhs,
                Operator.GreaterThanEqual => lhs >= rhs,
                Operator.LessThan => lhs < rhs,
                Operator.LessThanEqual => lhs <= rhs,
                Operator.Add => lhs + rhs,
                Operator.Subtract => lhs - rhs,
                Operator.Multiply => lhs * rhs,
                Operator.Divide => lhs / rhs,
                Operator.Modulus => lhs % rhs,
                // @Cleanup This branch should never be hit
                _ => null
            };
        }

        private Type GetTypeFromDefinition(TypeDefinition typeDef, IDictionary<string, TypeBuilder> temporaryTypes)
        {
            switch (typeDef.PrimitiveType)
            {
                case IntegerType integerType:
                    return GetIntegerType(integerType);
                case FloatType floatType:
                    return floatType.Bytes == 4 ? typeof(float) : typeof(double);
            }

            switch (typeDef.Name)
            {
                case "bool":
                    return typeof(bool);
                case "*":
                    var pointerType = GetTypeFromDefinition(typeDef.Generics[0], temporaryTypes);
                    if (pointerType == null)
                    {
                        return null;
                    }
                    return pointerType.MakePointerType();
                case "List" when typeDef.CArray:
                    var elementType = GetTypeFromDefinition(typeDef.Generics[0], temporaryTypes);
                    return elementType.MakeArrayType();
                    // TODO Implement me and make sure this works
            }

            if (_types.TryGetValue(typeDef.GenericName, out var type))
            {
                return type;
            }

            return temporaryTypes.TryGetValue(typeDef.GenericName, out var tempType) ? tempType : null;
        }

        private Type GetTypeFromDefinition(TypeDefinition typeDef, bool cCall = false)
        {
            switch (typeDef.PrimitiveType)
            {
                case IntegerType integerType:
                    return GetIntegerType(integerType);
                case FloatType floatType:
                    return floatType.Bytes == 4 ? typeof(float) : typeof(double);
            }

            switch (typeDef.Name)
            {
                case "bool":
                    return typeof(bool);
                case "string":
                    if (!cCall) break;
                    return typeof(char).MakePointerType();
                case "*":
                    var pointerType = GetTypeFromDefinition(typeDef.Generics[0], cCall);
                    if (pointerType == null)
                    {
                        return null;
                    }
                    return pointerType.MakePointerType();
            }

            return _types.TryGetValue(typeDef.GenericName, out var type) ? type : null;
        }

        private Type GetIntegerType(IntegerType integerType)
        {
            if (integerType.Signed)
            {
                return integerType.Bytes switch
                {
                    1 => typeof(sbyte),
                    2 => typeof(short),
                    4 => typeof(int),
                    8 => typeof(long),
                    _ => typeof(int)
                };
            }
            return integerType.Bytes switch
            {
                1 => typeof(byte),
                2 => typeof(ushort),
                4 => typeof(uint),
                8 => typeof(ulong),
                _ => typeof(uint)
            };
        }
    }
}
