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
        private Dictionary<string, (TypeDefinition type, object value)> _globalVariables = new();

        public void RunProgram(ProgramGraph programGraph)
        {
            if (!programGraph.Directives.Any()) return;

            var name = new AssemblyName("ExternFunctions");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
            var modelBuilder = assemblyBuilder.DefineDynamicModule("ExternFunctions");
            var typeBuilder = modelBuilder.DefineType("Functions", TypeAttributes.Class | TypeAttributes.Public);

            foreach (var types in programGraph.Data.Types)
            {
                // TODO Make structs as the types
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
            _functionObject = _library!.GetConstructor(Type.EmptyTypes)!.Invoke(new object[]{});

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

        private (TypeDefinition type, object value) ExecuteAst(IAst ast, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables, out bool returned)
        {
            returned = false;
            (TypeDefinition type, object value) returnValue = (null, null);
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

        private (TypeDefinition type, object value) ExecuteReturn(ReturnAst returnAst, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            return ExecuteExpression(returnAst.Value, programGraph, variables);
        }

        private void ExecuteDeclaration(DeclarationAst declaration, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            var value = declaration.Value == null ? GetUninitializedValue(declaration.Type) :
                ExecuteExpression(declaration.Value, programGraph, null).value;

            _globalVariables[declaration.Name] = (declaration.Type, value);
        }

        private static object GetUninitializedValue(TypeDefinition typeDef)
        {
            return typeDef.PrimitiveType switch
            {
                IntegerType => 0,
                FloatType floatType => floatType.Bytes == 4 ? 0f : 0.0,
                _ => 0 // TODO Handle more types
            };
        }

        private void ExecuteAssignment(AssignmentAst assignment, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            throw new NotImplementedException();
        }

        private (TypeDefinition type, object value) ExecuteScope(List<IAst> asts, ProgramGraph programGraph,
            IDictionary<string, (TypeDefinition type, object value)> variables, out bool returned)
        {
            var scopeVariables = new Dictionary<string, (TypeDefinition type, object value)>(variables);

            return ExecuteAsts(asts, programGraph, scopeVariables, out returned);
        }

        private (TypeDefinition type, object value) ExecuteConditional(ConditionalAst conditional, ProgramGraph programGraph,
            IDictionary<string, (TypeDefinition type, object value)> variables, out bool returned)
        {
            if (ExecuteCondition(conditional.Condition, programGraph, variables))
            {
                return ExecuteScope(conditional.Children, programGraph, variables, out returned);
            }

            return ExecuteScope(conditional.Else, programGraph, variables, out returned);
        }

        private (TypeDefinition type, object value) ExecuteWhile(WhileAst whileAst, ProgramGraph programGraph,
            IDictionary<string, (TypeDefinition type, object value)> variables, out bool returned)
        {
            throw new NotImplementedException();
        }

        private bool ExecuteCondition(IAst conditionExpression, ProgramGraph programGraph, 
            IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            throw new NotImplementedException();
        }

        private (TypeDefinition type, object value) ExecuteEach(EachAst each, ProgramGraph programGraph,
            IDictionary<string, (TypeDefinition type, object value)> variables, out bool returned)
        {
            throw new NotImplementedException();
        }

        private (TypeDefinition type, object value) ExecuteAsts(List<IAst> asts, ProgramGraph programGraph,
            IDictionary<string, (TypeDefinition type, object value)> variables, out bool returned)
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
            return (null, null);
        }

        private (TypeDefinition type, object value) ExecuteExpression(IAst ast, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
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
                    return (type, value);
                case NullAst:
                    return (null, null);
                case StructFieldRefAst structField:
                    // if (!localVariables.TryGetValue(structField.Name, out var structType))
                    // {
                    //     if (_types.TryGetValue(structField.Name, out var type))
                    //     {
                    //         if (type is EnumAst enumAst)
                    //         {
                    //             structField.IsEnum = true;
                    //             return VerifyEnumValue(structField, enumAst, errors);
                    //         }
                    //         return null;
                    //     }
                    //     return null;
                    // }
                    // return VerifyStructFieldRef(structField, structType, errors);
                    break;
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

                    var arguments = call.Arguments.Select(arg => ExecuteExpression(arg, programGraph, variables).value).ToArray();
                    if (function.Extern)
                    {
                        if (function.Varargs)
                        {
                            // TODO Create overloads for varargs functions
                        }

                        var functionDecl = _library.GetMethod(call.Function);
                        var returnValue = functionDecl!.Invoke(_functionObject, arguments);
                        return (function.ReturnType, returnValue);
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

            return (null, null);
        }

        private (TypeDefinition type, object value) CallFunction(FunctionAst function, ProgramGraph programGraph, object[] arguments)
        {
            var variables = new Dictionary<string, (TypeDefinition type, object value)>(_globalVariables);

            for (var i = 0; i < function.Arguments.Count; i++)
            {
                var arg = function.Arguments[i];
                variables[arg.Name] = (arg.Type, arguments[i]);
            }

            foreach (var ast in function.Children)
            {
                var value = ExecuteAst(ast, programGraph, variables, out var returned);

                if (returned)
                {
                    return value;
                }
            }

            return (null, null);
        }

        private static void CreateFunction(TypeBuilder typeBuilder, string name, string library, Type returnType, params Type[] args)
        {
            var method = typeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, args);
            var caBuilder = new CustomAttributeBuilder(typeof(DllImportAttribute).GetConstructor(new []{typeof(string)}), new []{library});
            method.SetCustomAttribute(caBuilder);
        }

        private static Type GetTypeFromDefinition(TypeDefinition typeDef)
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
            }

            // TODO Handle more types
            return null;
        }
    }
}
