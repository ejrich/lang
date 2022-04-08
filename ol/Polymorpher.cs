using System;
using System.Collections.Generic;
using System.Linq;

namespace ol;

public static class Polymorpher
{
    public static StructAst CreatePolymorphedStruct(StructAst baseStruct, string name, String backendName, TypeKind typeKind, bool privateGenericTypes, params IType[] genericTypes)
    {
        var polyStruct = new StructAst
        {
            FileIndex = baseStruct.FileIndex, Line = baseStruct.Line, Column = baseStruct.Column, Name = Allocator.ReserveString(name),
            BackendName = backendName, TypeKind = typeKind, Private = baseStruct.Private || privateGenericTypes,
            BaseStructName = baseStruct.Name, BaseTypeDefinition = baseStruct.BaseTypeDefinition,
            BaseStruct = baseStruct.BaseStruct, GenericTypes = genericTypes
        };

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

    public static FunctionAst CreatePolymorphedFunction(FunctionAst baseFunction, string name, bool privateGenericTypes, IType[] genericTypes)
    {
        var function = CopyAst(baseFunction);
        function.Flags = baseFunction.Flags;
        function.Name = Allocator.ReserveString(name);
        function.Private = baseFunction.Private || privateGenericTypes;
        function.ArgumentCount = baseFunction.ArgumentCount;

        if (baseFunction.Flags.Has(FunctionFlags.ReturnTypeHasGenerics))
        {
            function.ReturnTypeDefinition = CopyType(baseFunction.ReturnTypeDefinition, genericTypes);
        }
        else
        {
            function.ReturnType = baseFunction.ReturnType;
        }

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

        function.Body = Polymorpher.CopyScope(baseFunction.Body, genericTypes, baseFunction.Generics);

        return function;
    }

    public static OperatorOverloadAst CreatePolymorphedOperatorOverload(OperatorOverloadAst baseOverload, IType[] genericTypes)
    {
        var overload = CopyAst(baseOverload);
        overload.Operator = baseOverload.Operator;
        overload.Type = CopyType(baseOverload.Type, genericTypes);
        overload.FunctionIndex = TypeTable.GetFunctionIndex();
        overload.Flags = baseOverload.Flags;
        overload.ReturnTypeDefinition = baseOverload.Flags.Has(FunctionFlags.ReturnTypeHasGenerics) ? CopyType(baseOverload.ReturnTypeDefinition, genericTypes) : baseOverload.ReturnTypeDefinition;

        Span<char> typeName = stackalloc char[overload.Type.GenericNameLength];
        overload.Type.WriteGenericName(typeName);
        overload.Name = $"operator.{overload.Operator}.{typeName}";

        foreach (var argument in baseOverload.Arguments)
        {
            overload.Arguments.Add(CopyDeclaration(argument, genericTypes, baseOverload.Generics));
        }

        overload.Body = CopyScope(baseOverload.Body, genericTypes, baseOverload.Generics);

        return overload;
    }

    public static TypeDefinition CopyType(TypeDefinition type, IType[] genericTypes)
    {
        var copyType = CopyAst(type);

        if (type.IsGeneric)
        {
            var genericType = genericTypes[type.GenericIndex];
            copyType.Name = genericType.BackendName;
            copyType.BakedType = genericType;
        }
        else
        {
            copyType.Name = type.Name;
            copyType.Compound = type.Compound;
            copyType.Count = type.Count;

            foreach (var generic in type.Generics)
            {
                copyType.Generics.Add(CopyType(generic, genericTypes));
            }
        }

        return copyType;
    }

    private static void CopyAsts(List<IAst> parent, List<IAst> baseAsts, IType[] genericTypes, List<String> generics)
    {
        foreach (var ast in baseAsts)
        {
            parent.Add(CopyAst(ast, genericTypes, generics));
        }
    }

    private static IAst CopyAst(IAst ast, IType[] genericTypes, List<String> generics)
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

    private static ReturnAst CopyReturn(ReturnAst returnAst, IType[] genericTypes, List<String> generics)
    {
        var copy = CopyAst(returnAst);
        copy.Value = CopyExpression(returnAst.Value, genericTypes, generics);
        return copy;
    }

    private static DeclarationAst CopyDeclaration(DeclarationAst declaration, IType[] genericTypes, List<String> generics)
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

    private static AssignmentAst CopyAssignment(AssignmentAst assignment, IType[] genericTypes, List<String> generics)
    {
        var copy = CopyAst(assignment);
        copy.Reference = CopyExpression(assignment.Reference, genericTypes, generics);
        copy.Operator = assignment.Operator;
        copy.Value = CopyExpression(assignment.Value, genericTypes, generics);
        return copy;
    }

    public static ScopeAst CopyScope(ScopeAst scope, IType[] genericTypes, List<String> generics)
    {
        var copy = CopyAst(scope);
        CopyAsts(copy.Children, scope.Children, genericTypes, generics);
        return copy;
    }

    private static ConditionalAst CopyConditional(ConditionalAst conditional, IType[] genericTypes, List<String> generics)
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

    private static WhileAst CopyWhile(WhileAst whileAst, IType[] genericTypes, List<String> generics)
    {
        var copy = CopyAst(whileAst);
        copy.Condition = CopyExpression(whileAst.Condition, genericTypes, generics);
        copy.Body = CopyScope(whileAst.Body, genericTypes, generics);
        return copy;
    }

    private static EachAst CopyEach(EachAst each, IType[] genericTypes, List<String> generics)
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

    private static CompilerDirectiveAst CopyCompilerDirective(CompilerDirectiveAst compilerDirective, IType[] genericTypes, List<String> generics)
    {
        var copy = CopyAst(compilerDirective);
        copy.Type = compilerDirective.Type;
        copy.Value = CopyAst(compilerDirective.Value, genericTypes, generics);
        return copy;
    }

    private static IAst CopyExpression(IAst ast, IType[] genericTypes, List<String> generics)
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
                        var genericType = genericTypes[i];
                        var identifierCopy = CopyAst(identifier);
                        identifierCopy.Name = genericType.Name;
                        identifierCopy.TypeIndex = genericType.TypeIndex;
                        identifierCopy.BakedType = genericType;
                        return identifierCopy;
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
                callCopy.Name = call.Name;
                if (call.Generics != null)
                {
                    callCopy.Generics = new List<TypeDefinition>(call.Generics.Count);
                    foreach (var generic in call.Generics)
                    {
                        callCopy.Generics.Add(CopyType(generic, genericTypes));
                    }
                }
                if (call.SpecifiedArguments != null)
                {
                    callCopy.SpecifiedArguments = new Dictionary<String, IAst>();
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
            case CompoundExpressionAst compound:
                var compoundCopy = CopyAst(compound);
                foreach (var child in compound.Children)
                {
                    compoundCopy.Children.Add(CopyExpression(child, genericTypes, generics));
                }
                return compoundCopy;
            default:
                return null;
        }
    }

    private static T CopyAst<T>(T ast) where T : IAst, new()
    {
        return new()
        {
            FileIndex = ast.FileIndex,
            Line = ast.Line,
            Column = ast.Column
        };
    }
}
