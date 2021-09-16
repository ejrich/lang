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
                            Type = InstructionType.Store, AllocationIndex = allocationIndex,
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
            var functionName = $"operator.{overload.Operator}.{overload.Type.GenericName}";

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
                        Type = InstructionType.Store, AllocationIndex = allocationIndex,
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
                case ConstantAst constant:
                    var value = new InstructionValue {ValueType = InstructionValueType.Constant, Type = constant.Type};
                    switch (constant.Type.TypeKind)
                    {
                        case TypeKind.Boolean:
                            value.ConstantValue = new InstructionConstant {Boolean = constant.Value == "true"};
                            break;
                        case TypeKind.String:
                            value.ConstantString = constant.Value;
                            break;
                        case TypeKind.Integer:
                            var integerType = constant.Type.PrimitiveType;
                            if (constant.Type.Character)
                            {
                                value.ConstantValue = new InstructionConstant {Integer = (byte)constant.Value[0]};
                            }
                            else if (integerType.Bytes == 8 && !integerType.Signed)
                            {
                                value.ConstantValue = new InstructionConstant {UnsignedInteger = ulong.Parse(constant.Value)};
                            }
                            else
                            {
                                value.ConstantValue = new InstructionConstant {Integer = long.Parse(constant.Value)};
                            }
                            break;
                        case TypeKind.Float:
                            var floatType = constant.Type.PrimitiveType;
                            if (floatType.Bytes == 4)
                            {
                                value.ConstantValue = new InstructionConstant {Float = float.Parse(constant.Value)};
                            }
                            else
                            {
                                value.ConstantValue = new InstructionConstant {Double = double.Parse(constant.Value)};
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

                        return EmitLoad(declaration.AllocationIndex, block);
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
                    var structFieldPointer = EmitGetStructPointer(function, structField, scope, block);
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

                    var callInstruction = new Instruction
                    {
                        Type = InstructionType.Call, CallFunction = GetFunctionName(call.Function),
                        Value1 = new InstructionValue {ValueType = InstructionValueType.CallArguments, Arguments = arguments}
                    };
                    var callValue = new InstructionValue {ValueIndex = block.Instructions.Count, Type = call.Function.ReturnType};
                    block.Instructions.Add(callInstruction);
                    return callValue;
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
                    var indexPointer = EmitGetIndexPointer(function, index, scope, block, out var loaded);
                    if (!loaded)
                    {
                        // elementValue = _builder.BuildLoad(elementValue, "tmpindex");
                    }
                    return null;
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

        private InstructionValue EmitLoad(int allocationIndex, BasicBlock block)
        {
            var loadInstruction = new Instruction {Type = InstructionType.Load, AllocationIndex = allocationIndex};
            var loadValue = new InstructionValue {ValueIndex = block.Instructions.Count};
            block.Instructions.Add(loadInstruction);
            return loadValue;
        }

        private InstructionValue EmitGetStructPointer(FunctionIR function, StructFieldRefAst structField, ScopeAst scope, BasicBlock block)
        {
            switch (structField.Children[0])
            {
                // case IdentifierAst identifier:
                //     (type, value) = localVariables[identifier.Name];
                //     break;
                // case IndexAst index:
                //     var (indexType, indexValue) = GetIndexPointer(index, localVariables, out _);
                //     type = indexType;
                //     if (index.CallsOverload && !structField.Pointers[0])
                //     {
                //         value = _allocationQueue.Dequeue();
                //         LLVM.BuildStore(_builder, indexValue, value);
                //     }
                //     else
                //     {
                //         value = indexValue;
                //     }
                //     break;
                // case CallAst call:
                //     var (callType, callValue) = WriteExpression(call, localVariables);
                //     type = callType;
                //     if (structField.Pointers[0])
                //     {
                //         value = callValue;
                //     }
                //     else
                //     {
                //         value = _allocationQueue.Dequeue();
                //         LLVM.BuildStore(_builder, callValue, value);
                //     }
                //     break;
                default:
                    // @Cleanup this branch shouldn't be hit
                    // Console.WriteLine("Unexpected syntax tree in struct field ref");
                    // Environment.Exit(ErrorCodes.BuildError);
                    break;
            }

            var skipPointer = false;
            for (var i = 1; i < structField.Children.Count; i++)
            {
                if (structField.Pointers[i-1])
                {
                    if (!skipPointer)
                    {
                        // value = _builder.BuildLoad(value, "pointerval");
                    }
                    // type = type.Generics[0];
                }
                skipPointer = false;

                // if (type.CArray)
                // {
                //     switch (structField.Children[i])
                //     {
                //         case IdentifierAst identifier:
                //             constant = true;
                //             if (identifier.Name == "length")
                //             {
                //                 (type, value) = WriteExpression(type.Count, localVariables);
                //             }
                //             else
                //             {
                //                 type = new TypeDefinition {Name = "*", TypeKind = TypeKind.Pointer, Generics = {type.Generics[0]}};
                //                 value = _builder.BuildGEP(value, new []{_zeroInt, _zeroInt}, "dataPtr");
                //             }
                //             break;
                //         case IndexAst index:
                //             var (_, indexValue) = WriteExpression(index.Index, localVariables);
                //             value = _builder.BuildGEP(value, new []{_zeroInt, indexValue}, "indexptr");
                //             type = type.Generics[0];
                //             break;
                //     }
                //     continue;
                // }

                var structDefinition = (StructAst) structField.Types[i-1];
                // type = structDefinition.Fields[structField.ValueIndices[i-1]].Type;

                switch (structField.Children[i])
                {
                    case IdentifierAst identifier:
                        // value = _builder.BuildStructGEP(value, (uint)structField.ValueIndices[i-1], identifier.Name);
                        break;
                    case IndexAst index:
                        // value = _builder.BuildStructGEP(value, (uint)structField.ValueIndices[i-1], index.Name);
                        // (type, value) = GetIndexPointer(index, localVariables, out _, type, value);

                        if (index.CallsOverload)
                        {
                            skipPointer = true;
                            if (i < structField.Pointers.Length && !structField.Pointers[i])
                            {
                                // var pointer = _allocationQueue.Dequeue();
                                // LLVM.BuildStore(_builder, value, pointer);
                                // value = pointer;
                            }
                            else if (i == structField.Pointers.Length)
                            {
                                // loaded = true;
                            }
                        }
                        break;
                }
            }

            return null;
        }

        private InstructionValue EmitGetIndexPointer(FunctionIR function, IndexAst index, ScopeAst scope, BasicBlock block, out bool loaded, TypeDefinition type = null, InstructionValue variable = null)
        {
            // 1. Get the variable pointer
            if (type == null)
            {
                GetScopeIdentifier(scope, index.Name, out var identifier);
                var declaration = (DeclarationAst) identifier;
                type = declaration.Type;
            }

            // 2. Determine the index
            var indexValue = EmitIR(function, index.Index, scope, block);

            // 3. Call the overload if needed
            if (index.CallsOverload)
            {
                // TODO Implement me
                // var overloadName = GetOperatorOverloadName(type, Operator.Subscript);
                // var overload = _module.GetNamedFunction(overloadName);
                // var overloadDef = _programGraph.OperatorOverloads[type.GenericName][Operator.Subscript];

                // loaded = true;
                // return (overloadDef.ReturnType, _builder.BuildCall(overload, new []{_builder.BuildLoad(variable, index.Name), indexValue}, string.Empty));
            }

            // 4. Build the pointer with the first index of 0
            TypeDefinition elementType;
            if (type.TypeKind == TypeKind.String)
            {
                // _stringStruct ??= (StructAst)_programGraph.Types["string"];
                // elementType = _stringStruct.Fields[1].Type.Generics[0];
                elementType = new TypeDefinition {Name = "u8", TypeKind = TypeKind.Integer, PrimitiveType = new IntegerType {Bytes = 1}};
            }
            else
            {
                elementType = type.Generics[0];
            }
            if (type.TypeKind == TypeKind.Pointer)
            {
                // var dataPointer = _builder.BuildLoad(variable, "dataptr");
                // indexPointer = _builder.BuildGEP(dataPointer, new []{indexValue}, "indexptr");
            }
            else if (type.CArray)
            {
                // indexPointer = _builder.BuildGEP(variable, new []{_zeroInt, indexValue}, "dataptr");
            }
            else
            {
                // var arrayData = _builder.BuildStructGEP(variable, 1, "arraydata");
                // var dataPointer = _builder.BuildLoad(arrayData, "dataptr");
                // indexPointer = _builder.BuildGEP(dataPointer, new [] {indexValue}, "indexptr");
            }
            loaded = false;
            return null;
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
    }
}
