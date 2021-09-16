using System.Collections.Generic;

namespace Lang
{
    public interface IPolymorpher
    {
        StructAst CreatePolymorphedStruct(StructAst baseStruct, string name, TypeKind typeKind, int typeIndex, params TypeDefinition[] genericTypes);
        FunctionAst CreatePolymorphedFunction(FunctionAst baseFunction, string name, int typeIndex, TypeDefinition[] genericTypes);
        OperatorOverloadAst CreatePolymorphedOperatorOverload(OperatorOverloadAst baseOverload, TypeDefinition[] genericTypes);
    }

    public class Polymorpher : IPolymorpher
    {
        public StructAst CreatePolymorphedStruct(StructAst baseStruct, string name, TypeKind typeKind, int typeIndex, params TypeDefinition[] genericTypes)
        {
            var polyStruct = CopyAst(baseStruct);
            polyStruct.Name = name;
            polyStruct.TypeIndex = typeIndex;
            polyStruct.TypeKind = typeKind;

            foreach (var field in baseStruct.Fields)
            {
                if (field.HasGeneric)
                {
                    var newField = CopyAst(field);
                    newField.Type = CopyType(field.Type, genericTypes);
                    newField.Name = field.Name;
                    newField.DefaultValue = field.DefaultValue;
                    polyStruct.Fields.Add(newField);
                }
                else
                {
                    polyStruct.Fields.Add(field);
                }
            }

            return polyStruct;
        }

        public FunctionAst CreatePolymorphedFunction(FunctionAst baseFunction, string name, int typeIndex, TypeDefinition[] genericTypes)
        {
            var function = CopyAst(baseFunction);
            function.Name = name;
            function.TypeIndex = typeIndex;
            function.HasDirectives = baseFunction.HasDirectives;
            function.Params = baseFunction.Params;
            function.ReturnType = baseFunction.ReturnTypeHasGenerics ? CopyType(baseFunction.ReturnType, genericTypes) : baseFunction.ReturnType;

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

            CopyAsts(function.Children, baseFunction.Children, genericTypes, baseFunction.Generics);

            return function;
        }

        public OperatorOverloadAst CreatePolymorphedOperatorOverload(OperatorOverloadAst baseOverload, TypeDefinition[] genericTypes)
        {
            var overload = CopyAst(baseOverload);
            overload.Operator = baseOverload.Operator;
            overload.Type = CopyType(baseOverload.Type, genericTypes);
            overload.HasDirectives = baseOverload.HasDirectives;
            overload.ReturnType = baseOverload.ReturnTypeHasGenerics ? CopyType(baseOverload.ReturnType, genericTypes) : baseOverload.ReturnType;

            foreach (var argument in baseOverload.Arguments)
            {
                overload.Arguments.Add(CopyDeclaration(argument, genericTypes, baseOverload.Generics));
            }

            CopyAsts(overload.Children, baseOverload.Children, genericTypes, baseOverload.Generics);

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
            copy.Type = declaration.HasGenerics ? CopyType(declaration.Type, genericTypes) : declaration.Type;
            copy.Value = CopyExpression(declaration.Value, genericTypes, generics);
            foreach (var assignment in declaration.Assignments)
            {
                copy.Assignments.Add(CopyAssignment(assignment, genericTypes, generics));
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
            CopyAsts(copy.Children, conditional.Children, genericTypes, generics);
            CopyAsts(copy.Else, conditional.Else, genericTypes, generics);
            return copy;
        }

        private WhileAst CopyWhile(WhileAst whileAst, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(whileAst);
            copy.Condition = CopyExpression(whileAst.Condition, genericTypes, generics);
            CopyAsts(copy.Children, whileAst.Children, genericTypes, generics);
            return copy;
        }

        private EachAst CopyEach(EachAst each, TypeDefinition[] genericTypes, List<string> generics)
        {
            var copy = CopyAst(each);
            copy.IterationVariable = each.IterationVariable;
            copy.Iteration = CopyExpression(each.Iteration, genericTypes, generics);
            copy.RangeBegin = CopyExpression(each.RangeBegin, genericTypes, generics);
            copy.RangeEnd = CopyExpression(each.RangeEnd, genericTypes, generics);
            CopyAsts(copy.Children, each.Children, genericTypes, generics);
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
                    callCopy.Function = call.Function;
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
                    castCopy.TargetType = cast.HasGenerics ? CopyType(cast.TargetType, genericTypes) : cast.TargetType;
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
