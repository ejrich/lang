using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;

namespace ol;

public static class ProgramIRBuilder
{
    private static readonly Mutex _printFunctionMutex = new();

    public static void AddFunction(FunctionAst function)
    {
        var functionIR = new FunctionIR
        {
            Source = function, Allocations = new(), Instructions = new(), BasicBlocks = new(), Constants = new InstructionValue[function.ConstantCount]
        };

        if (function.Name == "__start")
        {
            Program.EntryPoint = functionIR;
        }
        else
        {
            Program.Functions[function.FunctionIndex] = functionIR;
        }

        var entryBlock = AddBasicBlock(functionIR);

        if (BuildSettings.Release)
        {
            for (var i = 0; i < function.Arguments.Count; i++)
            {
                var argument = function.Arguments[i];
                var allocation = AddAllocation(functionIR, argument);

                EmitStore(functionIR, allocation, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i, Type = argument.Type}, function.Body);
            }
        }
        else
        {
            for (var i = 0; i < function.Arguments.Count; i++)
            {
                var argument = function.Arguments[i];
                var allocation = AddAllocation(functionIR, argument);

                EmitStore(functionIR, allocation, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i, Type = argument.Type}, function.Body);
                functionIR.Instructions.Add(new Instruction {Type = InstructionType.DebugDeclareParameter, Scope = function.Body, Index = i});
            }
        }

        if (function.ReturnType.TypeKind == TypeKind.Compound)
        {
            functionIR.CompoundReturnAllocation = AddAllocation(functionIR, function.ReturnType);
        }

        EmitScope(functionIR, entryBlock, function.Body, function.ReturnType, null, null);

        if (function.Flags.HasFlag(FunctionFlags.ReturnVoidAtEnd))
        {
            functionIR.Instructions.Add(new Instruction {Type = InstructionType.ReturnVoid, Scope = function.Body});
        }

        if (function.Flags.HasFlag(FunctionFlags.PrintIR))
        {
            PrintFunction(function.Name, functionIR);
        }
    }

    public static void AddFunctionDefinition(FunctionAst function)
    {
        var functionIR = new FunctionIR {Source = function};

        Program.Functions[function.FunctionIndex] = functionIR;
    }

    public static void AddOperatorOverload(OperatorOverloadAst overload)
    {
        var functionIR = new FunctionIR
        {
            Source = overload, Allocations = new(), Instructions = new(), BasicBlocks = new(), Constants = new InstructionValue[overload.ConstantCount]
        };
        var entryBlock = AddBasicBlock(functionIR);

        if (BuildSettings.Release)
        {
            for (var i = 0; i < overload.Arguments.Count; i++)
            {
                var argument = overload.Arguments[i];
                var allocation = AddAllocation(functionIR, argument);

                EmitStore(functionIR, allocation, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i, Type = argument.Type}, overload.Body);
            }
        }
        else
        {
            for (var i = 0; i < overload.Arguments.Count; i++)
            {
                var argument = overload.Arguments[i];
                var allocation = AddAllocation(functionIR, argument);

                EmitStore(functionIR, allocation, new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i, Type = argument.Type}, overload.Body);
                functionIR.Instructions.Add(new Instruction {Type = InstructionType.DebugDeclareParameter, Scope = overload.Body, Index = i});
            }
        }

        Program.Functions[overload.FunctionIndex] = functionIR;

        EmitScope(functionIR, entryBlock, overload.Body, overload.ReturnType, null, null);

        if (overload.Flags.HasFlag(FunctionFlags.PrintIR))
        {
            PrintFunction(overload.Name, functionIR);
        }
    }

    public static FunctionIR CreateRunnableFunction(IAst ast)
    {
        var function = new FunctionIR {Allocations = new(), Instructions = new(), BasicBlocks = new()};
        var entryBlock = AddBasicBlock(function);

        var returns = false;
        switch (ast)
        {
            case ReturnAst returnAst:
                EmitReturn(function, returnAst, null, TypeChecker.GlobalScope);
                returns = true;
                break;
            case DeclarationAst declaration:
                EmitDeclaration(function, declaration, TypeChecker.GlobalScope);
                break;
            case CompoundDeclarationAst compoundDeclaration:
                EmitCompoundDeclaration(function, compoundDeclaration, TypeChecker.GlobalScope);
                break;
            case AssignmentAst assignment:
                EmitAssignment(function, assignment, TypeChecker.GlobalScope);
                break;
            case ScopeAst childScope:
                EmitScope(function, entryBlock, childScope, null, null, null);
                returns = childScope.Returns;
                break;
            case ConditionalAst conditional:
                EmitConditional(function, entryBlock, conditional, TypeChecker.GlobalScope, null, null, null, out returns);
                break;
            case WhileAst whileAst:
                EmitWhile(function, entryBlock, whileAst, TypeChecker.GlobalScope, null);
                break;
            case EachAst each:
                EmitEach(function, entryBlock, each, TypeChecker.GlobalScope, null);
                break;
            default:
                EmitIR(function, ast, TypeChecker.GlobalScope);
                break;
        }

        if (!returns)
        {
            function.Instructions.Add(new Instruction {Type = InstructionType.ReturnVoid});
        }

        return function;
    }

    public static FunctionIR CreateRunnableCondition(IAst ast)
    {
        var function = new FunctionIR {Allocations = new(), Instructions = new(), BasicBlocks = new()};
        var entryBlock = AddBasicBlock(function);

        var value = EmitIR(function, ast, TypeChecker.GlobalScope);

        // This logic is the opposite of EmitConditionExpression because for runnable conditions the returned value
        // should be true if the result of the expression is truthy.
        // For condition expressions in regular functions, the result is notted to jump to the else block
        var returnValue = value.Type.TypeKind switch
        {
            TypeKind.Integer => EmitInstruction(InstructionType.IntegerNotEquals, function, TypeTable.BoolType, null, value, GetDefaultConstant(value.Type)),
            TypeKind.Float => EmitInstruction(InstructionType.FloatNotEquals, function, TypeTable.BoolType, null, value, GetDefaultConstant(value.Type)),
            TypeKind.Pointer => EmitInstruction(InstructionType.IsNotNull, function, TypeTable.BoolType, null, value),
            // Will be type bool
            _ => value
        };

        function.Instructions.Add(new Instruction {Type = InstructionType.Return, Value1 = returnValue});

        return function;
    }

    private static void PrintFunction(string name, FunctionIR function)
    {
        _printFunctionMutex.WaitOne();

        Console.WriteLine($"\nIR for function '{name}'");

        var blockIndex = 0;
        var instructionIndex = 0;
        var file = Path.GetFileName(BuildSettings.Files[function.Source.FileIndex]);
        var line = function.Source.Line;
        var column = function.Source.Column;
        while (blockIndex < function.BasicBlocks.Count)
        {
            var instructionToStopAt = blockIndex < function.BasicBlocks.Count - 1 ? function.BasicBlocks[blockIndex + 1].Location : function.Instructions.Count;
            Console.WriteLine($"\n--------------- Basic Block {blockIndex} ---------------\n");
            while (instructionIndex < instructionToStopAt)
            {
                var instruction = function.Instructions[instructionIndex++];

                var text = $"{file} {line}:{column}\t\t{instruction.Type} ";
                switch (instruction.Type)
                {
                    case InstructionType.Jump:
                        text += instruction.Value1.JumpBlock.Index.ToString();
                        break;
                    case InstructionType.ConditionalJump:
                        text += $"{instruction.Value2.JumpBlock.Index} {PrintInstructionValue(instruction.Value1)}";
                        break;
                    case InstructionType.Return:
                    case InstructionType.ReturnVoid:
                        text += $"{PrintInstructionValue(instruction.Value1)}";
                        break;
                    case InstructionType.Store:
                        text += $"{PrintInstructionValue(instruction.Value1)}, {PrintInstructionValue(instruction.Value2)}";
                        break;
                    case InstructionType.GetStructPointer:
                        text += $"{PrintInstructionValue(instruction.Value1)} {instruction.Index} => v{instruction.ValueIndex}";
                        break;
                    case InstructionType.GetUnionPointer:
                        text += $"{PrintInstructionValue(instruction.Value1)}, {instruction.Value2.Type.Name}* => v{instruction.ValueIndex}";
                        break;
                    case InstructionType.Call:
                        var callingFunction = Program.Functions[instruction.Index];
                        text += $"{callingFunction.Source.Name} {PrintInstructionValue(instruction.Value1)} => v{instruction.ValueIndex}";
                        break;
                    case InstructionType.CallFunctionPointer:
                        text += $"{PrintInstructionValue(instruction.Value1)} {PrintInstructionValue(instruction.Value2)} => v{instruction.ValueIndex}";
                        break;
                    case InstructionType.SystemCall:
                        text += $"{instruction.Index} {PrintInstructionValue(instruction.Value2)} => v{instruction.ValueIndex}";
                        break;
                    case InstructionType.Load:
                    case InstructionType.LoadPointer:
                        text += $"{PrintInstructionValue(instruction.Value1)} {instruction.LoadType.Name} => v{instruction.ValueIndex}";
                        break;
                    case InstructionType.IsNull:
                    case InstructionType.IsNotNull:
                    case InstructionType.Not:
                    case InstructionType.IntegerNegate:
                    case InstructionType.FloatNegate:
                        text += $"{PrintInstructionValue(instruction.Value1)} => v{instruction.ValueIndex}";
                        break;
                    case InstructionType.DebugSetLocation:
                        line = instruction.Source.Line;
                        column = instruction.Source.Column;
                        continue;
                    case InstructionType.DebugDeclareParameter:
                    case InstructionType.DebugDeclareVariable:
                        continue;
                    default:
                        text += $"{PrintInstructionValue(instruction.Value1)}, {PrintInstructionValue(instruction.Value2)} => v{instruction.ValueIndex}";
                        break;
                }
                Console.WriteLine(text);
            }
            blockIndex++;
        }
        _printFunctionMutex.ReleaseMutex();
    }

    private static string PrintInstructionValue(InstructionValue value)
    {
        if (value == null) return string.Empty;

        switch (value.ValueType)
        {
            case InstructionValueType.Value:
                return $"v{value.ValueIndex}";
            case InstructionValueType.Allocation:
                return value.Global ? $"global{value.ValueIndex}" : $"a{value.ValueIndex}";
            case InstructionValueType.Argument:
                return $"arg{value.ValueIndex}";
            case InstructionValueType.Constant:
                switch (value.Type.TypeKind)
                {
                    case TypeKind.Boolean:
                        return value.ConstantValue.Boolean.ToString();
                    case TypeKind.Integer:
                    case TypeKind.Enum:
                        return value.ConstantValue.Integer.ToString();
                    case TypeKind.Float:
                        return value.ConstantValue.Double.ToString();
                    default:
                        return $"\"{value.ConstantString}\"";
                }
            case InstructionValueType.Null:
                return "null";
            case InstructionValueType.Type:
                return value.Type.Name;
            case InstructionValueType.TypeInfo:
                return $"TypeInfo* {value.ValueIndex}";
            case InstructionValueType.Function:
                var callingFunction = Program.Functions[value.ValueIndex];
                return $"@{callingFunction.Source.Name}";
            case InstructionValueType.CallArguments:
                return string.Join(", ", value.Values.Select(PrintInstructionValue));
        }

        return string.Empty;
    }

    public static void EmitGlobalVariable(DeclarationAst declaration, IScope scope)
    {
        if (declaration.Constant)
        {
            declaration.ConstantIndex = Program.Constants.Count;
            var constant = EmitConstantIR(declaration.Value, scope);
            Program.Constants.Add(constant);
        }
        else
        {
            var globalVariable = new GlobalVariable {Name = declaration.Name, FileIndex = declaration.FileIndex, Line = declaration.Line};

            if (declaration.Type is ArrayType arrayType)
            {
                globalVariable.Array = true;
                globalVariable.ArrayLength = arrayType.Length;
                globalVariable.Type = arrayType.ElementType;
                globalVariable.Size = globalVariable.ArrayLength * arrayType.ElementType.Size;
            }
            else
            {
                globalVariable.Type = declaration.Type;
                globalVariable.Size = declaration.Type.Size;
            }

            if (declaration.Value != null)
            {
                globalVariable.InitialValue = EmitConstantIR(declaration.Value, scope);
            }
            else
            {
                switch (declaration.Type.TypeKind)
                {
                    // Initialize arrays
                    case TypeKind.Array:
                        globalVariable.InitialValue = InitializeGlobalArray(declaration, scope, declaration.ArrayValues);
                        break;
                    case TypeKind.CArray:
                        if (declaration.ArrayValues != null)
                        {
                            globalVariable.InitialValue = InitializeGlobalCArray(declaration, scope, declaration.ArrayValues);
                        }
                        break;
                    // Initialize struct field default values
                    case TypeKind.Struct:
                    case TypeKind.String:
                        globalVariable.InitialValue = GetConstantStruct((StructAst)declaration.Type, scope, declaration.Assignments);
                        break;
                    // Initialize pointers to null
                    case TypeKind.Pointer:
                    case TypeKind.Interface:
                        globalVariable.InitialValue = new InstructionValue {ValueType = InstructionValueType.Null, Type = globalVariable.Type};
                        break;
                    // Don't initialize unions
                    case TypeKind.Union:
                        break;
                    // Or initialize to default
                    default:
                        globalVariable.InitialValue = GetDefaultConstant(declaration.Type);
                        break;
                }
            }

            declaration.Allocation = AllocationValue(Program.GlobalVariables.Count, globalVariable.Type, true);
            Program.GlobalVariables.Add(globalVariable);
            Program.GlobalVariablesSize += globalVariable.Size;
        }
    }

    private static InstructionValue GetConstantStruct(StructAst structDef, IScope scope, Dictionary<string, AssignmentAst> assignments)
    {
        var constantStruct = new InstructionValue {ValueType = InstructionValueType.ConstantStruct, Type = structDef, Values = new InstructionValue[structDef.Fields.Count]};

        if (assignments == null)
        {
            for (var i = 0; i < structDef.Fields.Count; i++)
            {
                var field = structDef.Fields[i];
                constantStruct.Values[i] = GetFieldConstant(field, scope);
            }
        }
        else
        {
            for (var i = 0; i < structDef.Fields.Count; i++)
            {
                var field = structDef.Fields[i];

                if (assignments.TryGetValue(field.Name, out var assignment))
                {
                    if (assignment.Value != null)
                    {
                        constantStruct.Values[i] = EmitConstantIR(assignment.Value, scope);
                    }
                    else if (assignment.Assignments != null)
                    {
                        constantStruct.Values[i] = GetConstantStruct((StructAst)field.Type, scope, assignment.Assignments);
                    }
                    else if (assignment.ArrayValues != null)
                    {
                        if (field.Type.TypeKind == TypeKind.Array)
                        {
                            constantStruct.Values[i] = InitializeGlobalArray(field, scope, assignment.ArrayValues);
                        }
                        else if (field.Type.TypeKind == TypeKind.CArray)
                        {
                            constantStruct.Values[i] = InitializeGlobalCArray(field, scope, assignment.ArrayValues);
                        }
                    }
                    else
                    {
                        Debug.Assert(false, "Expected assignment to have value");
                    }
                }
                else
                {
                    constantStruct.Values[i] = GetFieldConstant(field, scope);
                }
            }
        }

        return constantStruct;
    }

    private static InstructionValue GetFieldConstant(StructFieldAst field, IScope scope)
    {
        switch (field.Type.TypeKind)
        {
            // Initialize arrays
            case TypeKind.Array:
                return InitializeGlobalArray(field, scope, field.ArrayValues);
            case TypeKind.CArray:
                return InitializeGlobalCArray(field, scope, field.ArrayValues);
            // Initialize struct field default values
            case TypeKind.Struct:
            case TypeKind.String:
                return GetConstantStruct((StructAst)field.Type, scope, field.Assignments);
            // Initialize pointers to null
            case TypeKind.Pointer:
            case TypeKind.Interface:
                return new InstructionValue {ValueType = InstructionValueType.Null, Type = field.Type};
            // Or initialize to default
            default:
                return field.Value == null ? GetDefaultConstant(field.Type) : EmitConstantIR(field.Value, scope);
        }
    }

    private static InstructionValue InitializeGlobalArray(IDeclaration declaration, IScope scope, List<IAst> arrayValues)
    {
        var arrayStruct = (StructAst)declaration.Type;
        if (declaration.TypeDefinition.ConstCount == null)
        {
            return new InstructionValue
            {
                ValueType = InstructionValueType.ConstantStruct, Type = arrayStruct,
                Values = new [] {GetConstantS64(0), new InstructionValue {ValueType = InstructionValueType.Null, Type = arrayStruct.Fields[1].Type}}
            };
        }

        var length = declaration.TypeDefinition.ConstCount.Value;
        var elementType = declaration.ArrayElementType;

        var arrayIndex = Program.GlobalVariables.Count;
        var arrayVariable = new GlobalVariable
        {
            Name = "____array", Size = length * elementType.Size,
            Array = true, ArrayLength = length, Type = elementType
        };
        Program.GlobalVariables.Add(arrayVariable);
        Program.GlobalVariablesSize += arrayVariable.Size;

        if (arrayValues != null)
        {
            arrayVariable.InitialValue = new InstructionValue
            {
                ValueType = InstructionValueType.ConstantArray, Type = declaration.ArrayElementType,
                Values = arrayValues.Select(val => EmitConstantIR(val, scope)).ToArray(), ArrayLength = length
            };
        }

        return new InstructionValue
        {
            ValueType = InstructionValueType.ConstantStruct, Type = arrayStruct,
            Values = new [] {GetConstantS64(length), new InstructionValue {ValueIndex = arrayIndex}}
        };
    }

    private static InstructionValue InitializeGlobalCArray(IDeclaration declaration, IScope scope, List<IAst> arrayValues)
    {
        var arrayType = (ArrayType)declaration.Type;
        var constArray = new InstructionValue {ValueType = InstructionValueType.ConstantArray, Type = declaration.ArrayElementType, ArrayLength = arrayType.Length};

        if (arrayValues != null)
        {
            constArray.Values = arrayValues.Select(val => EmitConstantIR(val, scope)).ToArray();
        }

        return constArray;
    }

    private static BasicBlock EmitScope(FunctionIR function, BasicBlock block, ScopeAst scope, IType returnType, BasicBlock breakBlock, BasicBlock continueBlock)
    {
        foreach (var ast in scope.Children)
        {
            function.Instructions.Add(new Instruction {Type = InstructionType.DebugSetLocation, Source = ast, Scope = scope});

            switch (ast)
            {
                case ReturnAst returnAst:
                    EmitReturn(function, returnAst, returnType, scope);
                    scope.Returns = true;
                    return block;
                case DeclarationAst declaration:
                    EmitDeclaration(function, declaration, scope);
                    break;
                case CompoundDeclarationAst compoundDeclaration:
                    EmitCompoundDeclaration(function, compoundDeclaration, scope);
                    break;
                case AssignmentAst assignment:
                    EmitAssignment(function, assignment, scope);
                    break;
                case ScopeAst childScope:
                    block = EmitScope(function, block, childScope, returnType, breakBlock, continueBlock);
                    if (childScope.Returns)
                    {
                        scope.Returns = true;
                        return block;
                    }
                    break;
                case ConditionalAst conditional:
                    block = EmitConditional(function, block, conditional, scope, returnType, breakBlock, continueBlock, out var returns);
                    if (returns)
                    {
                        scope.Returns = true;
                        return block;
                    }
                    break;
                case WhileAst whileAst:
                    block = EmitWhile(function, block, whileAst, scope, returnType);
                    break;
                case EachAst each:
                    block = EmitEach(function, block, each, scope, returnType);
                    break;
                case BreakAst:
                    var breakJump = new Instruction {Type = InstructionType.Jump, Scope = scope, Value1 = BasicBlockValue(breakBlock)};
                    function.Instructions.Add(breakJump);
                    scope.Returns = true;
                    return block;
                case ContinueAst:
                    var continueJump = new Instruction {Type = InstructionType.Jump, Scope = scope, Value1 = BasicBlockValue(continueBlock)};
                    function.Instructions.Add(continueJump);
                    scope.Returns = true;
                    return block;
                default:
                    EmitIR(function, ast, scope);
                    break;
            }
        }
        return block;
    }

    private static void EmitDeclaration(FunctionIR function, DeclarationAst declaration, IScope scope)
    {
        if (declaration.Constant)
        {
            function.Constants[declaration.ConstantIndex] = EmitConstantIR(declaration.Value, scope, function);
        }
        else
        {
            var allocation = AddAllocation(function, declaration);

            if (!BuildSettings.Release)
            {
                DeclareVariable(function, declaration, scope);
            }

            if (declaration.Value != null)
            {
                var value = EmitAndCast(function, declaration.Value, scope, declaration.Type);
                EmitStore(function, allocation, value, scope);
                return;
            }

            switch (declaration.Type.TypeKind)
            {
                // Initialize arrays
                case TypeKind.Array:
                    var arrayStruct = (StructAst)declaration.Type;
                    if (declaration.TypeDefinition.ConstCount != null)
                    {
                        var arrayPointer = InitializeConstArray(function, allocation, arrayStruct, declaration.TypeDefinition.ConstCount.Value, declaration.ArrayElementType, scope);

                        if (declaration.ArrayValues != null)
                        {
                            InitializeArrayValues(function, arrayPointer, declaration.ArrayElementType, declaration.ArrayValues, scope);
                        }
                    }
                    else if (declaration.TypeDefinition.Count != null)
                    {
                        function.SaveStack = true;
                        var count = EmitAndCast(function, declaration.TypeDefinition.Count, scope, TypeTable.S64Type);
                        var countPointer = EmitGetStructPointer(function, allocation, scope, arrayStruct, 0);
                        EmitStore(function, countPointer, count, scope);

                        var elementType = new InstructionValue {ValueType = InstructionValueType.Type, Type = declaration.ArrayElementType};
                        var arrayData = EmitInstruction(InstructionType.AllocateArray, function, arrayStruct.Fields[1].Type, scope, count, elementType);
                        var dataPointer = EmitGetStructPointer(function, allocation, scope, arrayStruct, 1);
                        EmitStore(function, dataPointer, arrayData, scope);
                    }
                    else
                    {
                        var lengthPointer = EmitGetStructPointer(function, allocation, scope, arrayStruct, 0);
                        var lengthValue = GetConstantS64(0);
                        EmitStore(function, lengthPointer, lengthValue, scope);
                    }
                    break;
                case TypeKind.CArray:
                    if (declaration.ArrayValues != null)
                    {
                        InitializeArrayValues(function, allocation, declaration.ArrayElementType, declaration.ArrayValues, scope);
                    }
                    break;
                // Initialize struct field default values
                case TypeKind.Struct:
                case TypeKind.String:
                    InitializeStruct(function, (StructAst)declaration.Type, allocation, scope, declaration.Assignments);
                    break;
                // Initialize pointers to null
                case TypeKind.Pointer:
                case TypeKind.Interface:
                    EmitStore(function, allocation, new InstructionValue {ValueType = InstructionValueType.Null, Type = declaration.Type}, scope);
                    break;
                // Don't initialize unions
                case TypeKind.Union:
                    break;
                // Or initialize to default
                default:
                    var zero = GetDefaultConstant(declaration.Type);
                    EmitStore(function, allocation, zero, scope);
                    break;
            }
        }
    }

    private static void EmitCompoundDeclaration(FunctionIR function, CompoundDeclarationAst declaration, IScope scope)
    {
        var variableCount = declaration.Variables.Length;

        if (declaration.Type.TypeKind == TypeKind.Compound)
        {
            var compoundType = (CompoundType)declaration.Type;
            if (declaration.Value is CompoundExpressionAst compoundExpression)
            {
                if (!BuildSettings.Release)
                {
                    for (var i = 0; i < variableCount; i++)
                    {
                        var variable = declaration.Variables[i];
                        var allocation = variable.Pointer = AddAllocation(function, variable.Type);
                        DeclareVariable(function, variable, scope);

                        var value = EmitIR(function, compoundExpression.Children[i], scope);
                        EmitStore(function, allocation, value, scope);
                    }
                }
                else
                {
                    for (var i = 0; i < variableCount; i++)
                    {
                        var variable = declaration.Variables[i];
                        var allocation = variable.Pointer = AddAllocation(function, variable.Type);

                        var value = EmitIR(function, compoundExpression.Children[i], scope);
                        EmitStore(function, allocation, value, scope);
                    }
                }
            }
            else
            {
                var allocation = AddAllocation(function, compoundType);
                var value = EmitIR(function, declaration.Value, scope);
                EmitStore(function, allocation, value, scope);

                uint offset = 0;
                if (!BuildSettings.Release)
                {
                    for (var i = 0; i < variableCount; i++)
                    {
                        var variable = declaration.Variables[i];
                        var type = compoundType.Types[i];
                        variable.Pointer = EmitGetStructPointer(function, allocation, scope, i, offset, type);
                        offset += type.Size;

                        DeclareVariable(function, variable, scope);
                    }
                }
                else
                {
                    for (var i = 0; i < variableCount; i++)
                    {
                        var variable = declaration.Variables[i];
                        var type = compoundType.Types[i];
                        variable.Pointer = EmitGetStructPointer(function, allocation, scope, i, offset, type);
                        offset += type.Size;
                    }
                }
            }
        }
        else
        {
            if (!BuildSettings.Release)
            {
                for (var i = 0; i < variableCount; i++)
                {
                    var variable = declaration.Variables[i];
                    variable.Pointer = AddAllocation(function, declaration.Type);

                    DeclareVariable(function, variable, scope);
                }
            }
            else
            {
                for (var i = 0; i < variableCount; i++)
                {
                    var variable = declaration.Variables[i];
                    variable.Pointer = AddAllocation(function, declaration.Type);
                }
            }

            if (declaration.Value != null)
            {
                var value = EmitAndCast(function, declaration.Value, scope, declaration.Type);

                foreach (var variable in declaration.Variables)
                {
                    EmitStore(function, variable.Pointer, value, scope);
                }
                return;
            }

            switch (declaration.Type.TypeKind)
            {
                // Initialize arrays
                case TypeKind.Array:
                    var arrayStruct = (StructAst)declaration.Type;
                    if (declaration.TypeDefinition.ConstCount != null)
                    {
                        var length = declaration.TypeDefinition.ConstCount.Value;
                        if (declaration.ArrayValues == null)
                        {
                            foreach (var variable in declaration.Variables)
                            {
                                InitializeConstArray(function, variable.Pointer, arrayStruct, length, declaration.ArrayElementType, scope);
                            }
                        }
                        else
                        {
                            foreach (var variable in declaration.Variables)
                            {
                                var arrayPointer = InitializeConstArray(function, variable.Pointer, arrayStruct, length, declaration.ArrayElementType, scope);
                                InitializeArrayValues(function, arrayPointer, declaration.ArrayElementType, declaration.ArrayValues, scope);
                            }
                        }
                    }
                    else if (declaration.TypeDefinition.Count != null)
                    {
                        function.SaveStack = true;
                        foreach (var variable in declaration.Variables)
                        {
                            var count = EmitIR(function, declaration.TypeDefinition.Count, scope);
                            var countPointer = EmitGetStructPointer(function, variable.Pointer, scope, arrayStruct, 0);
                            EmitStore(function, countPointer, count, scope);

                            var elementType = new InstructionValue {ValueType = InstructionValueType.Type, Type = declaration.ArrayElementType};
                            var arrayData = EmitInstruction(InstructionType.AllocateArray, function, arrayStruct.Fields[1].Type, scope, count, elementType);
                            var dataPointer = EmitGetStructPointer(function, variable.Pointer, scope, arrayStruct, 1);
                            EmitStore(function, dataPointer, arrayData, scope);
                        }
                    }
                    else
                    {
                        var lengthValue = GetConstantS64(0);
                        foreach (var variable in declaration.Variables)
                        {
                            var lengthPointer = EmitGetStructPointer(function, variable.Pointer, scope, arrayStruct, 0);
                            EmitStore(function, lengthPointer, lengthValue, scope);
                        }
                    }
                    break;
                case TypeKind.CArray:
                    if (declaration.ArrayValues != null)
                    {
                        foreach (var variable in declaration.Variables)
                        {
                            InitializeArrayValues(function, variable.Pointer, declaration.ArrayElementType, declaration.ArrayValues, scope);
                        }
                    }
                    break;
                // Initialize struct field default values
                case TypeKind.Struct:
                case TypeKind.String:
                    foreach (var variable in declaration.Variables)
                    {
                        InitializeStruct(function, (StructAst)declaration.Type, variable.Pointer, scope, declaration.Assignments);
                    }
                    break;
                // Initialize pointers to null
                case TypeKind.Pointer:
                case TypeKind.Interface:
                    var nullValue = new InstructionValue {ValueType = InstructionValueType.Null, Type = declaration.Type};
                    foreach (var variable in declaration.Variables)
                    {
                        EmitStore(function, variable.Pointer, nullValue, scope);
                    }
                    break;
                // Or initialize to default
                default:
                    var zero = GetDefaultConstant(declaration.Type);
                    foreach (var variable in declaration.Variables)
                    {
                        EmitStore(function, variable.Pointer, zero, scope);
                    }
                    break;
            }
        }
    }

    private static InstructionValue AddAllocation(FunctionIR function, DeclarationAst declaration)
    {
        if (declaration.Type is ArrayType arrayType)
        {
            return declaration.Allocation = AddArrayAllocation(function, arrayType, arrayType.ElementType, arrayType.Length);
        }

        return declaration.Allocation = AddAllocation(function, declaration.Type);
    }

    private static InstructionValue AddAllocation(FunctionIR function, IType type)
    {
        var index = function.Allocations.Count;
        var allocation = new Allocation
        {
            Index = index, Offset = function.StackSize, Size = type.Size, Type = type
        };
        function.StackSize += type.Size;
        function.Allocations.Add(allocation);
        return AllocationValue(index, type);
    }

    private static InstructionValue AddArrayAllocation(FunctionIR function, IType type, IType elementType, uint arrayLength)
    {
        var index = function.Allocations.Count;
        var allocation = new Allocation
        {
            Index = index, Offset = function.StackSize, Size = elementType.Size,
            Array = true, ArrayLength = arrayLength, Type = elementType
        };
        function.StackSize += arrayLength * elementType.Size;
        function.Allocations.Add(allocation);
        return AllocationValue(index, type);
    }

    private static InstructionValue AllocationValue(int index, IType type, bool global = false)
    {
        return new InstructionValue {ValueType = InstructionValueType.Allocation, ValueIndex = index, Type = type, Global = global};
    }

    private static void DeclareVariable(FunctionIR function, DeclarationAst variable, IScope scope)
    {
        var instruction = new Instruction {Type = InstructionType.DebugDeclareVariable, Scope = scope, String = variable.Name, Source = variable, Value1 = variable.Allocation};
        function.Instructions.Add(instruction);
    }

    private static void DeclareVariable(FunctionIR function, VariableAst variable, IScope scope)
    {
        var instruction = new Instruction {Type = InstructionType.DebugDeclareVariable, Scope = scope, String = variable.Name, Source = variable, Value1 = variable.Pointer};
        function.Instructions.Add(instruction);
    }

    private static InstructionValue InitializeConstArray(FunctionIR function, InstructionValue arrayPointer, StructAst arrayStruct, uint length, IType elementType, IScope scope)
    {
        var lengthPointer = EmitGetStructPointer(function, arrayPointer, scope, arrayStruct, 0);
        var lengthValue = GetConstantS64(length);
        EmitStore(function, lengthPointer, lengthValue, scope);

        var dataPointerType = arrayStruct.Fields[1].Type;
        var arrayData = AddArrayAllocation(function, dataPointerType, elementType, length);
        var arrayDataPointer = EmitInstruction(InstructionType.PointerCast, function, dataPointerType, scope, arrayData, new InstructionValue {ValueType = InstructionValueType.Type, Type = dataPointerType});
        var dataPointer = EmitGetStructPointer(function, arrayPointer, scope, arrayStruct, 1);
        EmitStore(function, dataPointer, arrayDataPointer, scope);

        return arrayData;
    }

    private static void InitializeArrayValues(FunctionIR function, InstructionValue arrayPointer, IType elementType, List<IAst> arrayValues, IScope scope)
    {
        for (var i = 0; i < arrayValues.Count; i++)
        {
            var index = GetConstantInteger(i);
            var pointer = EmitGetPointer(function, arrayPointer, index, elementType, scope, true);

            var value = EmitAndCast(function, arrayValues[i], scope, elementType);
            EmitStore(function, pointer, value, scope);
        }
    }

    private static void InitializeStruct(FunctionIR function, StructAst structDef, InstructionValue pointer, IScope scope, Dictionary<string, AssignmentAst> assignments)
    {
        if (assignments == null)
        {
            for (var i = 0; i < structDef.Fields.Count; i++)
            {
                var field = structDef.Fields[i];

                var fieldPointer = EmitGetStructPointer(function, pointer, scope, structDef, i, field);

                InitializeField(function, field, fieldPointer, scope);
            }
        }
        else
        {
            for (var i = 0; i < structDef.Fields.Count; i++)
            {
                var field = structDef.Fields[i];

                var fieldPointer = EmitGetStructPointer(function, pointer, scope, structDef, i, field);

                if (assignments.TryGetValue(field.Name, out var assignment))
                {
                    if (assignment.Value != null)
                    {
                        var value = EmitAndCast(function, assignment.Value, scope, field.Type);

                        EmitStore(function, fieldPointer, value, scope);
                    }
                    else if (assignment.Assignments != null)
                    {
                        InitializeStruct(function, (StructAst)field.Type, fieldPointer, scope, assignment.Assignments);
                    }
                    else if (assignment.ArrayValues != null)
                    {
                        EmitArrayAssignments(function, assignment.ArrayValues, field.Type, fieldPointer, scope);
                    }
                    else
                    {
                        Debug.Assert(false, "Expected assignment to have a value");
                    }
                }
                else
                {
                    InitializeField(function, field, fieldPointer, scope);
                }
            }
        }
    }

    private static void InitializeField(FunctionIR function, StructFieldAst field, InstructionValue pointer, IScope scope)
    {
        switch (field.Type.TypeKind)
        {
            // Initialize arrays
            case TypeKind.Array:
                var arrayStruct = (StructAst)field.Type;
                if (field.TypeDefinition.ConstCount != null)
                {
                    var arrayPointer = InitializeConstArray(function, pointer, arrayStruct, field.TypeDefinition.ConstCount.Value, field.ArrayElementType, scope);

                    if (field.ArrayValues != null)
                    {
                        InitializeArrayValues(function, arrayPointer, field.ArrayElementType, field.ArrayValues, scope);
                    }
                }
                else
                {
                    var lengthPointer = EmitGetStructPointer(function, pointer, scope, arrayStruct, 0);
                    var lengthValue = GetConstantS64(0);
                    EmitStore(function, lengthPointer, lengthValue, scope);
                }
                break;
            case TypeKind.CArray:
                if (field.ArrayValues != null)
                {
                    InitializeArrayValues(function, pointer, field.ArrayElementType, field.ArrayValues, scope);
                }
                break;
            // Initialize struct field default values
            case TypeKind.Struct:
            case TypeKind.String:
                InitializeStruct(function, (StructAst)field.Type, pointer, scope, field.Assignments);
                break;
            // Initialize pointers to null
            case TypeKind.Pointer:
            case TypeKind.Interface:
                EmitStore(function, pointer, new InstructionValue {ValueType = InstructionValueType.Null, Type = field.Type}, scope);
                break;
            // Don't initialize unions
            case TypeKind.Union:
                break;
            // Or initialize to default
            default:
                var defaultValue = field.Value == null ? GetDefaultConstant(field.Type) : EmitIR(function, field.Value, scope);
                EmitStore(function, pointer, defaultValue, scope);
                break;
        }
    }

    private static InstructionValue GetDefaultConstant(IType type)
    {
        var value = new InstructionValue {ValueType = InstructionValueType.Constant, Type = type};
        switch (type.TypeKind)
        {
            case TypeKind.Boolean:
                value.ConstantValue = new Constant {Boolean = false};
                break;
            case TypeKind.Integer:
            case TypeKind.Enum:
                value.ConstantValue = new Constant {Integer = 0};
                break;
            case TypeKind.Float:
                value.ConstantValue = new Constant {Double = 0};
                break;
        }
        return value;
    }

    private static void EmitAssignment(FunctionIR function, AssignmentAst assignment, IScope scope)
    {
        if (assignment.Reference is CompoundExpressionAst compoundReference)
        {
            var pointers = new InstructionValue[compoundReference.Children.Count];
            var types = new IType[compoundReference.Children.Count];

            for (var i = 0; i < pointers.Length; i++)
            {
                var (pointer, type) = EmitGetReference(function, compoundReference.Children[i], scope, out var loaded);
                if (loaded && type.TypeKind == TypeKind.Pointer)
                {
                    var pointerType = (PrimitiveAst)type;
                    type = pointerType.PointerType;
                }
                pointers[i] = pointer;
                types[i] = type;
            }

            if (assignment.Value is NullAst)
            {
                for (var i = 0; i < pointers.Length; i++)
                {
                    var nullValue = new InstructionValue {ValueType = InstructionValueType.Null, Type = types[i]};
                    EmitStore(function, pointers[i], nullValue, scope);
                }
            }
            else if (assignment.Value is CompoundExpressionAst compoundExpression)
            {
                for (var i = 0; i < pointers.Length; i++)
                {
                    var value = EmitAndCast(function, compoundExpression.Children[i], scope, types[i]);
                    EmitStore(function, pointers[i], value, scope);
                }
            }
            else
            {
                var value = EmitIR(function, assignment.Value, scope);
                if (value.Type.TypeKind == TypeKind.Compound)
                {
                    var compoundType = (CompoundType)value.Type;
                    var allocation = AddAllocation(function, compoundType);
                    EmitStore(function, allocation, value, scope);

                    uint offset = 0;
                    for (var i = 0; i < pointers.Length; i++)
                    {
                        var type = compoundType.Types[i];
                        var pointer = EmitGetStructPointer(function, allocation, scope, i, offset, type);

                        var subValue = EmitLoad(function, type, pointer, scope);
                        var castValue = EmitCastValue(function, subValue, types[i], scope);
                        EmitStore(function, pointers[i], castValue, scope);
                        offset += type.Size;
                    }
                }
                else
                {
                    for (var i = 0; i < pointers.Length; i++)
                    {
                        var castValue = EmitCastValue(function, value, types[i], scope);
                        EmitStore(function, pointers[i], castValue, scope);
                    }
                }
            }
        }
        else
        {
            var (pointer, type) = EmitGetReference(function, assignment.Reference, scope, out var loaded);
            if (loaded && type.TypeKind == TypeKind.Pointer)
            {
                var pointerType = (PrimitiveAst)type;
                type = pointerType.PointerType;
            }

            EmitAssignments(function, assignment, scope, pointer, type);
        }
    }

    private static void EmitAssignments(FunctionIR function, AssignmentAst assignment, IScope scope, InstructionValue pointer, IType type)
    {
        if (assignment.Value != null)
        {
            var value = EmitAndCast(function, assignment.Value, scope, type);
            if (assignment.Operator != Operator.None)
            {
                var previousValue = EmitLoad(function, type, pointer, scope);
                value = EmitExpression(function, previousValue, value, assignment.Operator, type, scope);
            }

            EmitStore(function, pointer, value, scope);
        }
        else if (assignment.Assignments != null)
        {
            var structDef = (StructAst)type;
            for (var i = 0; i < structDef.Fields.Count; i++)
            {
                var field = structDef.Fields[i];
                if (assignment.Assignments.TryGetValue(field.Name, out var assignmentValue))
                {
                    var fieldPointer = EmitGetStructPointer(function, pointer, scope, structDef, i, field);

                    EmitAssignments(function, assignmentValue, scope, fieldPointer, field.Type);
                }
            }
        }
        else if (assignment.ArrayValues != null)
        {
            EmitArrayAssignments(function, assignment.ArrayValues, type, pointer, scope);
        }
        else
        {
            Debug.Assert(false, "Expected assignment to have a value");
        }
    }

    private static void EmitArrayAssignments(FunctionIR function, List<IAst> arrayValues, IType type, InstructionValue pointer, IScope scope)
    {
        if (type is StructAst arrayStruct)
        {
            var dataPointer = EmitGetStructPointer(function, pointer, scope, arrayStruct, 1);
            var arrayPointer = EmitLoadPointer(function, dataPointer.Type, dataPointer, scope);

            var elementType = arrayStruct.GenericTypes[0];
            for (var i = 0; i < arrayValues.Count; i++)
            {
                var index = GetConstantInteger(i);
                var elementPointer = EmitGetPointer(function, arrayPointer, index, elementType, scope);

                var value = EmitAndCast(function, arrayValues[i], scope, elementType);
                EmitStore(function, elementPointer, value, scope);
            }
        }
        else if (type is ArrayType arrayType)
        {
            InitializeArrayValues(function, pointer, arrayType.ElementType, arrayValues, scope);
        }
    }

    private static void EmitReturn(FunctionIR function, ReturnAst returnAst, IType returnType, IScope scope)
    {
        if (returnAst.Value == null)
        {
            var instruction = new Instruction {Type = InstructionType.ReturnVoid, Scope = scope};
            function.Instructions.Add(instruction);
        }
        else
        {
            if (returnType is CompoundType compoundReturnType && returnAst.Value is CompoundExpressionAst compoundExpression)
            {
                uint offset = 0;
                for (var i = 0; i < compoundReturnType.Types.Length; i++)
                {
                    var type = compoundReturnType.Types[i];
                    var expression = compoundExpression.Children[i];
                    var pointer = EmitGetStructPointer(function, function.CompoundReturnAllocation, scope, i, offset, type);

                    var value = EmitAndCast(function, expression, scope, type, returnValue: true);
                    EmitStore(function, pointer, value, scope);
                    offset += type.Size;
                }

                var returnValue = EmitLoad(function, compoundReturnType, function.CompoundReturnAllocation, scope);
                EmitInstruction(InstructionType.Return, function, null, scope, returnValue);
            }
            else
            {
                var returnValue = EmitAndCast(function, returnAst.Value, scope, returnType, returnValue: true);
                EmitInstruction(InstructionType.Return, function, null, scope, returnValue);
            }
        }
    }

    private static BasicBlock EmitConditional(FunctionIR function, BasicBlock block, ConditionalAst conditional, IScope scope, IType returnType, BasicBlock breakBlock, BasicBlock continueBlock, out bool returns)
    {
        // Run the condition expression in the current basic block and then jump to the following
        var condition = EmitConditionExpression(function, conditional.Condition, scope);
        var conditionJump = new Instruction {Type = InstructionType.ConditionalJump, Scope = scope, Value1 = condition};
        function.Instructions.Add(conditionJump);

        var bodyBlock = AddBasicBlock(function);
        bodyBlock = EmitScope(function, bodyBlock, conditional.IfBlock, returnType, breakBlock, continueBlock);
        Instruction jumpToAfter = null;
        BasicBlock elseBlock;

        // For when the the if block does not return and there is an else block, a jump to the after block is required
        if (!conditional.IfBlock.Returns && conditional.ElseBlock != null)
        {
            jumpToAfter = new Instruction {Type = InstructionType.Jump, Scope = scope};
            function.Instructions.Add(jumpToAfter);
            elseBlock = AddBasicBlock(function);
        }
        else if (bodyBlock.Location < function.Instructions.Count)
        {
            elseBlock = AddBasicBlock(function);
        }
        else
        {
            elseBlock = bodyBlock;
        }

        // Jump to the else block, otherwise fall through to the then block
        conditionJump.Value2 = BasicBlockValue(elseBlock);

        returns = false;
        if (conditional.ElseBlock == null)
        {
            return elseBlock;
        }

        elseBlock = EmitScope(function, elseBlock, conditional.ElseBlock, returnType, breakBlock, continueBlock);

        if (conditional.IfBlock.Returns && conditional.ElseBlock.Returns)
        {
            returns = true;
            return elseBlock;
        }

        var afterBlock = elseBlock.Location < function.Instructions.Count ? AddBasicBlock(function) : elseBlock;

        if (!conditional.IfBlock.Returns)
        {
            jumpToAfter.Value1 = BasicBlockValue(afterBlock);
        }

        return afterBlock;
    }

    private static BasicBlock EmitWhile(FunctionIR function, BasicBlock block, WhileAst whileAst, IScope scope, IType returnType)
    {
        // Create a block for the condition expression and then jump to the following
        var conditionBlock = block.Location < function.Instructions.Count ? AddBasicBlock(function) : block;
        var condition = EmitConditionExpression(function, whileAst.Condition, scope);
        var conditionJump = new Instruction {Type = InstructionType.ConditionalJump, Scope = scope, Value1 = condition};
        function.Instructions.Add(conditionJump);

        var whileBodyBlock = AddBasicBlock(function);
        var afterBlock = new BasicBlock();
        EmitScope(function, whileBodyBlock, whileAst.Body, returnType, afterBlock, conditionBlock);
        var jumpToCondition = new Instruction {Type = InstructionType.Jump, Scope = scope, Value1 = BasicBlockValue(conditionBlock)};
        function.Instructions.Add(jumpToCondition);

        AddBasicBlock(function, afterBlock);
        conditionJump.Value2 = BasicBlockValue(afterBlock);

        return afterBlock;
    }

    private static InstructionValue EmitConditionExpression(FunctionIR function, IAst ast, IScope scope)
    {
        var value = EmitIR(function, ast, scope);

        switch (value.Type.TypeKind)
        {
            case TypeKind.Integer:
            case TypeKind.Enum:
                return EmitInstruction(InstructionType.IntegerEquals, function, TypeTable.BoolType, scope, value, GetDefaultConstant(value.Type));
            case TypeKind.Float:
                return EmitInstruction(InstructionType.FloatEquals, function, TypeTable.BoolType, scope, value, GetDefaultConstant(value.Type));
            case TypeKind.Pointer:
                return EmitInstruction(InstructionType.IsNull, function, TypeTable.BoolType, scope, value);
            // Will be type bool
            default:
                return EmitInstruction(InstructionType.Not, function, TypeTable.BoolType, scope, value);
        }
    }

    private static BasicBlock EmitEach(FunctionIR function, BasicBlock block, EachAst each, IScope scope, IType returnType)
    {
        if (!BuildSettings.Release)
        {
            function.Instructions.Add(new Instruction {Type = InstructionType.DebugSetLocation, Scope = scope, Source = each});
        }

        InstructionValue indexVariable;
        InstructionValue compareTarget;

        if (each.Iteration != null)
        {
            indexVariable = AddAllocation(function, TypeTable.S64Type);
            if (each.IndexVariable != null)
            {
                each.IndexVariable.Pointer = indexVariable;
                if (!BuildSettings.Release)
                {
                    DeclareVariable(function, each.IndexVariable, each.Body);
                }
            }
            EmitStore(function, indexVariable, GetConstantS64(0), each.Body);

            var iteration = EmitIR(function, each.Iteration, each.Body);

            // Load the array data and set the compareTarget to the array count
            InstructionValue arrayData = null;
            var cArrayIteration = false;
            if (iteration.Type.TypeKind == TypeKind.CArray)
            {
                arrayData = iteration;
                cArrayIteration = true;
                var arrayType = (ArrayType)iteration.Type;
                compareTarget = GetConstantS64(arrayType.Length);
            }
            else
            {
                var arrayDef = (StructAst)iteration.Type;
                var iterationVariable = AddAllocation(function, arrayDef);
                EmitStore(function, iterationVariable, iteration, each.Body);

                var lengthPointer = EmitGetStructPointer(function, iterationVariable, each.Body, arrayDef, 0);
                compareTarget = EmitLoad(function, TypeTable.S32Type, lengthPointer, each.Body);

                var dataField = arrayDef.Fields[1];
                var dataPointer = EmitGetStructPointer(function, iterationVariable, each.Body, arrayDef, 1, dataField);
                arrayData = EmitLoad(function, dataField.Type, dataPointer, each.Body);
            }

            var conditionBlock = AddBasicBlock(function);
            var indexValue = EmitLoad(function, TypeTable.S64Type, indexVariable, each.Body);
            var condition = EmitInstruction(InstructionType.IntegerGreaterThanOrEqual, function, TypeTable.BoolType, each.Body, indexValue, compareTarget);

            var iterationPointer = EmitGetPointer(function, arrayData, indexValue, each.IterationVariable.Type, each.Body, cArrayIteration);
            each.IterationVariable.Pointer = iterationPointer;

            if (!BuildSettings.Release)
            {
                DeclareVariable(function, each.IterationVariable, each.Body);
            }

            return EmitEachBody(function, each.Body, TypeTable.S64Type, returnType, indexValue, indexVariable, condition, conditionBlock);
        }
        else
        {
            indexVariable = AddAllocation(function, TypeTable.S32Type);
            // Begin the loop at the beginning of the range
            var value = EmitAndCast(function, each.RangeBegin, each.Body, TypeTable.S32Type);

            EmitStore(function, indexVariable, value, each.Body);
            each.IterationVariable.Pointer = indexVariable;
            if (!BuildSettings.Release)
            {
                DeclareVariable(function, each.IterationVariable, each.Body);
            }

            // Get the end of the range
            compareTarget = EmitAndCast(function, each.RangeEnd, each.Body, TypeTable.S32Type);

            var conditionBlock = AddBasicBlock(function);
            var indexValue = EmitLoad(function, TypeTable.S32Type, indexVariable, each.Body);
            var condition = EmitInstruction(InstructionType.IntegerGreaterThan, function, TypeTable.BoolType, each.Body, indexValue, compareTarget);

            return EmitEachBody(function, each.Body, TypeTable.S32Type, returnType, indexValue, indexVariable, condition, conditionBlock);
        }
    }

    private static BasicBlock EmitEachBody(FunctionIR function, ScopeAst eachBody, IType indexType, IType returnType, InstructionValue indexValue, InstructionValue indexVariable, InstructionValue condition, BasicBlock conditionBlock)
    {
        var conditionJump = new Instruction {Type = InstructionType.ConditionalJump, Scope = eachBody, Value1 = condition};
        function.Instructions.Add(conditionJump);
        var instructionCount = function.Instructions.Count;

        var eachBodyBlock = AddBasicBlock(function);
        var eachIncrementBlock = new BasicBlock();
        var afterBlock = new BasicBlock();
        eachBodyBlock = EmitScope(function, eachBodyBlock, eachBody, returnType, afterBlock, eachIncrementBlock);

        if (eachBodyBlock.Location < function.Instructions.Count)
        {
            AddBasicBlock(function, eachIncrementBlock);
        }
        else
        {
            // Patch the jumps to the increment basic block to be the last body block that is currently empty
            for (; instructionCount < function.Instructions.Count; instructionCount++)
            {
                var instruction = function.Instructions[instructionCount];
                if (instruction.Type == InstructionType.Jump && instruction.Value1.JumpBlock == eachIncrementBlock)
                {
                    instruction.Value1.JumpBlock = eachBodyBlock;
                }
            }
        }

        var incrementValue = new InstructionValue {ValueType = InstructionValueType.Constant, Type = indexType, ConstantValue = new Constant {Integer = 1}};
        var nextValue = EmitInstruction(InstructionType.IntegerAdd, function, TypeTable.S32Type, eachBody, indexValue, incrementValue);
        EmitStore(function, indexVariable, nextValue, eachBody);
        var jumpToCondition = new Instruction {Type = InstructionType.Jump, Scope = eachBody, Value1 = BasicBlockValue(conditionBlock)};
        function.Instructions.Add(jumpToCondition);

        AddBasicBlock(function, afterBlock);
        conditionJump.Value2 = BasicBlockValue(afterBlock);

        return afterBlock;
    }

    private static InstructionValue BasicBlockValue(BasicBlock jumpBlock)
    {
        return new InstructionValue {ValueType = InstructionValueType.BasicBlock, JumpBlock = jumpBlock};
    }

    private static BasicBlock AddBasicBlock(FunctionIR function)
    {
        var block = new BasicBlock {Index = function.BasicBlocks.Count, Location = function.Instructions.Count};
        function.BasicBlocks.Add(block);
        return block;
    }

    private static void AddBasicBlock(FunctionIR function, BasicBlock block)
    {
        block.Index = function.BasicBlocks.Count;
        block.Location = function.Instructions.Count;
        function.BasicBlocks.Add(block);
    }

    private static InstructionValue EmitAndCast(FunctionIR function, IAst ast, IScope scope, IType type, bool useRawString = false, bool returnValue = false)
    {
        var value = EmitIR(function, ast, scope, useRawString, returnValue);
        return EmitCastValue(function, value, type, scope);
    }

    private static InstructionValue EmitIR(FunctionIR function, IAst ast, IScope scope, bool useRawString = false, bool returnValue = false)
    {
        return EmitIR(function, ast, scope, out _, useRawString, returnValue);
    }

    private static InstructionValue EmitIR(FunctionIR function, IAst ast, IScope scope, out bool hasCall, bool useRawString = false, bool returnValue = false)
    {
        hasCall = false;
        switch (ast)
        {
            case ConstantAst constant:
                return GetConstant(constant, useRawString);
            case NullAst nullAst:
                return new InstructionValue {ValueType = InstructionValueType.Null, Type = nullAst.TargetType};
            case IdentifierAst identifierAst:
                if (identifierAst.TypeIndex.HasValue)
                {
                    return GetConstantInteger(identifierAst.TypeIndex.Value);
                }
                else if (identifierAst.FunctionTypeIndex.HasValue)
                {
                    var functionDef = (FunctionAst)TypeTable.Types[identifierAst.FunctionTypeIndex.Value];
                    return new InstructionValue
                    {
                        ValueType = InstructionValueType.Function, Type = functionDef, ValueIndex = functionDef.FunctionIndex
                    };
                }

                var identifier = GetScopeIdentifier(scope, identifierAst.Name, out var global);
                if (identifier is DeclarationAst declaration)
                {
                    if (declaration.Constant)
                    {
                        var constantValue = global ? Program.Constants[declaration.ConstantIndex] : function.Constants[declaration.ConstantIndex];
                        if (useRawString && constantValue.Type?.TypeKind == TypeKind.String)
                        {
                            return new InstructionValue
                            {
                                ValueType = InstructionValueType.Constant, Type = TypeTable.RawStringType,
                                ConstantString = constantValue.ConstantString, UseRawString = true
                            };
                        }
                        return constantValue;
                    }

                    if (useRawString && declaration.Type.TypeKind == TypeKind.String)
                    {
                        var dataField = TypeTable.StringType.Fields[1];

                        var dataPointer = EmitGetStructPointer(function, declaration.Allocation, scope, TypeTable.StringType, 1, dataField);
                        return EmitLoadPointer(function, dataField.Type, dataPointer, scope);
                    }
                    return EmitLoad(function, declaration.Type, declaration.Allocation, scope, returnValue);
                }
                else if (identifier is VariableAst variable)
                {
                    if (useRawString && variable.Type.TypeKind == TypeKind.String)
                    {
                        var dataField = TypeTable.StringType.Fields[1];

                        var dataPointer = EmitGetStructPointer(function, variable.Pointer, scope, TypeTable.StringType, 1, dataField);
                        return EmitLoadPointer(function, dataField.Type, dataPointer, scope);
                    }
                    return EmitLoad(function, variable.Type, variable.Pointer, scope, returnValue);
                }
                break;
            case StructFieldRefAst structField:
                if (structField.IsEnum)
                {
                    var enumDef = (EnumAst)structField.Types[0];

                    return new InstructionValue
                    {
                        ValueType = InstructionValueType.Constant, Type = enumDef,
                        ConstantValue = new Constant {Integer = structField.ConstantValue}
                    };
                }
                else if (structField.IsConstant)
                {
                    return GetConstantInteger(structField.ConstantValue);
                }
                else if (structField.ConstantStringLength)
                {
                    var constantValue = structField.GlobalConstant ? Program.Constants[structField.ConstantIndex] : function.Constants[structField.ConstantIndex];

                    return GetConstantS64(constantValue.ConstantString.Length);
                }
                else if (structField.RawConstantString)
                {
                    var constantValue = structField.GlobalConstant ? Program.Constants[structField.ConstantIndex] : function.Constants[structField.ConstantIndex];

                    return new InstructionValue
                    {
                        ValueType = InstructionValueType.Constant, Type = TypeTable.RawStringType,
                        ConstantString = constantValue.ConstantString, UseRawString = true
                    };
                }
                var structFieldPointer = EmitGetStructRefPointer(function, structField, scope, out var loaded, out hasCall);
                if (!loaded)
                {
                    if (useRawString && structFieldPointer.Type.TypeKind == TypeKind.String)
                    {
                        var dataField = TypeTable.StringType.Fields[1];

                        var dataPointer = EmitGetStructPointer(function, structFieldPointer, scope, TypeTable.StringType, 1, dataField);
                        return EmitLoad(function, dataField.Type, dataPointer, scope);
                    }
                    return EmitLoad(function, structFieldPointer.Type, structFieldPointer, scope, returnValue);
                }
                return structFieldPointer;
            case CallAst call:
                hasCall = true;
                return EmitCall(function, call, scope);
            case ChangeByOneAst changeByOne:
                var (pointer, pointerType) = EmitGetReference(function, changeByOne.Value, scope, out loaded);
                if (loaded && pointerType.TypeKind == TypeKind.Pointer)
                {
                    var pointerTypeDef = (PrimitiveAst)pointerType;
                    pointerType = pointerTypeDef.PointerType;
                }
                var previousValue = EmitLoad(function, pointerType, pointer, scope);

                var constOne = new InstructionValue {ValueType = InstructionValueType.Constant, Type = changeByOne.Type};
                InstructionType instructionType;
                if (changeByOne.Type.TypeKind == TypeKind.Integer)
                {
                    instructionType = changeByOne.Positive ? InstructionType.IntegerAdd : InstructionType.IntegerSubtract;
                    constOne.ConstantValue = new Constant {Integer = 1};
                }
                else
                {
                    instructionType = changeByOne.Positive ? InstructionType.FloatAdd : InstructionType.FloatSubtract;
                    constOne.ConstantValue = new Constant {Double = 1};
                }
                var newValue = EmitInstruction(instructionType, function, changeByOne.Type, scope, previousValue, constOne);
                EmitStore(function, pointer, newValue, scope);

                return changeByOne.Prefix ? newValue : previousValue;
            case UnaryAst unary:
                InstructionValue value;
                switch (unary.Operator)
                {
                    case UnaryOperator.Not:
                        value = EmitIR(function, unary.Value, scope);
                        return EmitInstruction(InstructionType.Not, function, value.Type, scope, value);
                    case UnaryOperator.Negate:
                        value = EmitIR(function, unary.Value, scope);
                        var negate = value.Type.TypeKind == TypeKind.Integer ? InstructionType.IntegerNegate : InstructionType.FloatNegate;
                        return EmitInstruction(negate, function, value.Type, scope, value);
                    case UnaryOperator.Dereference:
                        value = EmitIR(function, unary.Value, scope, out hasCall);
                        return EmitLoad(function, unary.Type, value, scope);
                    case UnaryOperator.Reference:
                        (value, _) = EmitGetReference(function, unary.Value, scope, out _);
                        if (value.Type.TypeKind == TypeKind.CArray)
                        {
                            return EmitInstruction(InstructionType.PointerCast, function, unary.Type, scope, value, new InstructionValue {ValueType = InstructionValueType.Type, Type = unary.Type});
                        }
                        return new InstructionValue {ValueType = value.ValueType, ValueIndex = value.ValueIndex, Type = unary.Type, Global = value.Global};
                }
                break;
            case IndexAst index:
                var indexPointer = EmitGetIndexPointer(function, index, scope);

                if (useRawString && indexPointer.Type.TypeKind == TypeKind.String)
                {
                    var dataField = TypeTable.StringType.Fields[1];

                    var dataPointer = EmitGetStructPointer(function, indexPointer, scope, TypeTable.StringType, 1, dataField);
                    return EmitLoadPointer(function, dataField.Type, dataPointer, scope);
                }
                return index.CallsOverload ? indexPointer : EmitLoad(function, indexPointer.Type, indexPointer, scope);
            case ExpressionAst expression:
                var expressionValue = EmitIR(function, expression.Children[0], scope);
                for (var i = 1; i < expression.Children.Count; i++)
                {
                    var rhs = EmitIR(function, expression.Children[i], scope);
                    if (expression.OperatorOverloads.TryGetValue(i, out var overload))
                    {
                        expressionValue = EmitCall(function, overload, new []{expressionValue, rhs}, scope);
                    }
                    else
                    {
                        expressionValue = EmitExpression(function, expressionValue, rhs, expression.Operators[i - 1], expression.ResultingTypes[i - 1], scope);
                    }
                }
                return expressionValue;
            case TypeDefinition typeDef:
                return GetConstantInteger(typeDef.TypeIndex);
            case CastAst cast:
                return EmitAndCast(function, cast.Value, scope, cast.TargetType);
        }

        Debug.Assert(false, $"Expected to emit an expression");
        return null;
    }

    private static InstructionValue EmitConstantIR(IAst ast, IScope scope, FunctionIR function = null)
    {
        switch (ast)
        {
            case ConstantAst constant:
                return GetConstant(constant);
            case NullAst nullAst:
                return new InstructionValue {ValueType = InstructionValueType.Null, Type = nullAst.TargetType};
            case IdentifierAst identifierAst:
                if (identifierAst.TypeIndex.HasValue)
                {
                    return GetConstantInteger(identifierAst.TypeIndex.Value);
                }
                else if (identifierAst.FunctionTypeIndex.HasValue)
                {
                    return GetConstantInteger(identifierAst.FunctionTypeIndex.Value);
                }

                var identifier = GetScopeIdentifier(scope, identifierAst.Name, out var global);
                if (identifier is DeclarationAst declaration)
                {
                    if (declaration.Constant)
                    {
                        return global ? Program.Constants[declaration.ConstantIndex] : function?.Constants[declaration.ConstantIndex];
                    }

                    return null;
                }
                break;
            case StructFieldRefAst structField:
                if (structField.IsEnum)
                {
                    var enumDef = (EnumAst)structField.Types[0];

                    return new InstructionValue
                    {
                        ValueType = InstructionValueType.Constant, Type = enumDef,
                        ConstantValue = new Constant {Integer = structField.ConstantValue}
                    };
                }
                break;
        }
        Debug.Assert(false, "Value is not constant");
        return null;
    }

    private static InstructionValue GetConstant(ConstantAst constant, bool useRawString = false)
    {
        var value = new InstructionValue {ValueType = InstructionValueType.Constant, Type = constant.Type};
        if (constant.Type.TypeKind == TypeKind.String)
        {
            value.ConstantString = constant.String;
            value.UseRawString = useRawString;
        }
        else
        {
            value.ConstantValue = constant.Value;
        }
        return value;
    }

    private static (InstructionValue, IType) EmitGetReference(FunctionIR function, IAst ast, IScope scope, out bool loaded)
    {
        loaded = false;
        switch (ast)
        {
            case IdentifierAst identifier:
                var ident = GetScopeIdentifier(scope, identifier.Name, out var global);
                switch (ident)
                {
                    case DeclarationAst declaration:
                    {
                        return (declaration.Allocation, declaration.Type);
                    }
                    case VariableAst variable:
                        return (variable.Pointer, variable.Type);
                }
                break;
            case StructFieldRefAst structField:
                var structFieldPointer = EmitGetStructRefPointer(function, structField, scope, out loaded, out _);
                return (structFieldPointer, structFieldPointer.Type);
            case IndexAst index:
                loaded = index.CallsOverload;
                var indexPointer = EmitGetIndexPointer(function, index, scope);
                return (indexPointer, indexPointer.Type);
            case UnaryAst unary:
                var pointer = EmitIR(function, unary.Value, scope);
                return (pointer, unary.Type);
        }
        return (null, null);
    }

    private static InstructionValue EmitGetStructRefPointer(FunctionIR function, StructFieldRefAst structField, IScope scope, out bool loaded, out bool hasCall)
    {
        loaded = false;
        hasCall = false;
        InstructionValue value = null;
        var skipPointer = false;

        switch (structField.Children[0])
        {
            case IdentifierAst identifier:
                var ident = GetScopeIdentifier(scope, identifier.Name, out var global);
                switch (ident)
                {
                    case DeclarationAst declaration:
                        value = declaration.Allocation;
                        break;
                    case VariableAst variable:
                        value = variable.Pointer;
                        break;
                }
                break;
            case IndexAst index:
                value = EmitGetIndexPointer(function, index, scope);
                if (index.CallsOverload && !structField.Pointers[0])
                {
                    var type = structField.Types[0];
                    var allocation = AddAllocation(function, type);
                    EmitStore(function, allocation, value, scope);
                    value = allocation;
                }
                break;
            case CallAst call:
                hasCall = true;
                value = EmitCall(function, call, scope);
                skipPointer = true;
                if (!structField.Pointers[0])
                {
                    var type = structField.Types[0];
                    var allocation = AddAllocation(function, type);
                    EmitStore(function, allocation, value, scope);
                    value = allocation;
                }
                break;
            default:
                // @Cleanup this branch shouldn't be hit
                Console.WriteLine("Unexpected syntax tree in struct field ref");
                Environment.Exit(ErrorCodes.BuildError);
                break;
        }

        for (var i = 1; i < structField.Children.Count; i++)
        {
            var type = structField.Types[i-1];

            if (structField.Pointers[i-1])
            {
                if (!skipPointer)
                {
                    value = EmitLoadPointer(function, type, value, scope);
                }
            }
            skipPointer = false;

            if (type.TypeKind == TypeKind.CArray)
            {
                if (structField.Children[i] is IndexAst indexAst)
                {
                    var indexValue = EmitIR(function, indexAst.Index, scope);
                    var arrayType = (ArrayType) type;
                    var elementType = arrayType.ElementType;
                    value = EmitGetPointer(function, value, indexValue, elementType, scope, true);
                }
            }
            else if (type is UnionAst union)
            {
                var fieldIndex = structField.ValueIndices[i-1];
                var field = union.Fields[fieldIndex];
                var fieldType = new InstructionValue {ValueType = InstructionValueType.Type, Type = field.Type};
                value = EmitInstruction(InstructionType.GetUnionPointer, function, field.Type, scope, value, fieldType);
            }
            else
            {
                var structDefinition = (StructAst) type;
                var fieldIndex = structField.ValueIndices[i-1];
                var field = structDefinition.Fields[fieldIndex];

                value = EmitGetStructPointer(function, value, scope, structDefinition, fieldIndex, field);
            }

            if (structField.Children[i] is IndexAst index)
            {
                value = EmitGetIndexPointer(function, index, scope, value.Type, value);

                if (index.CallsOverload)
                {
                    skipPointer = true;
                    if (i < structField.Pointers.Length && !structField.Pointers[i])
                    {
                        var nextType = structField.Types[i];
                        var allocation = AddAllocation(function, nextType);
                        EmitStore(function, allocation, value, scope);
                        value = allocation;
                    }
                    else if (i == structField.Pointers.Length)
                    {
                        loaded = true;
                    }
                }
            }
            else if (structField.Children[i] is CallAst call)
            {
                var functionPointer = EmitLoad(function, value.Type, value, scope);
                value = EmitCallFunctionPointer(function, call, scope, functionPointer);

                skipPointer = true;
                if (i < structField.Pointers.Length && !structField.Pointers[i])
                {
                    var allocation = AddAllocation(function, value.Type);
                    EmitStore(function, allocation, value, scope);
                    value = allocation;
                }
                else if (i == structField.Pointers.Length)
                {
                    loaded = true;
                }
            }
        }

        return value;
    }

    private static InstructionValue EmitCall(FunctionIR function, CallAst call, IScope scope)
    {
        if (call.TypeInfo != null)
        {
            if (call.Name == "type_of")
            {
                return GetTypeInfo(call.TypeInfo);
            }
            else
            {
                return GetConstantInteger(call.TypeInfo.Size);
            }
        }

        if (call.Function == null)
        {
            return EmitCallFunctionPointer(function, call, scope);
        }

        var argumentCount = call.Function.Flags.HasFlag(FunctionFlags.Varargs) ? call.Arguments.Count : call.Function.Arguments.Count;
        var arguments = new InstructionValue[argumentCount];

        if (call.Function.Flags.HasFlag(FunctionFlags.Params))
        {
            for (var i = 0; i < argumentCount - 1; i++)
            {
                var functionArg = call.Function.Arguments[i];
                var argument = EmitIR(function, call.Arguments[i], scope, out var hasCall);
                if (functionArg.Type.TypeKind == TypeKind.Any)
                {
                    arguments[i] = GetAnyValue(function, argument, scope);
                }
                else
                {
                    var value = EmitCastValue(function, argument, functionArg.Type, scope);
                    if (hasCall && value.Type is StructAst)
                    {
                        var allocation = AddAllocation(function, value.Type);
                        EmitStore(function, allocation, value, scope);
                        arguments[i] = EmitLoad(function, value.Type, allocation, scope);
                    }
                    else
                    {
                        arguments[i] = value;
                    }
                }
            }

            // Rollup the rest of the arguments into an array
            var paramsType = call.Function.Arguments[^1].Type;
            var paramsElementType = call.Function.ParamsElementType;
            var paramsAllocationIndex = AddAllocation(function, paramsType);
            var dataPointer = InitializeConstArray(function, paramsAllocationIndex, (StructAst)paramsType, (uint)(call.Arguments.Count - call.Function.Arguments.Count + 1), paramsElementType, scope);

            uint paramsIndex = 0;
            if (paramsElementType.TypeKind == TypeKind.Any)
            {
                for (var i = call.Function.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                {
                    var index = GetConstantInteger(paramsIndex);
                    var pointer = EmitGetPointer(function, dataPointer, index, paramsElementType, scope, true);

                    var argument = EmitIR(function, call.Arguments[i], scope);
                    var anyValue = GetAnyValue(function, argument, scope);
                    EmitStore(function, pointer, anyValue, scope);
                }
            }
            else
            {
                for (var i = call.Function.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                {
                    var index = GetConstantInteger(paramsIndex);
                    var pointer = EmitGetPointer(function, dataPointer, index, paramsElementType, scope, true);

                    var value = EmitAndCast(function, call.Arguments[i], scope, paramsElementType);
                    EmitStore(function, pointer, value, scope);
                }
            }

            var paramsValue = EmitLoad(function, paramsType, paramsAllocationIndex, scope);
            arguments[argumentCount - 1] = paramsValue;
        }
        else if (call.Function.Flags.HasFlag(FunctionFlags.Varargs))
        {
            var i = 0;
            for (; i < call.Function.Arguments.Count - 1; i++)
            {
                arguments[i] = EmitAndCast(function, call.Arguments[i], scope, call.Function.Arguments[i].Type, true);
            }

            // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
            // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
            for (; i < argumentCount; i++)
            {
                var argument = EmitIR(function, call.Arguments[i], scope, true);
                if (argument.Type?.TypeKind == TypeKind.Float && argument.Type.Size == 4)
                {
                    argument = EmitCastValue(function, argument, TypeTable.Float64Type, scope);
                }
                arguments[i] = argument;
            }
        }
        else
        {
            var externCall = call.Function.Flags.HasFlag(FunctionFlags.Extern);
            for (var i = 0; i < argumentCount; i++)
            {
                var functionArg = call.Function.Arguments[i];
                var argument = EmitIR(function, call.Arguments[i], scope, out var hasCall, externCall);
                if (functionArg.Type.TypeKind == TypeKind.Any)
                {
                    arguments[i] = GetAnyValue(function, argument, scope);
                }
                else
                {
                    var value = EmitCastValue(function, argument, functionArg.Type, scope);
                    if (hasCall && value.Type is StructAst)
                    {
                        var allocation = AddAllocation(function, value.Type);
                        EmitStore(function, allocation, value, scope);
                        arguments[i] = EmitLoad(function, value.Type, allocation, scope);
                    }
                    else
                    {
                        arguments[i] = value;
                    }
                }
            }

            if (call.Function.Flags.HasFlag(FunctionFlags.Syscall))
            {
                return EmitSyscall(function, call.Function, arguments, scope);
            }
        }

        return EmitCall(function, call.Function, arguments, scope, call.ExternIndex);
    }

    private static InstructionValue GetAnyValue(FunctionIR function, InstructionValue argument, IScope scope)
    {
        if (argument.Type.TypeKind == TypeKind.Any)
        {
            return argument;
        }

        // Allocate the Any struct
        var allocation = AddAllocation(function, TypeTable.AnyType);

        // Set the TypeInfo pointer
        var typeInfoPointer = EmitGetStructPointer(function, allocation, scope, TypeTable.AnyType, 0);
        var typeInfo = GetTypeInfo(argument.Type);
        EmitStore(function, typeInfoPointer, typeInfo, scope);

        // Set the data pointer
        var dataPointer = EmitGetStructPointer(function, allocation, scope, TypeTable.AnyType, 1);
        var voidPointer = new InstructionValue {ValueType = InstructionValueType.Type, Type = TypeTable.VoidPointerType};
        // a. For pointers, set the value with the existing pointer
        if (argument.Type.TypeKind == TypeKind.Pointer)
        {
            var voidPointerValue = EmitInstruction(InstructionType.PointerCast, function, TypeTable.VoidPointerType, scope, argument, voidPointer);
            EmitStore(function, dataPointer, voidPointerValue, scope);
        }
        // b. Otherwise, allocate the value, store the value, and set the value with the allocated pointer
        else
        {
            var dataAllocation = AddAllocation(function, argument.Type);

            EmitStore(function, dataAllocation, argument, scope);
            var voidPointerValue = EmitInstruction(InstructionType.PointerCast, function, TypeTable.VoidPointerType, scope, dataAllocation, voidPointer);
            EmitStore(function, dataPointer, voidPointerValue, scope);
        }

        return EmitLoad(function, TypeTable.AnyType, allocation, scope);
    }

    private static InstructionValue GetTypeInfo(IType type)
    {
        type.Used = true;
        return new InstructionValue {ValueType = InstructionValueType.TypeInfo, ValueIndex = type.TypeIndex, Type = TypeTable.TypeInfoPointerType};
    }

    private static InstructionValue EmitCallFunctionPointer(FunctionIR function, CallAst call, IScope scope, InstructionValue functionPointer = null)
    {
        if (functionPointer == null)
        {
            var identifier = GetScopeIdentifier(scope, call.Name, out var _);
            if (identifier is DeclarationAst declaration)
            {
                functionPointer = EmitLoad(function, declaration.Type, declaration.Allocation, scope);
            }
            else if (identifier is VariableAst variable)
            {
                functionPointer = EmitLoad(function, variable.Type, variable.Pointer, scope);
            }
        }

        var arguments = new InstructionValue[call.Interface.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            var functionArg = call.Interface.Arguments[i];
            var argument = EmitIR(function, call.Arguments[i], scope);
            if (functionArg.Type.TypeKind == TypeKind.Any)
            {
                arguments[i] = GetAnyValue(function, argument, scope);
            }
            else
            {
                arguments[i] = EmitCastValue(function, argument, functionArg.Type, scope);
            }
        }

        var callInstruction = new Instruction
        {
            Type = InstructionType.CallFunctionPointer, Scope = scope, Value1 = functionPointer,
            Value2 = new InstructionValue {ValueType = InstructionValueType.CallArguments, Values = arguments}
        };
        return AddInstruction(function, callInstruction, call.Interface.ReturnType);
    }

    private static InstructionValue EmitGetIndexPointer(FunctionIR function, IndexAst index, IScope scope, IType type = null, InstructionValue variable = null)
    {
        if (type == null)
        {
            var identifier = GetScopeIdentifier(scope, index.Name, out var global);
            if (identifier is DeclarationAst declaration)
            {
                type = declaration.Type;
                variable = declaration.Allocation;
            }
            else if (identifier is VariableAst variableAst)
            {
                type = variableAst.Type;
                variable = variableAst.Pointer;
            }
        }

        var indexValue = EmitIR(function, index.Index, scope);

        if (index.CallsOverload)
        {
            var value = EmitLoad(function, type, variable, scope);
            return EmitCall(function, index.Overload, new []{value, indexValue}, scope);
        }

        IType elementType;
        if (type.TypeKind == TypeKind.Pointer)
        {
            var pointerType = (PrimitiveAst)type;
            elementType = pointerType.PointerType;

            var dataPointer = EmitLoadPointer(function, type, variable, scope);
            return EmitGetPointer(function, dataPointer, indexValue, elementType, scope);
        }

        if (type.TypeKind == TypeKind.CArray)
        {
            var arrayType = (ArrayType)type;
            elementType = arrayType.ElementType;

            return EmitGetPointer(function, variable, indexValue, elementType, scope, true);
        }

        {
            var structAst = (StructAst)type;
            var dataField = structAst.Fields[1];
            if (type.TypeKind == TypeKind.String)
            {
                elementType = TypeTable.U8Type;
            }
            else
            {
                var pointerType = (PrimitiveAst)dataField.Type;
                elementType = pointerType.PointerType;
            }

            var data = EmitGetStructPointer(function, variable, scope, structAst, 1, dataField);
            var dataPointer = EmitLoadPointer(function, data.Type, data, scope);
            return EmitGetPointer(function, dataPointer, indexValue, elementType, scope);
        }
    }

    private static InstructionValue EmitExpression(FunctionIR function, InstructionValue lhs, InstructionValue rhs, Operator op, IType type, IScope scope)
    {
        // 1. Handle pointer math
        if (lhs.Type?.TypeKind == TypeKind.Pointer)
        {
            return EmitPointerOperation(function, lhs, rhs, op, type, scope);
        }
        if (rhs.Type?.TypeKind == TypeKind.Pointer)
        {
            return EmitPointerOperation(function, rhs, lhs, op, type, scope);
        }

        // 2. Handle compares and shifts, since the lhs and rhs should not be cast to the target type
        switch (op)
        {
            case Operator.And:
                return EmitInstruction(InstructionType.And, function, type, scope, lhs, rhs);
            case Operator.Or:
                return EmitInstruction(InstructionType.Or, function, type, scope, lhs, rhs);
            case Operator.ShiftLeft:
                rhs = EmitCastValue(function, rhs, type, scope);
                return EmitInstruction(InstructionType.ShiftLeft, function, type, scope, lhs, rhs);
            case Operator.ShiftRight:
                rhs = EmitCastValue(function, rhs, type, scope);
                return EmitInstruction(InstructionType.ShiftRight, function, type, scope, lhs, rhs);
            case Operator.RotateLeft:
                rhs = EmitCastValue(function, rhs, type, scope);
                return EmitInstruction(InstructionType.RotateLeft, function, type, scope, lhs, rhs);
            case Operator.RotateRight:
                rhs = EmitCastValue(function, rhs, type, scope);
                return EmitInstruction(InstructionType.RotateRight, function, type, scope, lhs, rhs);
            case Operator.Equality:
            case Operator.NotEqual:
            case Operator.GreaterThanEqual:
            case Operator.LessThanEqual:
            case Operator.GreaterThan:
            case Operator.LessThan:
                return EmitCompare(function, lhs, rhs, op, type, scope);
        }

        // 3. Cast lhs and rhs to the target types
        lhs = EmitCastValue(function, lhs, type, scope);
        rhs = EmitCastValue(function, rhs, type, scope);

        // 4. Handle the rest of the simple operators
        switch (op)
        {
            case Operator.BitwiseAnd:
                return EmitInstruction(InstructionType.BitwiseAnd, function, type, scope, lhs, rhs);
            case Operator.BitwiseOr:
                return EmitInstruction(InstructionType.BitwiseOr, function, type, scope, lhs, rhs);
            case Operator.Xor:
                return EmitInstruction(InstructionType.Xor, function, type, scope, lhs, rhs);
        }

        InstructionType instructionType;
        if (type.TypeKind == TypeKind.Integer)
        {
            var integerType = (PrimitiveAst)type;
            instructionType = op switch
            {
                Operator.Add => InstructionType.IntegerAdd,
                Operator.Subtract => InstructionType.IntegerSubtract,
                Operator.Multiply => InstructionType.IntegerMultiply,
                Operator.Divide => integerType.Signed ? InstructionType.IntegerDivide : InstructionType.UnsignedIntegerDivide,
                Operator.Modulus => integerType.Signed ? InstructionType.IntegerModulus : InstructionType.UnsignedIntegerModulus,
                // @Cleanup this branch should never be hit
                _ => InstructionType.IntegerAdd
            };
        }
        else
        {
            instructionType = op switch
            {
                Operator.Add => InstructionType.FloatAdd,
                Operator.Subtract => InstructionType.FloatSubtract,
                Operator.Multiply => InstructionType.FloatMultiply,
                Operator.Divide => InstructionType.FloatDivide,
                Operator.Modulus => InstructionType.FloatModulus,
                // @Cleanup this branch should never be hit
                _ => InstructionType.FloatAdd
            };
        }
        return EmitInstruction(instructionType, function, type, scope, lhs, rhs);
    }

    private static InstructionValue EmitPointerOperation(FunctionIR function, InstructionValue lhs, InstructionValue rhs, Operator op, IType type, IScope scope)
    {
        if (op == Operator.Equality)
        {
            if (rhs.ValueType == InstructionValueType.Null)
            {
                return EmitInstruction(InstructionType.IsNull, function, type, scope, lhs);
            }
            return EmitInstruction(InstructionType.PointerEquals, function, type, scope, lhs, rhs);
        }
        if (op == Operator.NotEqual)
        {
            if (rhs.ValueType == InstructionValueType.Null)
            {
                return EmitInstruction(InstructionType.IsNotNull, function, type, scope, lhs);
            }
            return EmitInstruction(InstructionType.PointerNotEquals, function, type, scope, lhs, rhs);
        }
        if (op == Operator.Subtract)
        {
            return EmitInstruction(InstructionType.PointerSubtract, function, type, scope, lhs, rhs);
        }
        return EmitInstruction(InstructionType.PointerAdd, function, type, scope, lhs, rhs);
    }

    private static InstructionValue EmitCompare(FunctionIR function, InstructionValue lhs, InstructionValue rhs, Operator op, IType type, IScope scope)
    {
        switch (lhs.Type.TypeKind)
        {
            case TypeKind.Integer:
                switch (rhs.Type.TypeKind)
                {
                    case TypeKind.Integer:
                        if (lhs.Type.Size > rhs.Type.Size)
                        {
                            rhs = EmitCastValue(function, rhs, lhs.Type, scope);
                        }
                        else if (lhs.Type.Size < rhs.Type.Size)
                        {
                            lhs = EmitCastValue(function, lhs, rhs.Type, scope);
                        }
                        var integerType = (PrimitiveAst)lhs.Type;
                        return EmitInstruction(GetIntCompareInstructionType(op, integerType.Signed), function, type, scope, lhs, rhs);
                    case TypeKind.Float:
                        lhs = EmitCastValue(function, lhs, rhs.Type, scope);
                        return EmitInstruction(GetFloatCompareInstructionType(op), function, type, scope, lhs, rhs);
                }
                break;
            case TypeKind.Float:
                switch (rhs.Type.TypeKind)
                {
                    case TypeKind.Integer:
                        rhs = EmitCastValue(function, rhs, lhs.Type, scope);
                        return EmitInstruction(GetFloatCompareInstructionType(op), function, type, scope, lhs, rhs);
                    case TypeKind.Float:
                        if (lhs.Type.Size > rhs.Type.Size)
                        {
                            rhs = EmitCastValue(function, rhs, TypeTable.Float64Type, scope);
                        }
                        else if (lhs.Type.Size < rhs.Type.Size)
                        {
                            lhs = EmitCastValue(function, lhs, TypeTable.Float64Type, scope);
                        }
                        return EmitInstruction(GetFloatCompareInstructionType(op), function, type, scope, lhs, rhs);
                }
                break;
            case TypeKind.Enum:
                var enumAst = (EnumAst)lhs.Type;
                return EmitInstruction(GetIntCompareInstructionType(op, enumAst.BaseType.Signed), function, type, scope, lhs, rhs);
            case TypeKind.Interface:
                if (op == Operator.Equality)
                {
                    if (rhs.ValueType == InstructionValueType.Null)
                    {
                        return EmitInstruction(InstructionType.IsNull, function, type, scope, lhs);
                    }
                    return EmitInstruction(InstructionType.PointerEquals, function, type, scope, lhs, rhs);
                }
                if (op == Operator.NotEqual)
                {
                    if (rhs.ValueType == InstructionValueType.Null)
                    {
                        return EmitInstruction(InstructionType.IsNotNull, function, type, scope, lhs);
                    }
                    return EmitInstruction(InstructionType.PointerNotEquals, function, type, scope, lhs, rhs);
                }
                break;
        }

        Debug.Assert(false, "Unexpected type in compare");
        return null;
    }

    private static InstructionType GetIntCompareInstructionType(Operator op, bool signed)
    {
        return op switch
        {
            Operator.Equality => InstructionType.IntegerEquals,
            Operator.NotEqual => InstructionType.IntegerNotEquals,
            Operator.GreaterThan => signed ? InstructionType.IntegerGreaterThan : InstructionType.UnsignedIntegerGreaterThan,
            Operator.GreaterThanEqual => signed ? InstructionType.IntegerGreaterThanOrEqual : InstructionType.UnsignedIntegerGreaterThanOrEqual,
            Operator.LessThan => signed ? InstructionType.IntegerLessThan : InstructionType.UnsignedIntegerLessThan,
            Operator.LessThanEqual => signed ? InstructionType.IntegerLessThanOrEqual : InstructionType.UnsignedIntegerLessThanOrEqual,
            // @Cleanup This branch should never be hit
            _ => InstructionType.IntegerEquals
        };
    }

    private static InstructionType GetFloatCompareInstructionType(Operator op)
    {
        return op switch
        {
            Operator.Equality => InstructionType.FloatEquals,
            Operator.NotEqual => InstructionType.FloatNotEquals,
            Operator.GreaterThan => InstructionType.FloatGreaterThan,
            Operator.GreaterThanEqual => InstructionType.FloatGreaterThanOrEqual,
            Operator.LessThan => InstructionType.FloatLessThan,
            Operator.LessThanEqual => InstructionType.FloatLessThanOrEqual,
            // @Cleanup This branch should never be hit
            _ => InstructionType.FloatEquals
        };
    }

    private static InstructionValue EmitCastValue(FunctionIR function, InstructionValue value, IType targetType, IScope scope)
    {
        if (value.Type == targetType || value.UseRawString || targetType.TypeKind == TypeKind.Type || (targetType.TypeKind == TypeKind.Interface && value.Type.TypeKind != TypeKind.Pointer))
        {
            return value;
        }

        var castInstruction = new Instruction {Scope = scope, Value1 = value, Value2 = new InstructionValue {ValueType = InstructionValueType.Type, Type = targetType}};

        switch (targetType.TypeKind)
        {
            case TypeKind.Integer:
            {
                var targetIntegerType = (PrimitiveAst)targetType;
                switch (value.Type.TypeKind)
                {
                    case TypeKind.Integer:
                        var sourceIntegerType = (PrimitiveAst)value.Type;
                        castInstruction.Type = GetIntegerCastType(sourceIntegerType, targetIntegerType);
                        break;
                    case TypeKind.Enum:
                        var enumType = (EnumAst)value.Type;
                        sourceIntegerType = enumType.BaseType;
                        castInstruction.Type = GetIntegerCastType(sourceIntegerType, targetIntegerType);
                        break;
                    case TypeKind.Float:
                        castInstruction.Type = targetIntegerType.Signed ? InstructionType.FloatToIntegerCast: InstructionType.FloatToUnsignedIntegerCast;
                        break;
                    case TypeKind.Pointer:
                        castInstruction.Type = InstructionType.PointerToIntegerCast;
                        break;
                }
                break;
            }
            case TypeKind.Float:
                if (value.Type.TypeKind == TypeKind.Float)
                {
                    castInstruction.Type = InstructionType.FloatCast;
                }
                else
                {
                    var integerType = (PrimitiveAst)value.Type;
                    castInstruction.Type = integerType.Signed ? InstructionType.IntegerToFloatCast : InstructionType.UnsignedIntegerToFloatCast;
                }
                break;
            case TypeKind.Pointer:
            case TypeKind.Interface:
                castInstruction.Type = InstructionType.PointerCast;
                break;
        }

        return AddInstruction(function, castInstruction, targetType);
    }

    private static InstructionType GetIntegerCastType(PrimitiveAst sourceType, PrimitiveAst targetType)
    {
        if (targetType.Size >= sourceType.Size)
        {
            if (targetType.Signed)
            {
                return sourceType.Signed ? InstructionType.IntegerExtend : InstructionType.UnsignedIntegerToIntegerExtend;
            }
            return sourceType.Signed ? InstructionType.IntegerToUnsignedIntegerExtend : InstructionType.UnsignedIntegerExtend;
        }

        if (targetType.Signed)
        {
            return sourceType.Signed ? InstructionType.IntegerTruncate : InstructionType.UnsignedIntegerToIntegerTruncate;
        }
        return sourceType.Signed ? InstructionType.IntegerToUnsignedIntegerTruncate : InstructionType.UnsignedIntegerTruncate;
    }

    private static InstructionValue GetConstantS64(long value)
    {
        return new InstructionValue {ValueType = InstructionValueType.Constant, Type = TypeTable.S64Type, ConstantValue = new Constant {Integer = value}};
    }

    private static InstructionValue GetConstantInteger(int value)
    {
        return new InstructionValue {ValueType = InstructionValueType.Constant, Type = TypeTable.S32Type, ConstantValue = new Constant {Integer = value}};
    }

    private static InstructionValue GetConstantInteger(uint value)
    {
        return new InstructionValue {ValueType = InstructionValueType.Constant, Type = TypeTable.S32Type, ConstantValue = new Constant {Integer = value}};
    }

    private static IAst GetScopeIdentifier(IScope scope, string name, out bool global)
    {
        do {
            if (scope.Identifiers.TryGetValue(name, out var ast))
            {
                global = scope.Parent == null;
                return ast;
            }
            scope = scope.Parent;
        } while (scope != null);
        global = false;
        return null;
    }

    private static InstructionValue EmitLoad(FunctionIR function, IType type, InstructionValue value, IScope scope, bool returnValue = false)
    {
        // Loading C arrays is not required, only the pointer is needed, unless it is a return value
        if (type.TypeKind == TypeKind.CArray && !returnValue)
        {
            return value;
        }

        var loadInstruction = new Instruction {Type = InstructionType.Load, Scope = scope, LoadType = type, Value1 = value};
        return AddInstruction(function, loadInstruction, type);
    }

    private static InstructionValue EmitLoadPointer(FunctionIR function, IType type, InstructionValue value, IScope scope)
    {
        var loadInstruction = new Instruction {Type = InstructionType.LoadPointer, Scope = scope, LoadType = type, Value1 = value};
        return AddInstruction(function, loadInstruction, type);
    }

    private static InstructionValue EmitGetPointer(FunctionIR function, InstructionValue pointer, InstructionValue index, IType type, IScope scope, bool getFirstPointer = false)
    {
        var instruction = new Instruction
        {
            Type = InstructionType.GetPointer, Scope = scope, Index2 = (int)type.Size, Value1 = pointer, Value2 = index, Flag = getFirstPointer
        };
        return AddInstruction(function, instruction, type);
    }

    private static InstructionValue EmitGetStructPointer(FunctionIR function, InstructionValue value, IScope scope, StructAst structDef, int fieldIndex, StructFieldAst field = null)
    {
        if (field == null)
        {
             field = structDef.Fields[fieldIndex];
        }

        return EmitGetStructPointer(function, value, scope, fieldIndex, field.Offset, field.Type);
    }

    private static InstructionValue EmitGetStructPointer(FunctionIR function, InstructionValue value, IScope scope, int fieldIndex, uint offset, IType type)
    {
        var instruction = new Instruction {Type = InstructionType.GetStructPointer, Scope = scope, Index = fieldIndex, Index2 = (int)offset, Value1 = value};
        return AddInstruction(function, instruction, type);
    }

    private static InstructionValue EmitCall(FunctionIR function, IFunction callingFunction, InstructionValue[] arguments, IScope scope, int callIndex = 0)
    {
        var callInstruction = new Instruction
        {
            Type = InstructionType.Call, Scope = scope, Index = callingFunction.FunctionIndex, Index2 = callIndex,
            Value1 = new InstructionValue {ValueType = InstructionValueType.CallArguments, Values = arguments}
        };
        return AddInstruction(function, callInstruction, callingFunction.ReturnType);
    }

    private static InstructionValue EmitSyscall(FunctionIR function, FunctionAst callingFunction, InstructionValue[] arguments, IScope scope)
    {
        var callInstruction = new Instruction
        {
            Type = InstructionType.SystemCall, Source = callingFunction, Scope = scope, Index = callingFunction.Syscall,
            Flag = callingFunction.ReturnType == TypeTable.VoidType, Value1 = new InstructionValue {ValueType = InstructionValueType.CallArguments, Values = arguments}
        };
        return AddInstruction(function, callInstruction, callingFunction.ReturnType);
    }

    private static void EmitStore(FunctionIR function, InstructionValue pointer, InstructionValue value, IScope scope)
    {
        var store = new Instruction {Type = InstructionType.Store, Scope = scope, Value1 = pointer, Value2 = value};
        function.Instructions.Add(store);
    }

    private static InstructionValue EmitInstruction(InstructionType instructionType, FunctionIR function, IType type, IScope scope, InstructionValue value1, InstructionValue value2 = null)
    {
        var instruction = new Instruction {Type = instructionType, Scope = scope, Value1 = value1, Value2 = value2};
        return AddInstruction(function, instruction, type);
    }

    private static InstructionValue AddInstruction(FunctionIR function, Instruction instruction, IType type)
    {
        Debug.Assert(instruction.Type != InstructionType.None, "Instruction did not have an assigned type");

        var valueIndex = function.ValueCount++;
        instruction.ValueIndex = valueIndex;
        var value = new InstructionValue {ValueIndex = valueIndex, Type = type};
        function.Instructions.Add(instruction);
        return value;
    }
}
