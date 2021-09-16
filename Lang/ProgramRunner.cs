using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Lang
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
        private IntPtr _typeDataPointer;

        private readonly Dictionary<string, List<int>> _functionIndices = new();
        private readonly List<(Type type, object libraryObject)> _functionLibraries = new();
        private readonly Dictionary<string, ValueType> _globalVariables = new();
        private readonly Dictionary<string, Type> _types = new();
        private readonly Dictionary<string, IntPtr> _typeInfoPointers = new();
        private readonly TypeDefinition _s32Type = new() {Name = "s32", TypeKind = TypeKind.Integer, PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};

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
            foreach (var (name, type) in TypeTable.Types)
            {
                if (type is StructAst structAst && !_types.ContainsKey(name))
                {
                    var structBuilder = _moduleBuilder.DefineType(name, TypeAttributes.Public | TypeAttributes.SequentialLayout);
                    temporaryStructs[name] = structBuilder;
                }
            }

            var fieldIndices = new Dictionary<string, int>();
            while (temporaryStructs.Any())
            {
                foreach (var (name, structBuilder) in temporaryStructs)
                {
                    var indexFound = fieldIndices.TryGetValue(name, out var index);
                    var structAst = TypeTable.Types[name] as StructAst;
                    var count = structAst!.Fields.Count;

                    for (; index < count; index++)
                    {
                        var field = structAst.Fields[index];
                        var fieldType = GetTypeFromDefinition(field.TypeDefinition, temporaryStructs);
                        if (fieldType == null)
                        {
                            break;
                        }
                        var structField = structBuilder.DefineField(field.Name, fieldType, FieldAttributes.Public);
                        if (field.TypeDefinition.TypeKind == TypeKind.CArray)
                        {
                            var marshalAsType = typeof(MarshalAsAttribute);
                            var sizeConstField = marshalAsType.GetField("SizeConst");
                            var size = (int)field.TypeDefinition.ConstCount.Value;

                            var caBuilder = new CustomAttributeBuilder(typeof(MarshalAsAttribute).GetConstructor(new []{typeof(UnmanagedType)}), new object[]{UnmanagedType.ByValArray}, new []{sizeConstField}, new object[]{size});
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
            foreach (var functions in TypeTable.Functions.Values)
            {
                foreach (var function in functions.Where(_ => _.Extern))
                {
                    var returnType = GetTypeFromDefinition(function.ReturnTypeDefinition);

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
                            var args = function.Arguments.Select(arg => GetTypeFromDefinition(arg.TypeDefinition, cCall: true)).ToArray();
                            CreateFunction(functionTypeBuilder, function.Name, function.ExternLib, returnType, args);
                            functionIndex.Add(_version);
                        }
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

            if (_typeCount != TypeTable.Count)
            {
                _typeCount = TypeTable.Count;

                // Free old data
                var typeTableVariable = _globalVariables["__type_table"];
                var oldPointer = GetPointer(typeTableVariable.Value);
                if (_typeDataPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_typeDataPointer);
                }
                Marshal.FreeHGlobal(oldPointer);

                // Get required types and allocate the array
                var typeInfoArrayType = _types["Array.*.TypeInfo"];
                var typeInfoType = _types["TypeInfo"];
                var typeInfoSize = Marshal.SizeOf(typeInfoType);

                const int pointerSize = 8;
                var typeTable = Activator.CreateInstance(typeInfoArrayType);
                _typeDataPointer = InitializeConstArray(typeTable, typeInfoArrayType, pointerSize, _typeCount);

                // Create TypeInfo pointers
                var newTypeInfos = new List<(IType type, object typeInfo, IntPtr typeInfoPointer)>();
                foreach (var (name, type) in TypeTable.Types)
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

                        _typeInfoPointers[name] = typeInfoPointer = Marshal.AllocHGlobal(typeInfoSize);
                        newTypeInfos.Add((type, typeInfo, typeInfoPointer));
                    }

                    var arrayPointer = IntPtr.Add(_typeDataPointer, type.TypeIndex * pointerSize);
                    Marshal.StructureToPtr(typeInfoPointer, arrayPointer, false);
                }

                foreach (var (name, functions) in TypeTable.Functions)
                {
                    for (var i = 0; i < functions.Count; i++)
                    {
                        var function = functions[i];
                        if (!_typeInfoPointers.TryGetValue($"{name}.{i}", out var typeInfoPointer))
                        {
                            var typeInfo = Activator.CreateInstance(typeInfoType);

                            var typeNameField = typeInfoType.GetField("name");
                            typeNameField.SetValue(typeInfo, GetString(function.Name));
                            var typeKindField = typeInfoType.GetField("type");
                            typeKindField.SetValue(typeInfo, function.TypeKind);

                            _typeInfoPointers[$"{name}.{i}"] = typeInfoPointer = Marshal.AllocHGlobal(typeInfoSize);
                            newTypeInfos.Add((function, typeInfo, typeInfoPointer));
                        }

                        var arrayPointer = IntPtr.Add(_typeDataPointer, function.TypeIndex * pointerSize);
                        Marshal.StructureToPtr(typeInfoPointer, arrayPointer, false);
                    }
                }

                // Set fields and enum values on TypeInfo objects
                if (newTypeInfos.Any())
                {
                    var typeFieldArrayType = _types["Array.TypeField"];
                    var typeFieldType = _types["TypeField"];
                    var typeFieldSize = Marshal.SizeOf(typeFieldType);

                    var enumValueArrayType = _types["Array.EnumValue"];
                    var enumValueType = _types["EnumValue"];
                    var enumValueSize = Marshal.SizeOf(enumValueType);

                    var argumentArrayType = _types["Array.ArgumentType"];
                    var argumentType = _types["ArgumentType"];
                    var argumentSize = Marshal.SizeOf(argumentType);

                    foreach (var (type, typeInfo, typeInfoPointer) in newTypeInfos)
                    {
                        switch (type)
                        {
                            case StructAst structAst:
                                var typeFieldArray = Activator.CreateInstance(typeFieldArrayType);
                                InitializeConstArray(typeFieldArray, typeFieldArrayType, typeFieldSize, structAst.Fields.Count);

                                var typeFieldsField = typeInfoType.GetField("fields");
                                typeFieldsField.SetValue(typeInfo, typeFieldArray);

                                var typeFieldArrayDataField = typeFieldArrayType.GetField("data");
                                var typeFieldsDataPointer = GetPointer(typeFieldArrayDataField.GetValue(typeFieldArray));

                                for (var i = 0; i < structAst.Fields.Count; i++)
                                {
                                    var field = structAst.Fields[i];
                                    var typeField = Activator.CreateInstance(typeFieldType);

                                    var typeFieldName = typeFieldType.GetField("name");
                                    typeFieldName.SetValue(typeField, GetString(field.Name));
                                    var typeFieldOffset = typeFieldType.GetField("offset");
                                    typeFieldOffset.SetValue(typeField, field.Offset);
                                    var typeFieldInfo = typeFieldType.GetField("type_info");
                                    var typePointer = _typeInfoPointers[field.TypeDefinition.GenericName];
                                    typeFieldInfo.SetValue(typeField, typePointer);

                                    var arrayPointer = IntPtr.Add(typeFieldsDataPointer, typeFieldSize * i);
                                    Marshal.StructureToPtr(typeField, arrayPointer, false);
                                }
                                break;
                            case EnumAst enumAst:
                                var enumValueArray = Activator.CreateInstance(enumValueArrayType);
                                InitializeConstArray(enumValueArray, enumValueArrayType, enumValueSize, enumAst.Values.Count);

                                var enumValuesField = typeInfoType.GetField("enum_values");
                                enumValuesField.SetValue(typeInfo, enumValueArray);

                                var enumValuesArrayDataField = enumValueArrayType.GetField("data");
                                var enumValuesDataPointer = GetPointer(enumValuesArrayDataField.GetValue(enumValueArray));

                                for (var i = 0; i < enumAst.Values.Count; i++)
                                {
                                    var value = enumAst.Values[i];
                                    var enumValue = Activator.CreateInstance(enumValueType);

                                    var enumValueName = enumValueType.GetField("name");
                                    enumValueName.SetValue(enumValue, GetString(value.Name));
                                    var enumValueValue = enumValueType.GetField("value");
                                    enumValueValue.SetValue(enumValue, value.Value);

                                    var arrayPointer = IntPtr.Add(enumValuesDataPointer, enumValueSize * i);
                                    Marshal.StructureToPtr(enumValue, arrayPointer, false);
                                }
                                break;
                            case FunctionAst function:
                                var returnTypeField = typeInfoType.GetField("return_type");
                                returnTypeField.SetValue(typeInfo, _typeInfoPointers[function.ReturnTypeDefinition.GenericName]);

                                var argumentArray = Activator.CreateInstance(argumentArrayType);
                                var argumentCount = function.Varargs ? function.Arguments.Count - 1 : function.Arguments.Count;
                                InitializeConstArray(argumentArray, argumentArrayType, argumentSize, argumentCount);

                                var argumentsField = typeInfoType.GetField("arguments");
                                argumentsField.SetValue(typeInfo, argumentArray);

                                var argumentArrayDataField = argumentArrayType.GetField("data");
                                var argumentArrayDataPointer = GetPointer(argumentArrayDataField.GetValue(argumentArray));

                                for (var i = 0; i < argumentCount; i++)
                                {
                                    var argument = function.Arguments[i];
                                    var argumentValue = Activator.CreateInstance(argumentType);

                                    var argumentName = argumentType.GetField("name");
                                    argumentName.SetValue(argumentValue, GetString(argument.Name));
                                    var argumentTypeField = argumentType.GetField("type_info");

                                    var argumentTypeInfoPointer = argument.TypeDefinition.TypeKind switch
                                    {
                                        TypeKind.Type => _typeInfoPointers["s32"],
                                        TypeKind.Params => _typeInfoPointers[$"Array.{argument.TypeDefinition.Generics[0].GenericName}"],
                                        _ => _typeInfoPointers[argument.TypeDefinition.GenericName]
                                    };
                                    argumentTypeField.SetValue(argumentValue, argumentTypeInfoPointer);

                                    var arrayPointer = IntPtr.Add(argumentArrayDataPointer, argumentSize * i);
                                    Marshal.StructureToPtr(argumentValue, arrayPointer, false);
                                }
                                break;
                        }
                        Marshal.StructureToPtr(typeInfo, typeInfoPointer, false);
                    }
                }

                // Set the variable
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeInfoArrayType));
                Marshal.StructureToPtr(typeTable, pointer, false);
                typeTableVariable.Value = pointer;
            }
        }

        public void RunProgram(IAst ast)
        {
            try
            {
                ExecuteAst(ast, _globalVariables, out _);
            }
            catch (Exception e)
            {
                AddError("Internal compiler error running program", ast);
                #if DEBUG
                Console.WriteLine(e);
                #endif
            }
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
                    return ExecuteExpression(returnAst.Value, variables);
                case DeclarationAst declaration:
                    ExecuteDeclaration(declaration, variables);
                    break;
                case AssignmentAst assignment:
                    ExecuteAssignment(assignment, variables);
                    break;
                case ScopeAst scope:
                    returnValue = ExecuteScope(scope, variables, out returned);
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
                case BreakAst:
                case ContinueAst:
                    break;
                default:
                    return ExecuteExpression(ast, variables);
            }

            return returnValue;
        }

        private void ExecuteDeclaration(DeclarationAst declaration, IDictionary<string, ValueType> variables)
        {
            object value;
            if (declaration.Value != null)
            {
                value = CastValue(ExecuteExpression(declaration.Value, variables).Value, declaration.TypeDefinition);
            }
            else if (declaration.ArrayValues != null)
            {
                value = InitializeArray(declaration.TypeDefinition, variables, declaration.ArrayValues);
            }
            else
            {
                value = GetUninitializedValue(declaration.TypeDefinition, variables, declaration.Assignments);
            }

            var variable = new ValueType {Type = declaration.TypeDefinition};
            if (declaration.TypeDefinition.TypeKind == TypeKind.CArray)
            {
                variable.Value = value;
            }
            else
            {
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(GetTypeFromDefinition(declaration.TypeDefinition)));
                Marshal.StructureToPtr(value, pointer, false);
                variable.Value = pointer;
            }

            variables[declaration.Name] = variable;
        }

        private object GetUninitializedValue(TypeDefinition typeDef, IDictionary<string, ValueType> variables, Dictionary<string, AssignmentAst> assignments)
        {
            switch (typeDef.TypeKind)
            {
                case TypeKind.Integer:
                    return 0;
                case TypeKind.Float:
                    return typeDef.PrimitiveType.Bytes == 4 ? 0f : 0.0;
                case TypeKind.Array:
                case TypeKind.CArray:
                    return InitializeArray(typeDef, variables);
                case TypeKind.Pointer:
                    return IntPtr.Zero;
                default:
                    var instanceType = _types[typeDef.GenericName];
                    var type = TypeTable.Types[typeDef.GenericName];
                    if (type is StructAst structAst)
                    {
                        return InitializeStruct(instanceType, structAst, variables, assignments);
                    }

                    return Activator.CreateInstance(instanceType);
            }
        }

        private object InitializeStruct(Type type, StructAst structAst, IDictionary<string, ValueType> variables, Dictionary<string, AssignmentAst> assignments)
        {
            var instance = Activator.CreateInstance(type);

            if (assignments == null)
            {
                foreach (var field in structAst.Fields)
                {
                    var fieldInstance = instance!.GetType().GetField(field.Name);

                    InitializeField(field, instance, fieldInstance, variables);
                }
            }
            else
            {
                foreach (var field in structAst.Fields)
                {
                    var fieldInstance = instance!.GetType().GetField(field.Name);

                    if (assignments.TryGetValue(field.Name, out var assignment))
                    {
                        var expression = ExecuteExpression(assignment.Value, variables);
                        var value = CastValue(expression.Value, field.TypeDefinition);

                        fieldInstance!.SetValue(instance, value);
                    }
                    else
                    {
                        InitializeField(field, instance, fieldInstance, variables);
                    }
                }
            }

            return instance;
        }

        private void InitializeField(StructFieldAst field, object instance, FieldInfo fieldInstance, IDictionary<string, ValueType> variables)
        {
            if (field.Value != null)
            {
                var value = ExecuteExpression(field.Value, variables);
                fieldInstance.SetValue(instance, value.Value);
            }
            else switch (field.TypeDefinition.TypeKind)
            {
                case TypeKind.Array:
                case TypeKind.CArray:
                    var array = InitializeArray(field.TypeDefinition, variables, field.ArrayValues, true);
                    fieldInstance!.SetValue(instance, array);
                    break;
                case TypeKind.Pointer:
                case TypeKind.Boolean:
                    break;
                default:
                {
                    if (field.TypeDefinition.PrimitiveType == null)
                    {
                        var fieldType = _types[field.TypeDefinition.GenericName];
                        var fieldTypeDef = TypeTable.Types[field.TypeDefinition.GenericName];
                        if (fieldTypeDef is StructAst fieldStructAst)
                        {
                            var value = InitializeStruct(fieldType, fieldStructAst, variables, field.Assignments);
                            fieldInstance.SetValue(instance, value);
                        }
                        else
                        {
                            fieldInstance.SetValue(instance, Activator.CreateInstance(fieldType));
                        }
                    }
                    break;
                }
            }
        }

        private object InitializeArray(TypeDefinition type, IDictionary<string, ValueType> variables, List<IAst> arrayValues = null, bool structField = false)
        {
            var elementTypeDef = type.Generics[0];
            var elementType = GetTypeFromDefinition(elementTypeDef);
            var elementSize = Marshal.SizeOf(elementType);

            if (type.TypeKind == TypeKind.CArray)
            {
                if (structField)
                {
                    var array = Array.CreateInstance(elementType, type.ConstCount.Value);
                    if (arrayValues != null)
                    {
                        for (var i = 0; i < arrayValues.Count; i++)
                        {
                            var value = ExecuteExpression(arrayValues[i], variables);

                            array.SetValue(CastValue(value.Value, elementTypeDef), i);
                        }
                    }

                    return array;
                }
                else
                {
                    var arrayPointer = Marshal.AllocHGlobal(elementSize * (int)type.ConstCount.Value);
                    if (arrayValues != null)
                    {
                        InitializeArrayValues(arrayPointer, elementSize, elementTypeDef, arrayValues, variables);
                    }

                    return arrayPointer;
                }
            }
            else
            {
                var arrayType = _types[type.GenericName];
                var array = Activator.CreateInstance(arrayType);
                if (type.ConstCount != null)
                {
                    var arrayPointer = InitializeConstArray(array, arrayType, elementSize, (int)type.ConstCount.Value);

                    if (arrayValues != null)
                    {
                        InitializeArrayValues(arrayPointer, elementSize, elementTypeDef, arrayValues, variables);
                    }
                }
                else if (type.Count != null)
                {
                    var length = (int)ExecuteExpression(type.Count, variables).Value;
                    InitializeConstArray(array, arrayType, elementSize, length);
                }

                return array;
            }
        }

        private static IntPtr InitializeConstArray(object array, Type arrayType, int elementSize, int length)
        {
            var countField = arrayType.GetField("length");
            countField!.SetValue(array, length);
            var dataField = arrayType.GetField("data");
            var arrayPointer = Marshal.AllocHGlobal(elementSize * length);
            dataField!.SetValue(array, arrayPointer);
            return arrayPointer;
        }

        private void InitializeArrayValues(IntPtr arrayPointer, int elementSize, TypeDefinition elementType, List<IAst> arrayValues, IDictionary<string, ValueType> variables)
        {
            for (var i = 0; i < arrayValues.Count; i++)
            {
                var value = ExecuteExpression(arrayValues[i], variables);

                var pointer = IntPtr.Add(arrayPointer, elementSize * i);
                Marshal.StructureToPtr(CastValue(value.Value, elementType), pointer, false);
            }
        }

        private void ExecuteAssignment(AssignmentAst assignment, IDictionary<string, ValueType> variables)
        {
            var expression = ExecuteExpression(assignment.Value, variables);
            if (assignment.Operator != Operator.None)
            {
                var lhs = ExecuteExpression(assignment.Reference, variables);
                switch (assignment.Reference)
                {
                    case IndexAst index when index.CallsOverload:
                    case StructFieldRefAst structField when structField.Children[^1] is IndexAst indexAst && indexAst.CallsOverload:
                        if (lhs.Type.Name == "*")
                        {
                            lhs.Type = lhs.Type.Generics[0];
                            lhs.Value = PointerToTargetType(GetPointer(lhs.Value), lhs.Type);
                        }
                        break;
                }
                expression.Value = RunExpression(lhs, expression, assignment.Operator, lhs.Type);
                expression.Type = lhs.Type;
            }

            switch (assignment.Reference)
            {
                case IdentifierAst identifier:
                {
                    var variable = variables[identifier.Name];
                    Marshal.StructureToPtr(CastValue(expression.Value, variable.Type), GetPointer(variable.Value), false);
                    break;
                }
                case StructFieldRefAst structField:
                {
                    var pointer = GetStructFieldRef(structField, variables, out _, out var constant, out var fromIndexOverload);
                    if (fromIndexOverload)
                    {
                        pointer.Type = pointer.Type.Generics[0];
                    }
                    if (!constant)
                    {
                        Marshal.StructureToPtr(CastValue(expression.Value, pointer.Type), GetPointer(pointer.Value), false);
                    }
                    break;
                }
                case IndexAst indexAst:
                {
                    var (typeDef, _, pointer) = GetIndexPointer(indexAst, variables, out var loaded);
                    if (loaded)
                    {
                        typeDef = typeDef.Generics[0];
                    }
                    Marshal.StructureToPtr(CastValue(expression.Value, typeDef), GetPointer(pointer), false);
                    break;
                }
                case UnaryAst unary:
                {
                    var pointer = ExecuteExpression(unary.Value, variables).Value;
                    Marshal.StructureToPtr(expression.Value, GetPointer(pointer), false);
                    break;
                }
            }
        }

        private ValueType ExecuteScope(ScopeAst scope, IDictionary<string, ValueType> variables, out bool returned)
        {
            if (!scope.Children.Any())
            {
                returned = false;
                return null;
            }

            var scopeVariables = new Dictionary<string, ValueType>(variables);

            return ExecuteAsts(scope.Children, scopeVariables, out returned);
        }

        private ValueType ExecuteConditional(ConditionalAst conditional, IDictionary<string, ValueType> variables, out bool returned)
        {
            if (ExecuteCondition(conditional.Condition, variables))
            {
                return ExecuteScope(conditional.IfBlock, variables, out returned);
            }

            if (conditional.ElseBlock != null)
            {
                return ExecuteScope(conditional.ElseBlock, variables, out returned);
            }

            returned = false;
            return null;
        }

        private ValueType ExecuteWhile(WhileAst whileAst, IDictionary<string, ValueType> variables, out bool returned)
        {
            while (ExecuteCondition(whileAst.Condition, variables))
            {
                var value = ExecuteScope(whileAst.Body, variables, out returned);

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
            try
            {
                return ExecuteCondition(expression, _globalVariables);
            }
            catch (Exception e)
            {
                AddError("Internal compiler error executing condition", expression);
                #if DEBUG
                Console.WriteLine(e);
                #endif
                return false;
            }
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

                var elementType = iterator.Type.Generics[0];
                var type = GetTypeFromDefinition(elementType);
                var iterationVariable = new ValueType {Type = elementType};
                eachVariables.Add(each.IterationVariable, iterationVariable);

                IntPtr dataPointer;
                int length;
                if (iterator.Type.TypeKind == TypeKind.CArray)
                {
                    dataPointer = GetPointer(iterator.Value);
                    length = (int)iterator.Type.ConstCount.Value;
                }
                else
                {
                    var dataField = iterator.Value.GetType().GetField("data");
                    var data = dataField!.GetValue(iterator.Value);
                    dataPointer = GetPointer(data!);

                    var lengthField = iterator.Value.GetType().GetField("length");
                    length = (int)lengthField!.GetValue(iterator.Value)!;
                }

                if (each.IndexVariable != null)
                {
                    var indexVariablePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(int)));
                    var indexVariable = new ValueType {Type = _s32Type, Value = indexVariablePointer};
                    eachVariables.Add(each.IndexVariable, indexVariable);
                    for (var i = 0; i < length; i++)
                    {
                        Marshal.StructureToPtr(i, indexVariablePointer, false);
                        iterationVariable.Value = IntPtr.Add(dataPointer, Marshal.SizeOf(type) * i);

                        var value = ExecuteAsts(each.Body.Children, eachVariables, out returned);

                        if (returned)
                        {
                            return value;
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < length; i++)
                    {
                        iterationVariable.Value = IntPtr.Add(dataPointer, Marshal.SizeOf(type) * i);

                        var value = ExecuteAsts(each.Body.Children, eachVariables, out returned);

                        if (returned)
                        {
                            return value;
                        }
                    }
                }
            }
            else
            {
                var iterationValue = ExecuteExpression(each.RangeBegin, variables);
                var rangeEnd = ExecuteExpression(each.RangeEnd, variables);

                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(GetTypeFromDefinition(iterationValue.Type)));
                var iterationVariable = new ValueType {Type = iterationValue.Type, Value = pointer};
                eachVariables.Add(each.IterationVariable, iterationVariable);

                while ((bool)RunExpression(iterationValue, rangeEnd, Operator.LessThanEqual, iterationValue.Type))
                {
                    Marshal.StructureToPtr(iterationValue.Value, pointer, false);
                    var value = ExecuteAsts(each.Body.Children, eachVariables, out returned);

                    if (returned)
                    {
                        return value;
                    }

                    iterationValue.Value = (int)iterationValue.Value + 1;
                }
            }

            returned = false;
            return null;
        }

        private ValueType ExecuteAsts(List<IAst> asts, IDictionary<string, ValueType> variables, out bool returned)
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
                    return new ValueType {Type = constant.TypeDefinition, Value = GetConstant(constant.TypeDefinition, constant.Value)};
                case NullAst nullAst:
                    return new ValueType {Type = nullAst.TargetType, Value = IntPtr.Zero};
                case StructFieldRefAst structField:
                {
                    if (structField.IsEnum)
                    {
                        return GetEnum(structField);
                    }
                    return GetStructFieldRef(structField, variables, out _, out _, out _, true);
                }
                case IdentifierAst identifier:
                {
                    if (!variables.TryGetValue(identifier.Name, out var variable))
                    {
                        var type = TypeTable.Types[identifier.Name];
                        return new ValueType {Type = _s32Type, Value = type.TypeIndex};
                    }

                    var value = variable.Type.TypeKind == TypeKind.CArray ? variable.Value : PointerToTargetType(GetPointer(variable.Value), variable.Type);
                    return new ValueType {Type = variable.Type, Value = value};
                }
                case ChangeByOneAst changeByOne:
                    switch (changeByOne.Value)
                    {
                        case IdentifierAst identifier:
                        {
                            var variable = variables[identifier.Name];
                            var pointer = GetPointer(variable.Value);

                            var previousValue = variable.Type.Constant ? variable.Value : PointerToTargetType(pointer, variable.Type);
                            var newValue = PerformOperation(variable.Type, previousValue, changeByOne.Positive ? 1 : -1, Operator.Add);

                            if (!variable.Type.Constant)
                            {
                                Marshal.StructureToPtr(newValue, pointer, false);
                            }

                            return new ValueType {Type = variable.Type, Value = changeByOne.Prefix ? newValue : previousValue};
                        }
                        case StructFieldRefAst structField:
                        {
                            var result = GetStructFieldRef(structField, variables, out var loaded, out var constant, out _);
                            var type = result.Type;
                            var pointer = GetPointer(result.Value);

                            if (loaded && type.Name == "*")
                            {
                                type = type.Generics[0];
                            }

                            var previousValue = constant ? pointer : PointerToTargetType(pointer, type);
                            var newValue = PerformOperation(type, previousValue, changeByOne.Positive ? 1 : -1, Operator.Add);

                            if (!constant)
                            {
                                Marshal.StructureToPtr(newValue, pointer, false);
                            }

                            return new ValueType {Type = type, Value = changeByOne.Prefix ? newValue : previousValue};
                        }
                        case IndexAst indexAst:
                        {
                            var (type, elementType, pointer) = GetIndexPointer(indexAst, variables, out var loaded);
                            if (loaded)
                            {
                                type = type.Generics[0];
                                elementType = GetTypeFromDefinition(type);
                            }

                            var previousValue = Marshal.PtrToStructure(GetPointer(pointer), elementType);
                            var newValue = PerformOperation(type, previousValue, changeByOne.Positive ? 1 : -1, Operator.Add);
                            Marshal.StructureToPtr(newValue, GetPointer(pointer), false);

                            return new ValueType {Type = type, Value = changeByOne.Prefix ? newValue : previousValue};
                        }
                    }
                    break;
                case UnaryAst unary:
                {
                    if (unary.Operator == UnaryOperator.Reference)
                    {
                        TypeDefinition typeDef = null;
                        object pointer = null;
                        switch (unary.Value)
                        {
                            case IdentifierAst identifier:
                                var variable = variables[identifier.Name];
                                typeDef = variable.Type;
                                pointer = variable.Value;
                                break;
                            case IndexAst index:
                                (typeDef, _, pointer) = GetIndexPointer(index, variables, out _);
                                break;
                            case StructFieldRefAst structField:
                                var value = GetStructFieldRef(structField, variables, out _, out _, out _);
                                typeDef = value.Type;
                                pointer = value.Value;
                                break;
                        }

                        var pointerType = new TypeDefinition {Name = "*", TypeKind = TypeKind.Pointer};
                        if (typeDef.TypeKind == TypeKind.CArray)
                        {
                            pointerType.Generics.Add(typeDef.Generics[0]);
                        }
                        else
                        {
                            pointerType.Generics.Add(typeDef);
                        }

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
                    }
                    break;
                }
                case CallAst call:
                    return ExecuteCall(call, variables);
                case ExpressionAst expression:
                    var firstValue = ExecuteExpression(expression.Children[0], variables);
                    var expressionValue = new ValueType {Type = firstValue.Type, Value = firstValue.Value};
                    for (var i = 1; i < expression.Children.Count; i++)
                    {
                        var rhs = ExecuteExpression(expression.Children[i], variables);
                        var nextType = expression.ResultingTypeDefinitions[i - 1];
                        expressionValue.Value = RunExpression(expressionValue, rhs, expression.Operators[i - 1], nextType);
                        expressionValue.Type = nextType;
                    }
                    return expressionValue;
                case IndexAst indexAst:
                {
                    var (typeDef, elementType, value) = GetIndexPointer(indexAst, variables, out var loaded);
                    if (!loaded)
                    {
                        value = PointerToTargetType(GetPointer(value), typeDef, elementType);
                    }
                    return new ValueType {Type = typeDef, Value = value};
                }
                case TypeDefinition typeDef:
                {
                    var type = TypeTable.Types[typeDef.GenericName];
                    return new ValueType {Type = _s32Type, Value = type.TypeIndex};
                }
                case CastAst cast:
                {
                    var value = ExecuteExpression(cast.Value, variables);
                    return new ValueType {Type = cast.TargetTypeDefinition, Value = CastValue(value.Value, cast.TargetTypeDefinition)};
                }
            }

            return null;
        }

        private object GetConstant(TypeDefinition type, string value)
        {
            switch (type.PrimitiveType)
            {
                case IntegerType integerType:
                    if (type.Character)
                    {
                        return (byte)value[0];
                    }

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

        private ValueType GetEnum(StructFieldRefAst structField)
        {
            var enumDef = (EnumAst)structField.Types[0];
            var enumValue = enumDef.Values[structField.ValueIndices[0]].Value;
            return new ValueType {Type = enumDef.BaseTypeDefinition, Value = CastValue(enumValue, enumDef.BaseTypeDefinition)};
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

        private ValueType GetStructFieldRef(StructFieldRefAst structField, IDictionary<string, ValueType> variables, out bool loaded, out bool constant, out bool valueFromIndexOverload, bool load = false)
        {
            constant = false;
            valueFromIndexOverload = false;
            TypeDefinition type = null;
            var pointer = IntPtr.Zero;
            object value = null;
            var skipPointer = false;

            switch (structField.Children[0])
            {
                case IdentifierAst identifier:
                    var variable = variables[identifier.Name];
                    type = variable.Type;
                    pointer = GetPointer(variable.Value);
                    break;
                case IndexAst index:
                    var (typeDef, elementType, indexPointer) = GetIndexPointer(index, variables, out _);
                    type = typeDef;
                    if (index.CallsOverload && !structField.Pointers[0])
                    {
                        pointer = Marshal.AllocHGlobal(Marshal.SizeOf(elementType));
                        Marshal.StructureToPtr(indexPointer, pointer, false);
                    }
                    else
                    {
                        pointer = GetPointer(indexPointer);
                    }
                    break;
                case CallAst call:
                    var callResult = ExecuteCall(call, variables);
                    type = callResult.Type;
                    skipPointer = true;
                    if (structField.Pointers[0])
                    {
                        pointer = GetPointer(callResult.Value);
                    }
                    else
                    {
                        pointer = Marshal.AllocHGlobal(Marshal.SizeOf(GetTypeFromDefinition(callResult.Type)));
                        Marshal.StructureToPtr(callResult.Value, pointer, false);
                    }
                    break;
            }

            for (var i = 1; i < structField.Children.Count; i++)
            {
                if (structField.Pointers[i-1])
                {
                    if (!skipPointer)
                    {
                        pointer = Marshal.ReadIntPtr(pointer);
                    }
                    type = type.Generics[0];
                }
                skipPointer = false;

                if (type.TypeKind == TypeKind.CArray)
                {
                    switch (structField.Children[i])
                    {
                        case IdentifierAst identifier:
                            constant = true;
                            if (identifier.Name == "length")
                            {
                                var typeCount = ExecuteExpression(type.Count, variables);
                                type = typeCount.Type;
                                value = typeCount.Value;
                            }
                            break;
                        case IndexAst index:
                            var (_, _, indexPointer) = GetIndexPointer(index, variables, out _, pointer, type);
                            pointer = GetPointer(indexPointer);
                            type = type.Generics[0];
                            break;
                    }
                    continue;
                }

                var structDefinition = (StructAst) structField.Types[i-1];
                var field = structDefinition.Fields[structField.ValueIndices[i-1]];
                var structType = GetTypeFromDefinition(type);
                var offset = (int)Marshal.OffsetOf(structType, field.Name);
                type = field.TypeDefinition;

                switch (structField.Children[i])
                {
                    case IdentifierAst identifier:
                        pointer = IntPtr.Add(pointer, offset);
                        break;
                    case IndexAst index:
                        pointer = IntPtr.Add(pointer, offset);
                        var (typeDef, _, result) = GetIndexPointer(index, variables, out _, pointer, type);
                        type = typeDef;
                        if (index.CallsOverload)
                        {
                            skipPointer = true;
                            if (i < structField.Pointers.Length)
                            {
                                if (structField.Pointers[i])
                                {
                                    pointer = GetPointer(result);
                                }
                                else
                                {
                                    pointer = Marshal.AllocHGlobal(Marshal.SizeOf(GetTypeFromDefinition(type)));
                                    Marshal.StructureToPtr(result, pointer, false);
                                }
                            }
                            else
                            {
                                valueFromIndexOverload = true;
                                value = result;
                            }
                        }
                        else
                        {
                            pointer = GetPointer(result);
                        }
                        break;
                }
            }

            if (value == null)
            {
                loaded = false;
                value = load && type.TypeKind != TypeKind.CArray ? PointerToTargetType(pointer, type) : pointer;
            }
            else
            {
                loaded = true;
            }

            return new ValueType {Type = type, Value = value};
        }

        private ValueType ExecuteCall(CallAst call, IDictionary<string, ValueType> variables)
        {
            var function = call.Function;
            if (call.Function.Params)
            {
                var arguments = new object[function.Arguments.Count];
                for (var i = 0; i < function.Arguments.Count - 1; i++)
                {
                    var value = ExecuteExpression(call.Arguments[i], variables).Value;
                    arguments[i] = value;
                }

                var elementType = function.Arguments[^1].TypeDefinition.Generics[0];
                var paramsType = GetTypeFromDefinition(elementType);
                var arrayType = _types[$"Array.{elementType.GenericName}"];
                var paramsArray = Activator.CreateInstance(arrayType);
                InitializeConstArray(paramsArray, arrayType, Marshal.SizeOf(paramsType), call.Arguments.Count - function.Arguments.Count + 1);

                var dataField = arrayType.GetField("data");
                var data = dataField!.GetValue(paramsArray);
                var dataPointer = GetPointer(data!);

                var paramsIndex = 0;
                for (var i = function.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                {
                    var value = ExecuteExpression(call.Arguments[i], variables).Value;

                    var valuePointer = IntPtr.Add(dataPointer, Marshal.SizeOf(paramsType) * paramsIndex);
                    Marshal.StructureToPtr(value, valuePointer, false);
                }

                arguments[function.Arguments.Count - 1] = paramsArray;

                return CallFunction(call.FunctionName, function, arguments);
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

                return CallFunction(call.FunctionName, function, arguments, types, call.VarargsIndex);
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

                return CallFunction(call.FunctionName, function, arguments, types);
            }
        }

        private StructAst _stringStruct;

        private (TypeDefinition typeDef, Type elementType, object pointer) GetIndexPointer(IndexAst index, IDictionary<string, ValueType> variables, out bool loaded, IntPtr pointer = default, TypeDefinition indexTypeDef = null)
        {
            var indexValue = (int)ExecuteExpression(index.Index, variables).Value;

            TypeDefinition elementTypeDef;
            if (pointer == default)
            {
                var variable = variables[index.Name];
                pointer = GetPointer(variable.Value);
                indexTypeDef ??= variable.Type;
            }

            if (index.CallsOverload)
            {
                var lhs = PointerToTargetType(pointer, indexTypeDef);
                var value = HandleOverloadedOperator(indexTypeDef, Operator.Subscript, lhs, indexValue);
                loaded = true;

                return (value.Type, GetTypeFromDefinition(value.Type), value.Value);
            }

            if (indexTypeDef.TypeKind == TypeKind.String)
            {
                _stringStruct ??= (StructAst)TypeTable.Types["string"];
                elementTypeDef = _stringStruct.Fields[1].TypeDefinition.Generics[0];
            }
            else
            {
                elementTypeDef = indexTypeDef.Generics[0];
            }
            var elementType = GetTypeFromDefinition(elementTypeDef);

            if (indexTypeDef.TypeKind == TypeKind.Pointer)
            {
                pointer = Marshal.ReadIntPtr(pointer);
            }
            else if (indexTypeDef.TypeKind != TypeKind.CArray)
            {
                var arrayObject = PointerToTargetType(pointer, indexTypeDef);
                var dataField = arrayObject.GetType().GetField("data");
                var data = dataField!.GetValue(arrayObject);
                pointer = GetPointer(data!);
            }

            if (indexValue != 0)
            {
                pointer = IntPtr.Add(pointer, Marshal.SizeOf(elementType) * indexValue);
            }

            loaded = false;
            return (elementTypeDef, elementType, pointer);
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
                    return new ValueType {Type = function.ReturnTypeDefinition, Value = returnValue};
                }
                else
                {
                    var functionIndex = _functionIndices[functionName][callIndex];
                    var (type, functionObject) = _functionLibraries[functionIndex];
                    var functionDecl = type.GetMethod(functionName);
                    var returnValue = functionDecl.Invoke(functionObject, args);
                    return new ValueType {Type = function.ReturnTypeDefinition, Value = returnValue};
                }
            }

            if (function.Compiler)
            {
                if (!_compilerFunctions.TryGetValue(function.Name, out var name))
                {
                    AddError($"Undefined compiler function '{function.Name}'", function);
                    return null;
                }
                var args = arguments.Select(GetManagedArg).ToArray();

                var functionDecl = typeof(ProgramRunner).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
                var returnValue = functionDecl.Invoke(this, args);
                return new ValueType {Type = function.ReturnTypeDefinition, Value = returnValue};
            }

            return ExecuteFunction(function, arguments);
        }

        private ValueType ExecuteFunction(IFunction function, object[] arguments)
        {
            var variables = new Dictionary<string, ValueType>(_globalVariables);

            for (var i = 0; i < function.Arguments.Count; i++)
            {
                var arg = function.Arguments[i];
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(GetTypeFromDefinition(arg.TypeDefinition)));
                Marshal.StructureToPtr(arguments[i], pointer, false);
                variables[arg.Name] = new ValueType {Type = arg.TypeDefinition, Value = pointer};
            }

            return ExecuteAsts(function.Body.Children, variables, out _);
        }

        private static object GetCArg(object argument)
        {
            var type = argument.GetType();
            if (type.Name == "string")
            {
                var dataField = type.GetField("data");
                return GetPointer(dataField!.GetValue(argument));
            }
            else if (argument is Pointer)
            {
                return GetPointer(argument);
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

        private object RunExpression(ValueType lhs, ValueType rhs, Operator op, TypeDefinition targetType)
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

            // 2. Handle compares and shifts, since the lhs and rhs should not be cast to the target type
            switch (op)
            {
                case Operator.And:
                case Operator.Or:
                    if (lhs.Type.Name == "bool")
                    {
                        var lhsBool = (bool)lhs.Value;
                        var rhsBool = (bool)rhs.Value;
                        return op == Operator.And ? lhsBool && rhsBool : lhsBool || rhsBool;
                    }
                    else
                    {
                        return HandleOverloadedOperator(lhs.Type, op, lhs.Value, rhs.Value).Value;
                    }
                case Operator.Equality:
                case Operator.NotEqual:
                case Operator.GreaterThanEqual:
                case Operator.LessThanEqual:
                case Operator.GreaterThan:
                case Operator.LessThan:
                    return Compare(lhs, rhs, op);
                case Operator.ShiftLeft:
                    return Shift(lhs, rhs, op);
                case Operator.ShiftRight:
                    return Shift(lhs, rhs, op, true);
                case Operator.RotateLeft:
                    return Shift(lhs, rhs, op, rotate: true);
                case Operator.RotateRight:
                    return Shift(lhs, rhs, op, true, true);
            }

            // 3. Handle overloaded operators
            if (lhs.Type.PrimitiveType == null && lhs.Type.Name != "bool")
            {
                return HandleOverloadedOperator(lhs.Type, op, lhs.Value, rhs.Value).Value;
            }

            // 4. Cast lhs and rhs to the target types
            var lhsValue = CastValue(lhs.Value, targetType);
            var rhsValue = CastValue(rhs.Value, targetType);

            // 5. Handle the rest of the simple operators
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

            // 6. Handle binary operations
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
                return lhsPointer != GetPointer(rhs);
            }
            if (op == Operator.Subtract)
            {
                return IntPtr.Subtract(lhsPointer, (int)rhs);
            }
            return IntPtr.Add(lhsPointer, (int)rhs);
        }

        private object Compare(ValueType lhs, ValueType rhs, Operator op)
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
                default:
                    return HandleOverloadedOperator(lhs.Type, op, lhs.Value, rhs.Value).Value;
            }

            // @Cleanup this should not be hit
            return null;
        }

        private object Shift(ValueType lhs, ValueType rhs, Operator op, bool right = false, bool rotate = false)
        {
            if (lhs.Type.PrimitiveType is IntegerType integerType)
            {
                var rhsValue = Convert.ToInt32(CastValue(rhs.Value, new TypeDefinition {TypeKind = TypeKind.Integer, PrimitiveType = new IntegerType {Bytes = 4, Signed = true}}));
                switch (integerType.Bytes)
                {
                    case 1:
                        if (integerType.Signed)
                        {
                            var lhsValue = Convert.ToSByte(lhs.Value);
                            var result = right ? lhsValue >> rhsValue : lhsValue << rhsValue;
                            if (rotate)
                            {
                                result |= right ? lhsValue << (8 - rhsValue) : lhsValue >> (8 - rhsValue);
                            }
                            return CastValue(result, lhs.Type);
                        }
                        else
                        {
                            var lhsValue = Convert.ToByte(lhs.Value);
                            var result = right ? lhsValue >> rhsValue : lhsValue << rhsValue;
                            if (rotate)
                            {
                                result |= right ? lhsValue << (8 - rhsValue) : lhsValue >> (8 - rhsValue);
                            }
                            return CastValue(result, lhs.Type);
                        }
                    case 2:
                        if (integerType.Signed)
                        {
                            var lhsValue = Convert.ToInt16(lhs.Value);
                            var result = right ? lhsValue >> rhsValue : lhsValue << rhsValue;
                            if (rotate)
                            {
                                result |= right ? lhsValue << (16 - rhsValue) : lhsValue >> (16 - rhsValue);
                            }
                            return CastValue(result, lhs.Type);
                        }
                        else
                        {
                            var lhsVal = Convert.ToUInt16(lhs.Value);
                            var result = right ? lhsVal >> rhsValue : lhsVal << rhsValue;
                            if (rotate)
                            {
                                result |= right ? lhsVal << (16 - rhsValue) : lhsVal >> (16 - rhsValue);
                            }
                            return CastValue(result, lhs.Type);
                        }
                    case 4:
                        if (integerType.Signed)
                        {
                            var lhsValue = Convert.ToInt32(lhs.Value);
                            var result = right ? lhsValue >> rhsValue : lhsValue << rhsValue;
                            if (rotate)
                            {
                                result |= right ? lhsValue << (32 - rhsValue) : lhsValue >> (32 - rhsValue);
                            }
                            return result;
                        }
                        else
                        {
                            var lhsValue = Convert.ToUInt32(lhs.Value);
                            var result = right ? lhsValue >> rhsValue : lhsValue << rhsValue;
                            if (rotate)
                            {
                                result |= right ? lhsValue << (32 - rhsValue) : lhsValue >> (32 - rhsValue);
                            }
                            return CastValue(result, lhs.Type);
                        }
                    case 8:
                        if (integerType.Signed)
                        {
                            var lhsValue = Convert.ToInt64(lhs.Value);
                            var result = right ? lhsValue >> rhsValue : lhsValue << rhsValue;
                            if (rotate)
                            {
                                result |= right ? lhsValue << (64 - rhsValue) : lhsValue >> (64 - rhsValue);
                            }
                            return CastValue(result, lhs.Type);
                        }
                        else
                        {
                            var lhsValue = Convert.ToUInt64(lhs.Value);
                            var result = right ? lhsValue >> rhsValue : lhsValue << rhsValue;
                            if (rotate)
                            {
                                result |= right ? lhsValue << (64 - rhsValue) : lhsValue >> (64 - rhsValue);
                            }
                            return CastValue(result, lhs.Type);
                        }
                }
            }

            return HandleOverloadedOperator(lhs.Type, op, lhs.Value, rhs.Value).Value;
        }

        private object PerformOperation(TypeDefinition targetType, object lhsValue, object rhsValue, Operator op)
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
                default:
                    return HandleOverloadedOperator(targetType, op, lhsValue, rhsValue).Value;
            }
        }

        private ValueType HandleOverloadedOperator(TypeDefinition type, Operator op, object lhs, object rhs)
        {
            var operatorOverload = _programGraph.OperatorOverloads[type.GenericName][op];
            return ExecuteFunction(operatorOverload, new []{lhs, rhs});
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

            if (targetType.TypeKind == TypeKind.Pointer)
            {
                return GetPointer(value);
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

        private Type GetTypeFromDefinition(TypeDefinition typeDef, IDictionary<string, TypeBuilder> temporaryTypes, bool pointer = false)
        {
            switch (typeDef.PrimitiveType)
            {
                case IntegerType:
                case EnumType:
                    return GetIntegerType(typeDef.PrimitiveType);
                case FloatType floatType:
                    return floatType.Bytes == 4 ? typeof(float) : typeof(double);
            }

            switch (typeDef.TypeKind)
            {
                case TypeKind.Void:
                    return pointer ? typeof(byte) : typeof(void);
                case TypeKind.Boolean:
                    return typeof(bool);
                case TypeKind.Type:
                    return typeof(int);
                case TypeKind.Pointer:
                    var pointerType = GetTypeFromDefinition(typeDef.Generics[0], temporaryTypes, true);
                    if (pointerType == null)
                    {
                        return null;
                    }
                    return pointerType.MakePointerType();
                case TypeKind.CArray:
                    var elementType = GetTypeFromDefinition(typeDef.Generics[0]);
                    return elementType.MakeArrayType();
            }

            if (_types.TryGetValue(typeDef.GenericName, out var type))
            {
                return type;
            }

            return temporaryTypes.TryGetValue(typeDef.GenericName, out var tempType) ? tempType : null;
        }

        private Type GetTypeFromDefinition(TypeDefinition typeDef, bool cCall = false, bool pointer = false)
        {
            switch (typeDef.PrimitiveType)
            {
                case IntegerType:
                case EnumType:
                    return GetIntegerType(typeDef.PrimitiveType);
                case FloatType floatType:
                    return floatType.Bytes == 4 ? typeof(float) : typeof(double);
            }

            switch (typeDef.TypeKind)
            {
                case TypeKind.Void:
                    return pointer ? typeof(byte) : typeof(void);
                case TypeKind.Boolean:
                    return typeof(bool);
                case TypeKind.Type:
                    return typeof(int);
                case TypeKind.String:
                    if (!cCall) break;
                    return typeof(char).MakePointerType();
                case TypeKind.Pointer:
                    var pointerType = GetTypeFromDefinition(typeDef.Generics[0], cCall, true);
                    if (pointerType == null)
                    {
                        return null;
                    }
                    return pointerType.MakePointerType();
                case TypeKind.Params:
                    return _types.TryGetValue($"Array.{typeDef.Generics[0].GenericName}", out var arrayType) ? arrayType : null;
                case TypeKind.CArray:
                    var elementType = GetTypeFromDefinition(typeDef.Generics[0]);
                    return elementType.MakePointerType();
            }

            return _types.TryGetValue(typeDef.GenericName, out var type) ? type : null;
        }

        private Type GetIntegerType(IPrimitive integerType)
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

        private void AddError(string message, IAst ast)
        {
            _programGraph.Errors.Add(new TranslationError
            {
                Error = message,
                FileIndex = ast.FileIndex,
                Line = ast.Line,
                Column = ast.Column
            });
        }
    }
}
