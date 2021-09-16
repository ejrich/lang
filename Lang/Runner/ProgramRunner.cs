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
        void RunProgram(ProgramGraph programGraph);
    }

    public class ProgramRunner : IProgramRunner
    {
        private Type _library;
        private object _functionObject;
        private readonly Dictionary<string, ValueType> _globalVariables = new();
        private readonly Dictionary<string, Type> _types = new();

        private class ValueType
        {
            public TypeDefinition Type { get; set; }
            public object Value { get; set; }
        }

        public void RunProgram(ProgramGraph programGraph)
        {
            if (!programGraph.Directives.Any()) return;

            var assemblyName = new AssemblyName("ExternFunctions");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule("ExternFunctions");
            var typeBuilder = moduleBuilder.DefineType("Functions", TypeAttributes.Class | TypeAttributes.Public);

            var temporaryStructs = new Dictionary<string, TypeBuilder>();
            foreach (var (_, type) in programGraph.Data.Types)
            {
                switch (type)
                {
                    case EnumAst enumAst:
                        var enumBuilder = moduleBuilder.DefineEnum(enumAst.Name, TypeAttributes.Public, typeof(int));
                        foreach (var value in enumAst.Values)
                        {
                            enumBuilder.DefineLiteral(value.Name, value.Value);
                        }
                        _types[enumAst.Name] = enumBuilder.CreateTypeInfo();
                        break;
                    case StructAst structAst:
                        var structBuilder = moduleBuilder.DefineType(structAst.Name, TypeAttributes.Public | TypeAttributes.SequentialLayout);
                        temporaryStructs[structAst.Name] = structBuilder;
                        break;
                }
            }

            var fieldIndices = new Dictionary<string, int>();
            while (temporaryStructs.Any())
            {
                foreach (var (name, structBuilder) in temporaryStructs)
                {
                    var indexFound = fieldIndices.TryGetValue(name, out var index);
                    var structAst = programGraph.Data.Types[name] as StructAst;
                    var count = structAst!.Fields.Count;

                    for (; index < count; index++)
                    {
                        var field = structAst.Fields[index];
                        var fieldType = GetTypeFromDefinition(field.Type);
                        if (fieldType == null)
                        {
                            break;
                        }
                        structBuilder.DefineField(field.Name, fieldType, FieldAttributes.Public);
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

            foreach (var function in programGraph.Functions.Values.Where(_ => _.Extern))
            {
                if (function.Varargs)
                {
                    // TODO For varargs functions, get usages instead of only using the definition
                    continue;
                }

                var returnType = GetTypeFromDefinition(function.ReturnType);
                var args = function.Arguments.Select(arg => GetTypeFromDefinition(arg.Type)).ToArray();
                CreateFunction(typeBuilder, function.Name, function.ExternLib, returnType, args);
            }

            CreateFunction(typeBuilder, "printf", "libc", null, typeof(string), typeof(int));

            _library = typeBuilder.CreateType();
            _functionObject = Activator.CreateInstance(_library!);

            foreach (var variable in programGraph.Data.Variables)
            {
                ExecuteDeclaration(variable, programGraph, _globalVariables);
            }

            foreach (var directive in programGraph.Directives)
            {
                switch (directive.Type)
                {
                    case DirectiveType.Run:
                        ExecuteAst(directive.Value, programGraph, _globalVariables, out _);
                        break;
                    case DirectiveType.If:
                        // TODO Evaluate the condition
                        break;
                }
            }
        }

        private ValueType ExecuteAst(IAst ast, ProgramGraph programGraph, IDictionary<string, ValueType> variables, out bool returned)
        {
            returned = false;
            ValueType returnValue = null;
            switch (ast)
            {
                case ReturnAst returnAst:
                    returned = true;
                    return ExecuteReturn(returnAst, programGraph, variables);
                case DeclarationAst declaration:
                    ExecuteDeclaration(declaration, programGraph, variables);
                    break;
                case AssignmentAst assignment:
                    ExecuteAssignment(assignment, programGraph, variables);
                    break;
                case ScopeAst scope:
                    returnValue = ExecuteScope(scope.Children, programGraph, variables, out returned);
                    break;
                case ConditionalAst conditional:
                    returnValue = ExecuteConditional(conditional, programGraph, variables, out returned);
                    break;
                case WhileAst whileAst:
                    returnValue = ExecuteWhile(whileAst, programGraph, variables, out returned);
                    break;
                case EachAst each:
                    returnValue = ExecuteEach(each, programGraph, variables, out returned);
                    break;
                default:
                    return ExecuteExpression(ast, programGraph, variables);
            }

            return returnValue;
        }

        private ValueType ExecuteReturn(ReturnAst returnAst, ProgramGraph programGraph, IDictionary<string, ValueType> variables)
        {
            return ExecuteExpression(returnAst.Value, programGraph, variables);
        }

        private void ExecuteDeclaration(DeclarationAst declaration, ProgramGraph programGraph, IDictionary<string, ValueType> variables)
        {
            var value = declaration.Value == null ?
                GetUninitializedValue(declaration.Type, programGraph, variables, declaration.Assignments) :
                ExecuteExpression(declaration.Value, programGraph, null).Value;

            variables[declaration.Name] = new ValueType {Type = declaration.Type, Value = value};
        }

        private object GetUninitializedValue(TypeDefinition typeDef, ProgramGraph programGraph,
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
                            // TODO Handle lists
                            return null;
                        case "*":
                            // TODO Handle pointers
                            return IntPtr.Zero;
                    }
                    var instanceType = _types[typeDef.GenericName];
                    var type = programGraph.Data.Types[typeDef.GenericName];
                    if (type is StructAst structAst)
                    {
                        return InitializeStruct(instanceType, structAst, programGraph, variables, values);
                    }

                    return Activator.CreateInstance(instanceType);
            }
        }

        private object InitializeStruct(Type type, StructAst structAst, ProgramGraph programGraph,
            IDictionary<string, ValueType> variables, List<AssignmentAst> values = null)
        {
            var assignments = values == null ? new Dictionary<string, AssignmentAst>() : values.ToDictionary(_ => (_.Variable as VariableAst)!.Name);
            var instance = Activator.CreateInstance(type);
            foreach (var field in structAst.Fields)
            {
                var fieldInstance = instance!.GetType().GetField(field.Name);

                if (assignments.TryGetValue(field.Name, out var assignment))
                {
                    var expression = ExecuteExpression(assignment.Value, programGraph, variables);
                    var value = CastValue(expression, field.Type);

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
                            var enumDef = (EnumAst)programGraph.Data.Types[structField.Name];
                            var value = enumDef.Values[structField.ValueIndex].Value;
                            var enumType = _types[structField.Name];
                            var enumInstance = Enum.ToObject(enumType, value);
                            fieldInstance!.SetValue(instance, enumInstance);
                            break;
                    }
                }
                else if (field.Type.Name == "*")
                {
                    // TODO Implement pointers
                }
                else if (field.Type.PrimitiveType == null)
                {
                    var fieldType = _types[field.Type.GenericName];
                    var fieldTypeDef = programGraph.Data.Types[field.Type.GenericName];
                    if (fieldTypeDef is StructAst fieldStructAst)
                    {
                        var value = InitializeStruct(fieldType, fieldStructAst, programGraph, variables);
                        fieldInstance!.SetValue(instance, value);
                    }
                    else
                    {
                        fieldInstance!.SetValue(instance, Activator.CreateInstance(fieldType));
                    }
                }
            }

            return instance;
        }

        private void ExecuteAssignment(AssignmentAst assignment, ProgramGraph programGraph, IDictionary<string, ValueType> variables)
        {
            var expression = ExecuteExpression(assignment.Value, programGraph, variables);
            if (assignment.Operator != Operator.None)
            {
                var lhs = ExecuteExpression(assignment.Variable, programGraph, variables);
                expression = BuildExpression(lhs, expression, assignment.Operator, lhs.Type);
            }

            switch (assignment.Variable)
            {
                case VariableAst variableAst:
                {
                    var variable = variables[variableAst.Name];
                    variable.Value = expression.Value;
                    break;
                }
                case StructFieldRefAst structField:
                {
                    var variable = variables[structField.Name];

                    var fieldObject = variable.Value;
                    var value = structField.Value;
                    FieldInfo field;
                    while (true)
                    {
                        field = fieldObject!.GetType().GetField(value.Name);
                        if (value.Value == null)
                        {
                            break;
                        }

                        fieldObject = field!.GetValue(fieldObject);
                        value = value.Value;
                    }
                    field!.SetValue(fieldObject, expression.Value);
                    break;
                }
                case IndexAst index:
                    switch (index.Variable)
                    {
                        // TODO Implement me
                        case VariableAst variableAst:
                        {
                            var variable = variables[variableAst.Name];
                            break;
                        }
                        case StructFieldRefAst structField:
                        {
                            var variable = variables[structField.Name];
                            break;
                        }
                    }
                    break;
            }
        }

        private ValueType ExecuteScope(List<IAst> asts, ProgramGraph programGraph,
            IDictionary<string, ValueType> variables, out bool returned)
        {
            var scopeVariables = new Dictionary<string, ValueType>(variables);

            return ExecuteAsts(asts, programGraph, scopeVariables, out returned);
        }

        private ValueType ExecuteConditional(ConditionalAst conditional, ProgramGraph programGraph,
            IDictionary<string, ValueType> variables, out bool returned)
        {
            if (ExecuteCondition(conditional.Condition, programGraph, variables))
            {
                return ExecuteScope(conditional.Children, programGraph, variables, out returned);
            }

            return ExecuteScope(conditional.Else, programGraph, variables, out returned);
        }

        private ValueType ExecuteWhile(WhileAst whileAst, ProgramGraph programGraph,
            IDictionary<string, ValueType> variables, out bool returned)
        {
            while (ExecuteCondition(whileAst.Condition, programGraph, variables))
            {
                var value = ExecuteScope(whileAst.Children, programGraph, variables, out returned);

                if (returned)
                {
                    return value;
                }
            }

            returned = false;
            return null;
        }

        private bool ExecuteCondition(IAst expression, ProgramGraph programGraph, IDictionary<string, ValueType> variables)
        {
            var valueType = ExecuteExpression(expression, programGraph, variables);
            var type = valueType.Type;
            var value = valueType.Value;
            return valueType.Type.PrimitiveType switch
            {
                IntegerType => (int)value != 0,
                FloatType => (float)value != 0f,
                _ when type.Name == "*" => (IntPtr)value != IntPtr.Zero,
                _ => (bool)value
            };
        }

        private ValueType ExecuteEach(EachAst each, ProgramGraph programGraph, IDictionary<string, ValueType> variables, out bool returned)
        {
            throw new NotImplementedException();
        }

        private ValueType ExecuteAsts(List<IAst> asts, ProgramGraph programGraph,
            IDictionary<string, ValueType> variables, out bool returned)
        {
            foreach (var ast in asts)
            {
                var value = ExecuteAst(ast, programGraph, variables, out returned);

                if (returned)
                {
                    return value;
                }
            }

            returned = false;
            return null;
        }

        private ValueType ExecuteExpression(IAst ast, ProgramGraph programGraph, IDictionary<string, ValueType> variables)
        {
            switch (ast)
            {
                case ConstantAst constant:
                    var type = constant.Type;
                    return new ValueType {Type = type, Value = GetConstant(type, constant.Value)};
                case NullAst:
                    return new ValueType();
                case StructFieldRefAst structField:
                    if (structField.IsEnum)
                    {
                        var enumDef = (EnumAst)programGraph.Data.Types[structField.Name];
                        var value = enumDef.Values[structField.ValueIndex].Value;
                        var enumType = _types[structField.Name];
                        var enumInstance = Enum.ToObject(enumType, value);
                        return new ValueType {Type = new TypeDefinition {Name = structField.Name}, Value = enumInstance};
                    }
                    var structVariable = variables[structField.Name];
                    return GetStructFieldRef(structField, programGraph, structVariable.Value);
                case VariableAst variableAst:
                    return variables[variableAst.Name];
                case ChangeByOneAst changeByOne:
                    switch (changeByOne.Variable)
                    {
                        case VariableAst variableAst:
                        {
                            var variable = variables[variableAst.Name];

                            var previousValue = new ValueType {Type = variable.Type, Value = variable.Value};

                            if (variable.Type.PrimitiveType is IntegerType)
                            {
                                var value = (int)variable.Value;
                                variable.Value = changeByOne.Positive ? value + 1 : value - 1;
                            }
                            else
                            {
                                var value = (float)variable.Value;
                                variable.Value = changeByOne.Positive ? value + 1 : value - 1;
                            }

                            return changeByOne.Prefix ? variable : previousValue;
                        }
                        case StructFieldRefAst structField:
                        {
                            var variable = variables[structField.Name];

                            var fieldObject = variable.Value;
                            var structFieldValue = structField.Value;
                            FieldInfo field;
                            TypeDefinition fieldType;
                            var structDefinition = (StructAst) programGraph.Data.Types[structField.StructName];
                            while (true)
                            {
                                field = fieldObject!.GetType().GetField(structFieldValue.Name);
                                if (structFieldValue.Value == null)
                                {
                                    fieldType = structDefinition.Fields[structFieldValue.ValueIndex].Type;
                                    break;
                                }

                                fieldObject = field!.GetValue(fieldObject);
                                structDefinition = (StructAst) programGraph.Data.Types[structFieldValue.StructName];
                                structFieldValue = structFieldValue.Value;
                            }

                            var previousValue = field!.GetValue(fieldObject);
                            object newValue;

                            if (fieldType.PrimitiveType is IntegerType)
                            {
                                var value = (int)previousValue!;
                                newValue = changeByOne.Positive ? value + 1 : value - 1;
                                field.SetValue(fieldObject, newValue);
                            }
                            else
                            {
                                var value = (float)previousValue!;
                                newValue = changeByOne.Positive ? value + 1 : value - 1;
                                field.SetValue(fieldObject, newValue);
                            }

                            return new ValueType {Type = fieldType, Value = changeByOne.Prefix ? newValue : previousValue};
                        }
                        case IndexAst index:
                            // TODO Implement me
                            switch (index.Variable)
                            {
                                case VariableAst indexVariable:
                                    break;
                                case StructFieldRefAst indexStructField:
                                    break;
                            }
                            return null;
                    }
                    break;
                case UnaryAst unary:
                    var valueType = ExecuteExpression(unary.Value, programGraph, variables);
                    switch (unary.Operator)
                    {
                        case UnaryOperator.Not:
                            var value = (bool)valueType.Value;
                            return new ValueType {Type = valueType.Type, Value = !value};
                        case UnaryOperator.Negate:
                            if (valueType.Type.PrimitiveType is IntegerType)
                            {
                                var intValue = (int)valueType.Value;
                                return new ValueType {Type = valueType.Type, Value = -intValue};
                            }
                            else
                            {
                                var floatValue = (float)valueType.Value;
                                return new ValueType {Type = valueType.Type, Value = -floatValue};
                            }
                        // TODO Implement pointers
                        case UnaryOperator.Dereference:
                            // return valueType.Generics[0];
                        case UnaryOperator.Reference:
                            // if (unary.Value is VariableAst || unary.Value is StructFieldRefAst || unary.Value is IndexAst || type == Type.Pointer)
                            // {
                            //     var pointerType = new TypeDefinition {Name = "*"};
                            //     pointerType.Generics.Add(valueType);
                            //     return pointerType;
                            // }
                            break;
                    }
                    break;
                case CallAst call:
                    var function = programGraph.Functions[call.Function];
                    if (call.Params)
                    {
                        // TODO Handle params
                    }

                    var arguments = call.Arguments.Select(arg => ExecuteExpression(arg, programGraph, variables).Value).ToArray();
                    if (function.Extern)
                    {
                        if (function.Varargs)
                        {
                            // TODO Create overloads for varargs functions
                        }

                        var functionDecl = _library.GetMethod(call.Function);
                        var returnValue = functionDecl!.Invoke(_functionObject, arguments);
                        return new ValueType {Type = function.ReturnType, Value = returnValue};
                    }
                    else
                    {
                        return CallFunction(function, programGraph, arguments);
                    }
                case ExpressionAst expression:
                    var expressionValue = ExecuteExpression(expression.Children[0], programGraph, variables);
                    for (var i = 1; i < expression.Children.Count; i++)
                    {
                        var rhs = ExecuteExpression(expression.Children[i], programGraph, variables);
                        expressionValue = BuildExpression(expressionValue, rhs, expression.Operators[i - 1], expression.ResultingTypes[i - 1]);
                    }
                    return expressionValue;
                case IndexAst index:
                    // return VerifyIndexType(index, localVariables, errors, out _);
                    break;
            }

            return null;
        }

        private static object GetConstant(TypeDefinition type, string value)
        {
            object result = type.PrimitiveType switch
            {
                IntegerType => int.Parse(value),
                FloatType => float.Parse(value),
                _ when type.Name == "bool" => value == "true",
                _ => value
            };
            return result;
        }

        private ValueType GetStructFieldRef(StructFieldRefAst structField, ProgramGraph programGraph,
            object structVariable)
        {
            var value = structField.Value;
            var structDefinition = (StructAst) programGraph.Data.Types[structField.StructName];

            var field = structVariable.GetType().GetField(value.Name);
            var fieldValue = field!.GetValue(structVariable);

            if (value.Value == null)
            {
                var fieldType = structDefinition.Fields[structField.ValueIndex].Type;
                return new ValueType {Type = fieldType, Value = fieldValue};
            }

            return GetStructFieldRef(value, programGraph, fieldValue);
        }

        private ValueType CallFunction(FunctionAst function, ProgramGraph programGraph, object[] arguments)
        {
            var variables = new Dictionary<string, ValueType>(_globalVariables);

            for (var i = 0; i < function.Arguments.Count; i++)
            {
                var arg = function.Arguments[i];
                variables[arg.Name] = new ValueType {Type = arg.Type, Value = arguments[i]};
            }

            foreach (var ast in function.Children)
            {
                var value = ExecuteAst(ast, programGraph, variables, out var returned);

                if (returned)
                {
                    return value;
                }
            }

            return null;
        }

        private ValueType BuildExpression(ValueType lhs, ValueType rhs, Operator op, TypeDefinition targetType)
        {
            // TODO Implement pointers
            // 1. Handle pointer math 
            // if (lhs.Type.Name == "*")
            // {
            //     return BuildPointerOperation(lhs.value, rhs.value, op);
            // }
            // if (rhs.Type.Name == "*")
            // {
            //     return BuildPointerOperation(rhs.value, lhs.value, op);
            // }

            // 2. Handle simple operators like && and ||
            if (op == Operator.And || op == Operator.Or)
            {
                var lhsBool = (bool)lhs.Value;
                var rhsBool = (bool)rhs.Value;
                return new ValueType
                {
                    Type = targetType, Value = op == Operator.And ? lhsBool && rhsBool : lhsBool || rhsBool
                };
            }

            // 3. Handle compares, since the lhs and rhs should not be cast to the target type 
            switch (op)
            {
                case Operator.Equality:
                case Operator.NotEqual:
                case Operator.GreaterThanEqual:
                case Operator.LessThanEqual:
                case Operator.GreaterThan:
                case Operator.LessThan:
                    return new ValueType {Type = targetType, Value = Compare(lhs, rhs, op)};
            }

            // 4. Cast lhs and rhs to the target types
            var lhsValue = CastValue(lhs, targetType);
            var rhsValue = CastValue(rhs, targetType);

            // 5. Handle the rest of the simple operators
            switch (op)
            {
                case Operator.BitwiseAnd:
                case Operator.BitwiseOr:
                case Operator.Xor:
                    // TODO Implement me
                    return new ValueType {Type = targetType, Value = null};
            }

            // 6. Handle binary operations
            var signed = lhs.Type.PrimitiveType.Signed || rhs.Type.PrimitiveType.Signed;
            return PerformOperation(targetType, lhsValue, rhsValue, op, signed);
        }

        private object Compare(ValueType lhs, ValueType rhs, Operator op)
        {
            // TODO Implement me
            return true;
        }

        private ValueType PerformOperation(TypeDefinition targetType, object lhsValue, object rhsValue, Operator op, bool signed)
        {
            // TODO Implement me
            return new() {Type = targetType, Value = null};
        }

        private object CastValue(ValueType expression, TypeDefinition targetType)
        {
            // TODO Implement me
            return expression.Value;
        }

        private static void CreateFunction(TypeBuilder typeBuilder, string name, string library, Type returnType, params Type[] args)
        {
            var method = typeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, args);
            var caBuilder = new CustomAttributeBuilder(typeof(DllImportAttribute).GetConstructor(new []{typeof(string)}), new []{library});
            method.SetCustomAttribute(caBuilder);
        }

        private Type GetTypeFromDefinition(TypeDefinition typeDef)
        {
            switch (typeDef.PrimitiveType)
            {
                case IntegerType integerType:
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
                case FloatType floatType:
                    return floatType.Bytes == 4 ? typeof(float) : typeof(double);
            }

            switch (typeDef.Name)
            {
                case "bool":
                    return typeof(bool);
                case "string":
                    return typeof(string);
                case "*":
                    return typeof(IntPtr);
            }

            return _types.TryGetValue(typeDef.GenericName, out var type) ? type : null;
        }
    }
}
