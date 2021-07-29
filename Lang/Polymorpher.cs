using System.Collections.Generic;
using System.Linq;

namespace Lang
{
    public interface IPolymorpher
    {
        StructAst CreatePolymorphedStruct(StructAst baseStruct, string name, TypeKind typeKind, params TypeDefinition[] genericTypes);
        FunctionAst CreatePolymorphedFunction(FunctionAst baseFunction, string name, TypeDefinition[] genericTypes);
        OperatorOverloadAst CreatePolymorphedOperatorOverload(OperatorOverloadAst baseOverload, TypeDefinition[] genericTypes);
    }

    public class Polymorpher : IPolymorpher
    {
        public StructAst CreatePolymorphedStruct(StructAst baseStruct, string name, TypeKind typeKind, params TypeDefinition[] genericTypes)
        {
            var polyStruct = CopyAst(baseStruct);
            polyStruct.Name = name;
            polyStruct.TypeKind = typeKind;

            foreach (var field in baseStruct.Fields)
            {
                if (field.HasGenerics)
                {
                    var newField = CopyAst(field);
                    newField.TypeDefinition = CopyType(field.TypeDefinition, genericTypes);
                    newField.Name = field.Name;
                    newField.Value = field.Value;
                    newField.Assignments = field.Assignments;
                    newField.ArrayValues = field.ArrayValues;
                    polyStruct.Fields.Add(newField);
                }
                else
                {
                    polyStruct.Fields.Add(field);
                }
            }

            return polyStruct;
        }

        public FunctionAst CreatePolymorphedFunction(FunctionAst baseFunction, string name, TypeDefinition[] genericTypes)
        {
            var function = CopyAst(baseFunction);
            function.Name = name;
            function.HasDirectives = baseFunction.HasDirectives;
            function.Params = baseFunction.Params;
            function.ReturnTypeDefinition = baseFunction.ReturnTypeHasGenerics ? CopyType(baseFunction.ReturnTypeDefinition, genericTypes) : baseFunction.ReturnTypeDefinition;

            foreach (var argument in baseFunction.Arguments)
            {
                if (argument.HasGenerics)
                {
                    function.Arguments.Add(CopyDeclaration(argument, genericTypes, baseFunction.Generics));
                }
                else
                {
                    function.Arguments.Add(argument);
                }
            }

            function.Body = CopyScope(baseFunction.Body, genericTypes, baseFunction.Generics);

            return function;
        }

        public OperatorOverloadAst CreatePolymorphedOperatorOverload(OperatorOverloadAst baseOverload, TypeDefinition[] genericTypes)
        {
            var overload = CopyAst(baseOverload);
            overload.Operator = baseOverload.Operator;
            overload.Type = CopyType(baseOverload.Type, genericTypes);
            overload.HasDirectives = baseOverload.HasDirectives;
            overload.ReturnTypeDefinition = baseOverload.ReturnTypeHasGenerics ? CopyType(baseOverload.ReturnTypeDefinition, genericTypes) : baseOverload.ReturnTypeDefinition;

            foreach (var argument in baseOverload.Arguments)
            {
                overload.Arguments.Add(CopyDeclaration(argument, genericTypes, baseOverload.Generics));
            }

            overload.Body = CopyScope(baseOverload.Body, genericTypes, baseOverload.Generics);

            return overload;
        }

        private TypeDefinition CopyType(TypeDefinition type, TypeDefinition[] genericTypes)
        {
            if (type.IsGeneric)
            {
                return genericTypes[type.GenericIndex];
            }

            var copyType = CopyAst(type);
            copyType.Name = type.Name;
            copyType.PrimitiveType = type.PrimitiveType;
            copyType.Count = type.Count;

            foreach (var generic in type.Generics)
            {
                copyType.Generics.Add(CopyType(generic, genericTypes));
            }

            return copyType;
        }

        private void CopyAsts(List<IAst> parent, List<IAst> baseAsts, TypeDefinition[] genericTypes, List<string> generics)
        {
            foreach (var ast in baseAsts)
            {
                parent.Add(CopyAst(ast, genericTypes, generics));
            }
        }

        private IAst CopyAst(IAst ast, TypeDefinition[] genericTypes, List<string> generics)
        {
            switch (ast)
            {
                case ReturnAst returnAst:
                    return CopyReturn(returnAst, genericTypes, generics);
                case DeclarationAst declaration:
                    return CopyDeclaration(declaration, genericTypes, generics);
                case AssignmentAst assignment:
                    return CopyAssignment(assignment, genericTypes, generics);
                case ScopeAst scope:
                    return CopyScope(scope, genericTypes, generics);
                case ConditionalAst conditional:
                    return CopyConditional(conditional, genericTypes, generics);
                case WhileAst whileAst:
                    return CopyWhile(whileAst, genericTypes, generics);
                case EachAst each:
                    return CopyEach(each, genericTypes, generics);
                case CompilerDirectiveAst compilerDirective:
                    return CopyCompilerDirective(compilerDirective, genericTypes, generics);
                default:
                    return CopyExpression(ast, genericTypes, generics);
            }
        }

        private ReturnAst CopyReturn(ReturnAst returnAst, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(returnAst);
            copy.Value = CopyExpression(returnAst.Value, genericTypes, generics);
            return copy;
        }

        private DeclarationAst CopyDeclaration(DeclarationAst declaration, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(declaration);
            copy.Name = declaration.Name;
            copy.Constant = declaration.Constant;
            copy.TypeDefinition = declaration.HasGenerics ? CopyType(declaration.TypeDefinition, genericTypes) : declaration.TypeDefinition;
            copy.Value = CopyExpression(declaration.Value, genericTypes, generics);

            if (declaration.Assignments != null)
            {
                copy.Assignments = new();
                foreach (var (name, assignment) in declaration.Assignments)
                {
                    copy.Assignments[name] = CopyAssignment(assignment, genericTypes, generics);
                }
            }
            else if (declaration.ArrayValues != null)
            {
                copy.ArrayValues = declaration.ArrayValues.Select(value => CopyExpression(value, genericTypes, generics)).ToList();
            }

            return copy;
        }

        private AssignmentAst CopyAssignment(AssignmentAst assignment, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(assignment);
            copy.Reference = assignment.Reference;
            copy.Operator = assignment.Operator;
            copy.Value = CopyExpression(assignment.Value, genericTypes, generics);
            return copy;
        }

        private ScopeAst CopyScope(ScopeAst scope, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(scope);
            CopyAsts(copy.Children, scope.Children, genericTypes, generics);
            return copy;
        }

        private ConditionalAst CopyConditional(ConditionalAst conditional, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(conditional);
            copy.Condition = CopyExpression(conditional.Condition, genericTypes, generics);
            copy.IfBlock = CopyScope(conditional.IfBlock, genericTypes, generics);
            if (conditional.ElseBlock != null)
            {
                copy.ElseBlock = CopyScope(conditional.ElseBlock, genericTypes, generics);
            }
            return copy;
        }

        private WhileAst CopyWhile(WhileAst whileAst, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(whileAst);
            copy.Condition = CopyExpression(whileAst.Condition, genericTypes, generics);
            copy.Body = CopyScope(whileAst.Body, genericTypes, generics);
            return copy;
        }

        private EachAst CopyEach(EachAst each, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(each);
            copy.IterationVariable = each.IterationVariable;
            copy.IndexVariable = each.IndexVariable;
            copy.Iteration = CopyExpression(each.Iteration, genericTypes, generics);
            copy.RangeBegin = CopyExpression(each.RangeBegin, genericTypes, generics);
            copy.RangeEnd = CopyExpression(each.RangeEnd, genericTypes, generics);
            copy.Body = CopyScope(each.Body, genericTypes, generics);
            return copy;
        }

        private CompilerDirectiveAst CopyCompilerDirective(CompilerDirectiveAst compilerDirective, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(compilerDirective);
            copy.Type = compilerDirective.Type;
            copy.Value = CopyAst(compilerDirective.Value, genericTypes, generics);
            return copy;
        }

        private IAst CopyExpression(IAst ast, TypeDefinition[] genericTypes, List<string> generics)
        {
            switch (ast)
            {
                case ConstantAst:
                case NullAst:
                    return ast;
                case StructFieldRefAst structField:
                    var structFieldCopy = CopyAst(structField);
                    foreach (var child in structField.Children)
                    {
                        if (child is IdentifierAst)
                        {
                            structFieldCopy.Children.Add(child);
                        }
                        else
                        {
                            structFieldCopy.Children.Add(CopyExpression(child, genericTypes, generics));
                        }
                    }
                    return structFieldCopy;
                case IdentifierAst identifier:
                    for (var i = 0; i < generics.Count; i++)
                    {
                        if (generics[i] == identifier.Name)
                        {
                            // @Robustness Should this copy the file and line info?
                            return genericTypes[i];
                        }
                    }
                    return identifier;
                case ChangeByOneAst changeByOne:
                    var changeByOneCopy = CopyAst(changeByOne);
                    changeByOneCopy.Prefix = changeByOne.Prefix;
                    changeByOneCopy.Positive = changeByOne.Positive;
                    changeByOneCopy.Value = CopyExpression(changeByOne.Value, genericTypes, generics);
                    return changeByOneCopy;
                case UnaryAst unary:
                    var unaryCopy = CopyAst(unary);
                    unaryCopy.Operator = unary.Operator;
                    unaryCopy.Value = CopyExpression(unary.Value, genericTypes, generics);
                    return unaryCopy;
                case CallAst call:
                    var callCopy = CopyAst(call);
                    callCopy.FunctionName = call.FunctionName;
                    if (call.SpecifiedArguments != null)
                    {
                        callCopy.SpecifiedArguments = new Dictionary<string, IAst>();
                        foreach (var (name, argument) in call.SpecifiedArguments)
                        {
                            callCopy.SpecifiedArguments[name] = CopyExpression(argument, genericTypes, generics);
                        }
                    }
                    foreach (var argument in call.Arguments)
                    {
                        callCopy.Arguments.Add(CopyExpression(argument, genericTypes, generics));
                    }
                    return callCopy;
                case ExpressionAst expression:
                    var expressionCopy = CopyAst(expression);
                    expressionCopy.Operators.AddRange(expression.Operators);
                    foreach (var childAst in expression.Children)
                    {
                        expressionCopy.Children.Add(CopyExpression(childAst, genericTypes, generics));
                    }
                    return expressionCopy;
                case IndexAst index:
                    var indexCopy = CopyAst(index);
                    indexCopy.Name = index.Name;
                    indexCopy.Index = CopyExpression(index.Index, genericTypes, generics);
                    return indexCopy;
                case TypeDefinition typeDef:
                    return CopyType(typeDef, genericTypes);
                case CastAst cast:
                    var castCopy = CopyAst(cast);
                    castCopy.TargetTypeDefinition = cast.HasGenerics ? CopyType(cast.TargetTypeDefinition, genericTypes) : cast.TargetTypeDefinition;
                    castCopy.Value = CopyExpression(cast.Value, genericTypes, generics);
                    return castCopy;
                default:
                    return null;
            }
        }

        private T CopyAst<T>(T ast) where T : IAst, new()
        {
            return new()
            {
                FileIndex = ast.FileIndex,
                Line = ast.Line,
                Column = ast.Column
            };
        }
    }
}
