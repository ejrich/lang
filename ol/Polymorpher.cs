using System.Collections.Generic;
using System.Linq;

namespace ol
{
    public interface IPolymorpher
    {
        StructAst CreatePolymorphedStruct(StructAst baseStruct, string name, string backendName, TypeKind typeKind, IType[] genericTypes, params TypeDefinition[] genericTypeDefinitions);
        FunctionAst CreatePolymorphedFunction(FunctionAst baseFunction, string name, IType[] genericTypes);
        OperatorOverloadAst CreatePolymorphedOperatorOverload(OperatorOverloadAst baseOverload, IType[] genericTypes);
    }

    public class Polymorpher : IPolymorpher
    {
        public StructAst CreatePolymorphedStruct(StructAst baseStruct, string name, string backendName, TypeKind typeKind, IType[] genericTypes, params TypeDefinition[] genericTypeDefinitions)
        {
            var polyStruct = CopyAst(baseStruct);
            polyStruct.Name = name;
            polyStruct.BackendName = backendName;
            polyStruct.BaseStructName = baseStruct.Name;
            polyStruct.BaseTypeDefinition = baseStruct.BaseTypeDefinition;
            polyStruct.BaseStruct = baseStruct.BaseStruct;
            polyStruct.TypeKind = typeKind;
            polyStruct.GenericTypes = genericTypes;

            foreach (var field in baseStruct.Fields)
            {
                if (field.HasGenerics)
                {
                    var newField = CopyAst(field);
                    newField.TypeDefinition = CopyType(field.TypeDefinition, genericTypeDefinitions);
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

        public FunctionAst CreatePolymorphedFunction(FunctionAst baseFunction, string name, IType[] genericTypes)
        {
            var genericTypeDefs = GetGenericTypeDefinitions(genericTypes);
            var function = CopyAst(baseFunction);
            function.Name = name;
            function.Flags = baseFunction.Flags;

            if (baseFunction.Flags.HasFlag(FunctionFlags.ReturnTypeHasGenerics))
            {
                function.ReturnTypeDefinition = CopyType(baseFunction.ReturnTypeDefinition, genericTypeDefs);
            }
            else
            {
                function.ReturnType = baseFunction.ReturnType;
            }

            foreach (var argument in baseFunction.Arguments)
            {
                if (argument.HasGenerics)
                {
                    function.Arguments.Add(CopyDeclaration(argument, genericTypeDefs, baseFunction.Generics));
                }
                else
                {
                    function.Arguments.Add(argument);
                }
            }

            function.Body = CopyScope(baseFunction.Body, genericTypeDefs, baseFunction.Generics);

            return function;
        }

        public OperatorOverloadAst CreatePolymorphedOperatorOverload(OperatorOverloadAst baseOverload, IType[] genericTypes)
        {
            var genericTypeDefs = GetGenericTypeDefinitions(genericTypes);
            var overload = CopyAst(baseOverload);
            overload.Operator = baseOverload.Operator;
            overload.Type = CopyType(baseOverload.Type, genericTypeDefs);
            overload.Name = $"operator.{overload.Operator}.{overload.Type.GenericName}";
            overload.Flags = baseOverload.Flags;
            overload.ReturnTypeDefinition = baseOverload.Flags.HasFlag(FunctionFlags.ReturnTypeHasGenerics) ? CopyType(baseOverload.ReturnTypeDefinition, genericTypeDefs) : baseOverload.ReturnTypeDefinition;

            foreach (var argument in baseOverload.Arguments)
            {
                overload.Arguments.Add(CopyDeclaration(argument, genericTypeDefs, baseOverload.Generics));
            }

            overload.Body = CopyScope(baseOverload.Body, genericTypeDefs, baseOverload.Generics);

            return overload;
        }

        // @Cleanup This probably isn't the best idea, but ok for now
        private TypeDefinition[] GetGenericTypeDefinitions(IType[] genericTypes)
        {
            var genericTypeDefinitions = new TypeDefinition[genericTypes.Length];

            for (var i = 0; i < genericTypes.Length; i++)
            {
                genericTypeDefinitions[i] = GetTypeDefinition(genericTypes[i]);
            }

            return genericTypeDefinitions;
        }

        private TypeDefinition GetTypeDefinition(IType type)
        {
            var typeDef = new TypeDefinition();
            switch (type.TypeKind)
            {
                case TypeKind.Void:
                case TypeKind.Boolean:
                case TypeKind.Integer:
                case TypeKind.Float:
                case TypeKind.String:
                case TypeKind.Enum:
                case TypeKind.Type:
                case TypeKind.Function:
                    typeDef.Name = type.Name;
                    break;
                case TypeKind.Pointer:
                    var pointerType = (PrimitiveAst)type;
                    typeDef.Name = "*";
                    typeDef.Generics.Add(GetTypeDefinition(pointerType.PointerType));
                    break;
                case TypeKind.CArray:
                    var arrayType = (ArrayType)type;
                    typeDef.Name = "CArray";
                    typeDef.Generics.Add(GetTypeDefinition(arrayType.ElementType));
                    break;
                case TypeKind.Array:
                case TypeKind.Struct:
                    var structDef = (StructAst)type;
                    if (structDef.GenericTypes != null)
                    {
                        typeDef.Name = structDef.BaseStructName;
                        foreach (var genericType in structDef.GenericTypes)
                        {
                            typeDef.Generics.Add(GetTypeDefinition(genericType));
                        }
                    }
                    else
                    {
                        typeDef.Name = structDef.Name;
                    }
                    break;
            }
            return typeDef;
        }

        private TypeDefinition CopyType(TypeDefinition type, TypeDefinition[] genericTypes)
        {
            if (type.IsGeneric)
            {
                return genericTypes[type.GenericIndex];
            }

            var copyType = CopyAst(type);
            copyType.Name = type.Name;
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
            copy.Reference = CopyExpression(assignment.Reference, genericTypes, generics);
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
