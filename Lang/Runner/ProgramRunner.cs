﻿using System;
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
            var value = declaration.Value == null ? GetUninitializedValue(declaration.Type) :
                ExecuteExpression(declaration.Value, programGraph, null).Value;

            variables[declaration.Name] = new ValueType {Type = declaration.Type, Value = value};
        }

        private object GetUninitializedValue(TypeDefinition typeDef)
        {
            switch (typeDef.PrimitiveType)
            {
                case IntegerType integerType:
                    return 0;
                case FloatType floatType:
                    return floatType.Bytes == 4 ? 0f : 0.0;
                default:
                    // TODO Handle more types
                    var type = _types[typeDef.GenericName];
                    return Activator.CreateInstance(type);
            }
        }

        private void ExecuteAssignment(AssignmentAst assignment, ProgramGraph programGraph, IDictionary<string, ValueType> variables)
        {
            var valueType = assignment.Variable switch
            {
                VariableAst variable => variables[variable.Name],
                // TODO Implement StructFieldRef and Index ASTs
                _ => null
            };

            var expression = ExecuteExpression(assignment.Value, programGraph, variables);
            if (assignment.Operator != Operator.None)
            {
                // TODO Implement this
            }

            // TODO This doesn't work, need to get the pointer
            valueType!.Value = expression.Value;
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
                    object value = type.PrimitiveType switch
                    {
                        IntegerType => int.Parse(constant.Value),
                        FloatType => float.Parse(constant.Value),
                        _ => constant.Value
                    };
                    return new ValueType {Type = type, Value = value};
                case NullAst:
                    return null;
                case StructFieldRefAst structField:
                    var structVariable = variables[structField.Name];
                    return GetStructFieldRef(structField, programGraph, structVariable.Value);
                case VariableAst variable:
                    return variables[variable.Name];
                case ChangeByOneAst changeByOne:
                    // switch (changeByOne.Variable)
                    // {
                    //     case VariableAst variable:
                    //         if (localVariables.TryGetValue(variable.Name, out var variableType))
                    //         {
                    //             var type = VerifyType(variableType, errors);
                    //             if (type == Type.Int || type == Type.Float) return variableType;
                    //         }
                    //     case StructFieldRefAst structField:
                    //         if (localVariables.TryGetValue(structField.Name, out var structType))
                    //         {
                    //             var fieldType = VerifyStructFieldRef(structField, structType, errors);
                    //             if (fieldType == null) return null;
                    //
                    //             var type = VerifyType(fieldType, errors);
                    //             if (type == Type.Int || type == Type.Float) return fieldType;
                    //         }
                    //     case IndexAst index:
                    //         var indexType = VerifyIndexType(index, localVariables, errors, out var variableAst);
                    //         if (indexType != null)
                    //         {
                    //             var type = VerifyType(indexType, errors);
                    //             if (type == Type.Int || type == Type.Float) return indexType;
                    //         }
                    //         return null;
                    // }
                case UnaryAst unary:
                    // var valueType = VerifyExpression(unary.Value, localVariables, errors);
                    // var type = VerifyType(valueType, errors);
                    // switch (unary.Operator)
                    // {
                    //     case UnaryOperator.Not:
                    //         if (type == Type.Boolean)
                    //         {
                    //             return valueType;
                    //         }
                    //     case UnaryOperator.Negate:
                    //         if (type == Type.Int || type == Type.Float)
                    //         {
                    //             return valueType;
                    //         }
                    //     case UnaryOperator.Dereference:
                    //         if (type == Type.Pointer)
                    //         {
                    //             return valueType.Generics[0];
                    //         }
                    //     case UnaryOperator.Reference:
                    //         if (unary.Value is VariableAst || unary.Value is StructFieldRefAst || unary.Value is IndexAst || type == Type.Pointer)
                    //         {
                    //             var pointerType = new TypeDefinition {Name = "*"};
                    //             pointerType.Generics.Add(valueType);
                    //             return pointerType;
                    //         }
                    // }
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
                    // return VerifyExpressionType(expression, localVariables, errors);
                case IndexAst index:
                    // return VerifyIndexType(index, localVariables, errors, out _);
                    break;
            }

            return null;
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
                case "string":
                    return typeof(string);
                case "*":
                    return typeof(IntPtr);
            }

            return _types.TryGetValue(typeDef.GenericName, out var type) ? type : null;
        }
    }
}
