using System;
using System.Collections.Generic;

namespace Lang
{
    public interface IProgramIRBuilder
    {
        ProgramIR Program { get; }
        FunctionIR AddFunction(FunctionAst function, Dictionary<string, IType> types);
        FunctionIR AddOperatorOverload(OperatorOverloadAst overload, Dictionary<string, IType> types);
        void EmitDeclaration(FunctionIR function, DeclarationAst declaration, IType type, ScopeAst scope);
        void EmitReturn(FunctionIR function, ReturnAst returnAst, IType returnType, ScopeAst scope, BasicBlock block = null);
        InstructionValue EmitIR(FunctionIR function, IAst ast, ScopeAst scope, BasicBlock block = null);
    }

    public class ProgramIRBuilder : IProgramIRBuilder
    {
        private readonly TypeDefinition _s32Type = new() {Name = "s32", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};

        public ProgramIR Program { get; } = new();

        public FunctionIR AddFunction(FunctionAst function, Dictionary<string, IType> types)
        {
            var functionName = GetFunctionName(function);

            var functionIR = new FunctionIR();
            if (!function.Extern && !function.Compiler)
            {
                functionIR.Allocations = new();
                functionIR.BasicBlocks = new();

                var entryBlock = new BasicBlock();
                functionIR.BasicBlocks.Add(entryBlock);

                for (var i = 0; i < function.Arguments.Count; i++)
                {
                    var argument = function.Arguments[i];
                    if (types.TryGetValue(argument.Type.GenericName, out var type))
                    {
                        var allocationIndex = functionIR.Allocations.Count;
                        AddAllocation(functionIR, argument, type, allocationIndex);

                        var storeInstruction = new Instruction
                        {
                            Type = InstructionType.Store, Index = allocationIndex,
                            Value1 = new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i}
                        };
                        entryBlock.Instructions.Add(storeInstruction);
                    }
                }
            }

            if (functionName == "main")
            {
                Program.EntryPoint = functionIR;
            }
            else
            {
                Program.Functions[functionName] = functionIR;
            }

            return functionIR;
        }

        public FunctionIR AddOperatorOverload(OperatorOverloadAst overload, Dictionary<string, IType> types)
        {
            var functionName = GetOperatorOverloadName(overload.Type, overload.Operator);

            var entryBlock = new BasicBlock();
            var functionIR = new FunctionIR {Allocations = new(), BasicBlocks = new List<BasicBlock>{entryBlock}};

            for (var i = 0; i < overload.Arguments.Count; i++)
            {
                var argument = overload.Arguments[i];
                if (types.TryGetValue(argument.Type.GenericName, out var type))
                {
                    var allocationIndex = functionIR.Allocations.Count;
                    AddAllocation(functionIR, argument, type, allocationIndex);

                    var storeInstruction = new Instruction
                    {
                        Type = InstructionType.Store, Index = allocationIndex,
                        Value1 = new InstructionValue {ValueType = InstructionValueType.Argument, ValueIndex = i}
                    };
                    entryBlock.Instructions.Add(storeInstruction);
                }
            }

            Program.Functions[functionName] = functionIR;

            return functionIR;
        }

        public void EmitDeclaration(FunctionIR function, DeclarationAst declaration, IType type, ScopeAst scope)
        {
            if (declaration.Constant)
            {
                function.Constants ??= new();

                function.Constants[declaration.Name] = EmitIR(function, declaration.Value, scope);
            }
            else
            {
                var allocationIndex = function.Allocations.Count;
                AddAllocation(function, declaration, type, allocationIndex);

                // TODO Add initialization values
                if (declaration.Value != null)
                {
                    // value = CastValue(ExecuteExpression(declaration.Value, variables).Value, declaration.Type);
                }
                else if (declaration.ArrayValues != null)
                {
                    // value = InitializeArray(declaration.Type, variables, declaration.ArrayValues);
                }
                else
                {
                    // value = GetUninitializedValue(declaration.Type, variables, declaration.Assignments);
                }
            }
        }

        private void AddAllocation(FunctionIR function, DeclarationAst declaration, IType type, int index)
        {
            declaration.AllocationIndex = index;
            var allocation = new Allocation
            {
                Index = index, Offset = function.StackSize,
                Size = type.Size, Type = type
            };
            function.StackSize += type.Size;
            function.Allocations.Add(allocation);
        }

        public void EmitReturn(FunctionIR function, ReturnAst returnAst, IType returnType, ScopeAst scope, BasicBlock block = null)
        {
            if (block == null)
            {
                block = function.BasicBlocks[^1];
            }

            var instruction = new Instruction {Type = InstructionType.Return};
            if (returnAst.Value != null)
            {
                instruction.Value1 = EmitIR(function, returnAst.Value, scope, block);
            }

            block.Instructions.Add(instruction);
        }

        public InstructionValue EmitIR(FunctionIR function, IAst ast, ScopeAst scope, BasicBlock block = null)
        {
            if (block == null)
            {
                block = function.BasicBlocks[^1];
            }
            switch (ast)
            {
                case ConstantAst constantAst:
                    var value = new InstructionValue {ValueType = InstructionValueType.Constant, Type = constantAst.Type};
                    switch (constantAst.Type.TypeKind)
                    {
                        case TypeKind.Boolean:
                            value.ConstantValue = new InstructionConstant {Boolean = constantAst.Value == "true"};
                            break;
                        case TypeKind.String:
                            value.ConstantString = constantAst.Value;
                            break;
                        case TypeKind.Integer:
                            var integerType = constantAst.Type.PrimitiveType;
                            if (constantAst.Type.Character)
                            {
                                value.ConstantValue = new InstructionConstant {UnsignedInteger = (byte)constantAst.Value[0]};
                            }
                            else if (integerType.Signed)
                            {
                                value.ConstantValue = new InstructionConstant {Integer = long.Parse(constantAst.Value)};
                            }
                            else
                            {
                                value.ConstantValue = new InstructionConstant {UnsignedInteger = ulong.Parse(constantAst.Value)};
                            }
                            break;
                        case TypeKind.Float:
                            var floatType = constantAst.Type.PrimitiveType;
                            if (floatType.Bytes == 4)
                            {
                                value.ConstantValue = new InstructionConstant {Float = float.Parse(constantAst.Value)};
                            }
                            else
                            {
                                value.ConstantValue = new InstructionConstant {Double = double.Parse(constantAst.Value)};
                            }
                            break;
                    }
                    return value;
                case NullAst nullAst:
                    return new InstructionValue {ValueType = InstructionValueType.Null};
                case IdentifierAst identifier:
                    if (!GetScopeIdentifier(scope, identifier.Name, out var identifierAst))
                    {
                        return null;
                    }
                    if (identifierAst is DeclarationAst declaration)
                    {
                        if (declaration.Constant)
                        {
                            // TODO Get global variables and constants
                            if (function.Constants != null)
                            {
                                function.Constants.TryGetValue(declaration.Name, out var val);
                                return val;
                            }
                            return null;
                        }

                        return EmitLoad(block, declaration.AllocationIndex);
                    }
                    else if (identifierAst is IType type)
                    {
                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = _s32Type,
                            ConstantValue = new InstructionConstant {Integer = (uint)type.TypeIndex}
                        };
                    }
                    break;
                    // TODO Implement getStringPointer
                    // if (type.TypeKind == TypeKind.String)
                    // {
                    //     if (getStringPointer)
                    //     {
                    //         value = _builder.BuildStructGEP(value, 1, "stringdata");
                    //     }
                    //     value = _builder.BuildLoad(value, identifier.Name);
                    // }
                    // else if (!type.Constant)
                    // {
                    //     value = _builder.BuildLoad(value, identifier.Name);
                    // }
                case StructFieldRefAst structField:
                    if (structField.IsEnum)
                    {
                        var enumDef = (EnumAst)structField.Types[0];
                        var enumValue = enumDef.Values[structField.ValueIndices[0]].Value;

                        return new InstructionValue
                        {
                            ValueType = InstructionValueType.Constant, Type = enumDef.BaseType,
                            ConstantValue = new InstructionConstant {Integer = enumValue}
                        };
                    }
                    var structFieldPointer = EmitGetStructPointer(function, structField, scope, block, out var loaded, out var constant);
                    // if (!loaded && !constant)
                    // {
                    //     if (getStringPointer && type.TypeKind == TypeKind.String)
                    //     {
                    //         field = _builder.BuildStructGEP(field, 1, "stringdata");
                    //     }
                    //     field = _builder.BuildLoad(field, "field");
                    // }
                    // return (type, field);
                    break;
                case CallAst call:
                case ChangeByOneAst changeByOne:
                    // var constant = false;
                    // var (variableType, pointer) = changeByOne.Value switch
                    // {
                    //     IdentifierAst identifier => localVariables[identifier.Name],
                    //     StructFieldRefAst structField => BuildStructField(structField, localVariables, out _, out constant),
                    //     IndexAst index => GetIndexPointer(index, localVariables, out _),
                    //     // @Cleanup This branch should never be hit
                    //     _ => (null, new LLVMValueRef())
                    // };

                    // var value = constant ? pointer : _builder.BuildLoad(pointer, "tmpvalue");
                    // if (variableType.TypeKind == TypeKind.Pointer)
                    // {
                    //     variableType = variableType.Generics[0];
                    // }
                    // var type = ConvertTypeDefinition(variableType);

                    // LLVMValueRef newValue;
                    // if (variableType.PrimitiveType is IntegerType)
                    // {
                    //     newValue = changeByOne.Positive
                    //         ? _builder.BuildAdd(value, LLVMValueRef.CreateConstInt(type, 1, false), "inc")
                    //         : _builder.BuildSub(value, LLVMValueRef.CreateConstInt(type, 1, false), "dec");
                    // }
                    // else
                    // {
                    //     newValue = changeByOne.Positive
                    //         ? _builder.BuildFAdd(value, LLVM.ConstReal(type, 1), "incf")
                    //         : _builder.BuildFSub(value, LLVM.ConstReal(type, 1), "decf");
                    // }

                    // if (!constant) // Values are either readonly or constants, so don't store
                    // {
                    //     LLVM.BuildStore(_builder, newValue, pointer);
                    // }
                    // return changeByOne.Prefix ? (variableType, newValue) : (variableType, value);
                case UnaryAst unary:
                    // if (unary.Operator == UnaryOperator.Reference)
                    // {
                    //     var (valueType, pointer) = unary.Value switch
                    //     {
                    //         IdentifierAst identifier => localVariables[identifier.Name],
                    //         StructFieldRefAst structField => BuildStructField(structField, localVariables, out _, out _),
                    //         IndexAst index => GetIndexPointer(index, localVariables, out _),
                    //         // @Cleanup this branch should not be hit
                    //         _ => (null, new LLVMValueRef())
                    //     };
                    //     var pointerType = new TypeDefinition {Name = "*", TypeKind = TypeKind.Pointer};
                    //     if (valueType.CArray)
                    //     {
                    //         pointerType.Generics.Add(valueType.Generics[0]);
                    //     }
                    //     else
                    //     {
                    //         pointerType.Generics.Add(valueType);
                    //     }
                    //     return (pointerType, pointer);
                    // }

                    // var (type, value) = WriteExpression(unary.Value, localVariables);
                    // return unary.Operator switch
                    // {
                    //     UnaryOperator.Not => (type, _builder.BuildNot(value, "not")),
                    //     UnaryOperator.Negate => type.PrimitiveType switch
                    //     {
                    //         IntegerType => (type, _builder.BuildNeg(value, "neg")),
                    //         FloatType => (type, _builder.BuildFNeg(value, "fneg")),
                    //         // @Cleanup This branch should not be hit
                    //         _ => (null, new LLVMValueRef())
                    //     },
                    //     UnaryOperator.Dereference => (type.Generics[0], _builder.BuildLoad(value, "tmpderef")),
                    //     // @Cleanup This branch should not be hit
                    //     _ => (null, new LLVMValueRef())
                    // };
                    break;
                case IndexAst index:
                    var indexPointer = EmitGetIndexPointer(function, index, scope, block);

                    return index.CallsOverload ? indexPointer : EmitLoad(block, value: indexPointer);
                case ExpressionAst expression:
                    // var expressionValue = WriteExpression(expression.Children[0], localVariables);
                    // for (var i = 1; i < expression.Children.Count; i++)
                    // {
                    //     var rhs = WriteExpression(expression.Children[i], localVariables);
                    //     expressionValue.value = BuildExpression(expressionValue, rhs, expression.Operators[i - 1], expression.ResultingTypes[i - 1]);
                    //     expressionValue.type = expression.ResultingTypes[i - 1];
                    // }
                    // return expressionValue;
                    break;
                case TypeDefinition typeDef:
                    return new InstructionValue
                    {
                        ValueType = InstructionValueType.Constant, Type = _s32Type,
                        ConstantValue = new InstructionConstant {Integer = typeDef.TypeIndex.Value}
                    };
                case CastAst cast:
                    var castValue = EmitIR(function, cast.Value, scope);
                    var targetType = new InstructionValue {ValueType = InstructionValueType.Type, Type = cast.TargetType};
                    var castInstruction = new Instruction {Type = InstructionType.Cast, Value1 = castValue, Value2 = targetType};

                    var valueIndex = block.Instructions.Count;
                    block.Instructions.Add(castInstruction);
                    return new InstructionValue {ValueIndex = valueIndex, Type = cast.TargetType};
            }
            return null;
        }

        private InstructionValue EmitGetStructPointer(FunctionIR function, StructFieldRefAst structField, ScopeAst scope, BasicBlock block, out bool loaded, out bool constant)
        {
            loaded = false;
            constant = false;
            TypeDefinition type = null;
            InstructionValue value = null;

            switch (structField.Children[0])
            {
                case IdentifierAst identifierAst:
                    GetScopeIdentifier(scope, identifierAst.Name, out var identifier);
                    var declaration = (DeclarationAst) identifier;
                    type = declaration.Type;
                    value = EmitGetPointer(block, allocationIndex: declaration.AllocationIndex);
                    break;
                case IndexAst index:
                    value = EmitGetIndexPointer(function, index, scope, block);
                    type = value.Type;
                    if (index.CallsOverload && !structField.Pointers[0])
                    {
                        // TODO Add an allocation
                        // value = _allocationQueue.Dequeue();
                        // LLVM.BuildStore(_builder, indexValue, value);
                    }
                    break;
                case CallAst call:
                    value = EmitCall(function, call, scope, block);
                    type = value.Type;
                    if (!structField.Pointers[0])
                    {
                        // TODO Add an allocation
                        // value = _allocationQueue.Dequeue();
                        // LLVM.BuildStore(_builder, callValue, value);
                    }
                    break;
                default:
                    // @Cleanup this branch shouldn't be hit
                    Console.WriteLine("Unexpected syntax tree in struct field ref");
                    Environment.Exit(ErrorCodes.BuildError);
                    break;
            }

            var skipPointer = false;
            for (var i = 1; i < structField.Children.Count; i++)
            {
                if (structField.Pointers[i-1])
                {
                    if (!skipPointer)
                    {
                        value = EmitLoad(block, value: value);
                    }
                    // type = type.Generics[0];
                }
                skipPointer = false;

                if (type?.CArray ?? false)
                {
                    switch (structField.Children[i])
                    {
                        case IdentifierAst identifier:
                            constant = true;
                            if (identifier.Name == "length")
                            {
                                value = EmitIR(function, type.Count, scope, block);
                                type = _s32Type;
                            }
                            else
                            {
                                type = new TypeDefinition {Name = "*", TypeKind = TypeKind.Pointer, Generics = {type.Generics[0]}};
                                var index = new InstructionValue
                                {
                                    ValueType = InstructionValueType.Constant, Type = _s32Type, ConstantValue = new InstructionConstant {Integer = 0}
                                };
                                value = EmitGetPointer(block, value, index, type, true);
                            }
                            break;
                        case IndexAst index:
                            var indexValue = EmitIR(function, index.Index, scope, block);
                            type = type.Generics[0];
                            value = EmitGetPointer(block, value, indexValue, type, true);
                            break;
                    }
                    continue;
                }

                var structDefinition = (StructAst) structField.Types[i-1];
                type = structDefinition.Fields[structField.ValueIndices[i-1]].Type;

                value = EmitGetStructPointer(block, value, structField.ValueIndices[i-1]);
                switch (structField.Children[i])
                {
                    case IdentifierAst identifier:
                        break;
                    case IndexAst index:
                        value = EmitGetIndexPointer(function, index, scope, block, type, value);

                        if (index.CallsOverload)
                        {
                            skipPointer = true;
                            if (i < structField.Pointers.Length && !structField.Pointers[i])
                            {
                                // TODO Implement me
                                // var pointer = _allocationQueue.Dequeue();
                                // LLVM.BuildStore(_builder, value, pointer);
                                // value = pointer;
                            }
                            else if (i == structField.Pointers.Length)
                            {
                                loaded = true;
                            }
                        }
                        break;
                }
            }

            return value;
        }

        private InstructionValue EmitCall(FunctionIR function, CallAst call, ScopeAst scope, BasicBlock block)
        {
            var argumentCount = call.Function.Varargs ? call.Arguments.Count : call.Function.Arguments.Count;
            var arguments = new InstructionValue[argumentCount];

            if (call.Function.Params)
            {
                for (var i = 0; i < argumentCount - 1; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, block);
                    arguments[i] = argument;//CastValue(value, functionDef.Arguments[i].Type);
                }

                // Rollup the rest of the arguments into an array
                // var paramsType = call.Function.Arguments[^1].Type.Generics[0];
                // InitializeConstArray(paramsPointer, (uint)(call.Arguments.Count - functionDef.Arguments.Count + 1), paramsType);

                // var arrayData = _builder.BuildStructGEP(paramsPointer, 1, "arraydata");
                // var dataPointer = _builder.BuildLoad(arrayData, "dataptr");

                // uint paramsIndex = 0;
                // for (var i = functionDef.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                // {
                //     var pointer = _builder.BuildGEP(dataPointer, new [] {LLVMValueRef.CreateConstInt(LLVM.Int32Type(), paramsIndex, false)}, "indexptr");
                //     var (_, value) = WriteExpression(call.Arguments[i], localVariables);
                //     LLVM.BuildStore(_builder, value, pointer);
                // }

                // var paramsValue = _builder.BuildLoad(paramsPointer, "params");
                // callArguments[functionDef.Arguments.Count - 1] = paramsValue;
            }
            else if (call.Function.Varargs)
            {
                var i = 0;
                for (; i < call.Function.Arguments.Count - 1; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, block);
                    arguments[i] = argument;//CastValue(value, functionDef.Arguments[i].Type);
                }

                // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                for (; i < argumentCount; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, block);
                    // if (type.Name == "float")
                    // {
                    //     value = _builder.BuildFPExt(value, LLVM.DoubleType(), "tmpdouble");
                    // }
                    arguments[i] = argument;//CastValue(value, functionDef.Arguments[i].Type);
                }
            }
            else
            {
                for (var i = 0; i < argumentCount - 1; i++)
                {
                    var argument = EmitIR(function, call.Arguments[i], scope, block);
                    arguments[i] = argument;//CastValue(value, functionDef.Arguments[i].Type);
                }
            }

            return EmitCall(block, GetFunctionName(call.Function), arguments, call.Function.ReturnType);
        }

        private InstructionValue EmitGetIndexPointer(FunctionIR function, IndexAst index, ScopeAst scope, BasicBlock block, TypeDefinition type = null, InstructionValue variable = null)
        {
            // 1. Get the variable pointer
            if (type == null)
            {
                GetScopeIdentifier(scope, index.Name, out var identifier);
                var declaration = (DeclarationAst) identifier;
                type = declaration.Type;
                variable = EmitGetPointer(block, allocationIndex: declaration.AllocationIndex);
            }

            // 2. Determine the index
            var indexValue = EmitIR(function, index.Index, scope, block);

            // 3. Call the overload if needed
            if (index.CallsOverload)
            {
                var overloadName = GetOperatorOverloadName(type, Operator.Subscript);

                var value = EmitLoad(block, value: variable);
                // TODO Add return type?
                return EmitCall(block, overloadName, new []{value, indexValue});
            }

            // 4. Build the pointer with the first index of 0
            TypeDefinition elementType;
            if (type.TypeKind == TypeKind.String)
            {
                elementType = new TypeDefinition {Name = "u8", TypeKind = TypeKind.Integer, PrimitiveType = new IntegerType {Bytes = 1}};
            }
            else
            {
                elementType = type.Generics[0];
            }

            if (type.TypeKind == TypeKind.Pointer)
            {
                var dataPointer = EmitLoad(block, value: variable);
                return EmitGetPointer(block, dataPointer, indexValue, elementType);
            }
            else if (type.CArray)
            {
                return EmitGetPointer(block, variable, indexValue, elementType, true);
            }
            else
            {
                var data = EmitGetStructPointer(block, variable, 1);
                var dataPointer = EmitLoad(block, value: data);
                return EmitGetPointer(block, dataPointer, indexValue, elementType);
            }
        }

        private bool GetScopeIdentifier(ScopeAst scope, string name, out IAst ast)
        {
            do {
                if (scope.Identifiers.TryGetValue(name, out ast))
                {
                    return true;
                }
                scope = scope.Parent;
            } while (scope != null);
            return false;
        }

        private string GetFunctionName(FunctionAst function)
        {
            return function.Name switch
            {
                "main" => "__main",
                "__start" => "main",
                _ => function.OverloadIndex > 0 ? $"{function.Name}.{function.OverloadIndex}" : function.Name
            };
        }

        private string GetOperatorOverloadName(TypeDefinition type, Operator op)
        {
            return $"operator.{op}.{type.GenericName}";
        }

        private InstructionValue EmitLoad(BasicBlock block, int? allocationIndex = null, InstructionValue value = null)
        {
            var loadInstruction = new Instruction {Type = InstructionType.Load, Index = allocationIndex, Value1 = value};
            var loadValue = new InstructionValue {ValueIndex = block.Instructions.Count};
            block.Instructions.Add(loadInstruction);
            return loadValue;
        }

        // TODO For index value, calculate the size of the element
        private InstructionValue EmitGetPointer(BasicBlock block, InstructionValue pointer = null, InstructionValue index = null, TypeDefinition type = null, bool getFirstPointer = false, int? allocationIndex = null)
        {
            var loadInstruction = new Instruction
            {
                Type = InstructionType.GetPointer, Index = allocationIndex,
                Value1 = pointer, Value2 = index, GetFirstPointer = getFirstPointer
            };
            var loadValue = new InstructionValue {ValueIndex = block.Instructions.Count, Type = type};
            block.Instructions.Add(loadInstruction);
            return loadValue;
        }

        // TODO Add the offset size
        private InstructionValue EmitGetStructPointer(BasicBlock block, InstructionValue value, int fieldIndex)
        {
            var loadInstruction = new Instruction {Type = InstructionType.GetStructPointer, Index = fieldIndex, Value1 = value};
            var loadValue = new InstructionValue {ValueIndex = block.Instructions.Count};
            block.Instructions.Add(loadInstruction);
            return loadValue;
        }

        private InstructionValue EmitCall(BasicBlock block, string name, InstructionValue[] arguments, TypeDefinition returnType = null)
        {
            var callInstruction = new Instruction
            {
                Type = InstructionType.Call, CallFunction = name,
                Value1 = new InstructionValue {ValueType = InstructionValueType.CallArguments, Arguments = arguments}
            };
            var callValue = new InstructionValue {ValueIndex = block.Instructions.Count, Type = returnType};
            block.Instructions.Add(callInstruction);
            return callValue;
        }
    }
}
