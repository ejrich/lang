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
        private Type library;
        private object functionObject;

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

            library = typeBuilder.CreateType();
            functionObject = library!.GetConstructor(Type.EmptyTypes)!.Invoke(new object[]{});

            // TODO Initialize using global variables
            var variables = new Dictionary<string, (TypeDefinition type, object value)>();
            foreach (var directive in programGraph.Directives)
            {
                switch (directive.Type)
                {
                    case DirectiveType.Run:
                        ExecuteAst(directive.Value, programGraph, variables);
                        break;
                    case DirectiveType.If:
                        // TODO Evaluate the condition
                        break;
                }
            }
        }

        private bool ExecuteAst(IAst ast, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            switch (ast)
            {
                case ReturnAst returnAst:
                    ExecuteReturn(returnAst, programGraph, variables);
                    return true;
                case DeclarationAst declaration:
                    ExecuteDeclaration(declaration, programGraph, variables);
                    break;
                case AssignmentAst assignment:
                    ExecuteAssignment(assignment, programGraph, variables);
                    break;
                case ScopeAst scope:
                    return ExecuteScope(scope.Children, programGraph, variables);
                case ConditionalAst conditional:
                    return ExecuteConditional(conditional, programGraph, variables);
                case WhileAst whileAst:
                    return ExecuteWhile(whileAst, programGraph, variables);
                case EachAst each:
                    return ExecuteEach(each, programGraph, variables);
                default:
                    ExecuteExpression(ast, programGraph, variables);
                    break;
            }
            return false;
        }

        private void ExecuteReturn(ReturnAst returnAst, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            throw new NotImplementedException();
        }

        private void ExecuteDeclaration(DeclarationAst declaration, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            throw new NotImplementedException();
        }

        private void ExecuteAssignment(AssignmentAst assignment, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            throw new NotImplementedException();
        }

        private bool ExecuteScope(List<IAst> scopeChildren, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            throw new NotImplementedException();
        }

        private bool ExecuteConditional(ConditionalAst conditional, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            throw new NotImplementedException();
        }

        private bool ExecuteWhile(WhileAst whileAst, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            throw new NotImplementedException();
        }

        private bool ExecuteEach(EachAst each, ProgramGraph programGraph, IDictionary<string, (TypeDefinition type, object value)> variables)
        {
            throw new NotImplementedException();
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
                    // return null;
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
                case VariableAst variable:
                    // if (localVariables.TryGetValue(variable.Name, out var typeDefinition))
                    // return typeDefinition;
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

                        var functionDecl = library.GetMethod(call.Function);
                        var returnValue = functionDecl!.Invoke(functionObject, arguments);
                        return (function.ReturnType, returnValue);
                    }
                    else
                    {
                        // TODO Make calls to other user-defined functions
                    }
                    break;
                case ExpressionAst expression:
                    // return VerifyExpressionType(expression, localVariables, errors);
                case IndexAst index:
                    // return VerifyIndexType(index, localVariables, errors, out _);
                    break;
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
