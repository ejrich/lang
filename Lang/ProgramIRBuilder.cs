using System.Collections.Generic;

namespace Lang
{
    public interface IProgramIRBuilder
    {
        ProgramIR Program { get; }
        FunctionIR AddFunction(FunctionAst function, Dictionary<string, IType> types);
        FunctionIR AddOperatorOverload(OperatorOverloadAst overload, Dictionary<string, IType> types);
        void EmitDeclaration(FunctionIR function, DeclarationAst declaration, IType type);
        InstructionValue EmitIR(FunctionIR function, IAst ast, BasicBlock block = null);
    }

    public class ProgramIRBuilder : IProgramIRBuilder
    {
        private readonly TypeDefinition _s32Type = new() {Name = "s32", PrimitiveType = new IntegerType {Bytes = 4, Signed = true}};

        public ProgramIR Program { get; } = new();

        public FunctionIR AddFunction(FunctionAst function, Dictionary<string, IType> types)
        {
            var functionName = function.Name switch
            {
                "main" => "__main",
                "__start" => "main",
                _ => function.OverloadIndex > 0 ? $"{function.Name}.{function.OverloadIndex}" : function.Name
            };

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

        public void EmitDeclaration(FunctionIR function, DeclarationAst declaration, IType type)
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

        public InstructionValue EmitIR(FunctionIR function, IAst ast, BasicBlock block = null)
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
                                value.ConstantValue = new InstructionConstant {Integer = ulong.Parse(constant.Value)};
                            }
                            else
                            {
                                value.ConstantValue = new InstructionConstant {Integer = (ulong)long.Parse(constant.Value)};
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
                // {
                //     if (!localVariables.TryGetValue(identifier.Name, out var typeValue))
                //     {
                //         var typeDef = _programGraph.Types[identifier.Name];
                //         return (_s32Type, LLVMValueRef.CreateConstInt(LLVM.Int32Type(), (uint)typeDef.TypeIndex, false));
                //     }
                //     var (type, value) = typeValue;
                //     if (type.TypeKind == TypeKind.String)
                //     {
                //         if (getStringPointer)
                //         {
                //             value = _builder.BuildStructGEP(value, 1, "stringdata");
                //         }
                //         value = _builder.BuildLoad(value, identifier.Name);
                //     }
                //     else if (!type.Constant)
                //     {
                //         value = _builder.BuildLoad(value, identifier.Name);
                //     }
                //     return (type, value);
                // }
                case StructFieldRefAst structField:
                // {
                //     if (structField.IsEnum)
                //     {
                //         var enumName = structField.TypeNames[0];
                //         var enumDef = (EnumAst)_programGraph.Types[enumName];
                //         var value = enumDef.Values[structField.ValueIndices[0]].Value;
                //         return (enumDef.BaseType, LLVMValueRef.CreateConstInt(GetIntegerType(enumDef.BaseType.PrimitiveType), (ulong)value, false));
                //     }
                //     var (type, field) = BuildStructField(structField, localVariables, out var loaded, out var constant);
                //     if (!loaded && !constant)
                //     {
                //         if (getStringPointer && type.TypeKind == TypeKind.String)
                //         {
                //             field = _builder.BuildStructGEP(field, 1, "stringdata");
                //         }
                //         field = _builder.BuildLoad(field, "field");
                //     }
                //     return (type, field);
                // }
                case CallAst call:
                    // var functions = _programGraph.Functions[call.Function];
                    // LLVMValueRef function;
                    // if (call.Function == "main")
                    // {
                    //     function = _module.GetNamedFunction("__main");
                    // }
                    // else
                    // {
                    //     var functionName = GetFunctionName(call.Function, call.FunctionIndex, functions.Count);
                    //     function = _module.GetNamedFunction(functionName);
                    // }
                    // var functionDef = functions[call.FunctionIndex];

                    // if (functionDef.Params)
                    // {
                    //     var callArguments = new LLVMValueRef[functionDef.Arguments.Count];
                    //     for (var i = 0; i < functionDef.Arguments.Count - 1; i++)
                    //     {
                    //         var value = WriteExpression(call.Arguments[i], localVariables);
                    //         callArguments[i] = CastValue(value, functionDef.Arguments[i].Type);
                    //     }

                    //     // Rollup the rest of the arguments into an array
                    //     var paramsType = functionDef.Arguments[^1].Type.Generics[0];
                    //     var paramsPointer = _allocationQueue.Dequeue();
                    //     InitializeConstArray(paramsPointer, (uint)(call.Arguments.Count - functionDef.Arguments.Count + 1), paramsType);

                    //     var arrayData = _builder.BuildStructGEP(paramsPointer, 1, "arraydata");
                    //     var dataPointer = _builder.BuildLoad(arrayData, "dataptr");

                    //     uint paramsIndex = 0;
                    //     for (var i = functionDef.Arguments.Count - 1; i < call.Arguments.Count; i++, paramsIndex++)
                    //     {
                    //         var pointer = _builder.BuildGEP(dataPointer, new [] {LLVMValueRef.CreateConstInt(LLVM.Int32Type(), paramsIndex, false)}, "indexptr");
                    //         var (_, value) = WriteExpression(call.Arguments[i], localVariables);
                    //         LLVM.BuildStore(_builder, value, pointer);
                    //     }

                    //     var paramsValue = _builder.BuildLoad(paramsPointer, "params");
                    //     callArguments[functionDef.Arguments.Count - 1] = paramsValue;
                    //     return (functionDef.ReturnType, _builder.BuildCall(function, callArguments, string.Empty));
                    // }
                    // else if (functionDef.Varargs)
                    // {
                    //     var callArguments = new LLVMValueRef[call.Arguments.Count];
                    //     for (var i = 0; i < functionDef.Arguments.Count - 1; i++)
                    //     {
                    //         var value = WriteExpression(call.Arguments[i], localVariables, functionDef.Extern);
                    //         callArguments[i] = CastValue(value, functionDef.Arguments[i].Type);
                    //     }

                    //     // In the C99 standard, calls to variadic functions with floating point arguments are extended to doubles
                    //     // Page 69 of http://www.open-std.org/jtc1/sc22/wg14/www/docs/n1256.pdf
                    //     for (var i = functionDef.Arguments.Count - 1; i < call.Arguments.Count; i++)
                    //     {
                    //         var (type, value) = WriteExpression(call.Arguments[i], localVariables, functionDef.Extern);
                    //         if (type.Name == "float")
                    //         {
                    //             value = _builder.BuildFPExt(value, LLVM.DoubleType(), "tmpdouble");
                    //         }
                    //         callArguments[i] = value;
                    //     }

                    //     return (functionDef.ReturnType, _builder.BuildCall(function, callArguments, string.Empty));
                    // }
                    // else
                    // {
                    //     var callArguments = new LLVMValueRef[call.Arguments.Count];
                    //     for (var i = 0; i < call.Arguments.Count; i++)
                    //     {
                    //         var value = WriteExpression(call.Arguments[i], localVariables, functionDef.Extern);
                    //         callArguments[i] = CastValue(value, functionDef.Arguments[i].Type);
                    //     }
                    //     return (functionDef.ReturnType, _builder.BuildCall(function, callArguments, string.Empty));
                    // }
                case ChangeByOneAst changeByOne:
                // {
                //     var constant = false;
                //     var (variableType, pointer) = changeByOne.Value switch
                //     {
                //         IdentifierAst identifier => localVariables[identifier.Name],
                //         StructFieldRefAst structField => BuildStructField(structField, localVariables, out _, out constant),
                //         IndexAst index => GetIndexPointer(index, localVariables, out _),
                //         // @Cleanup This branch should never be hit
                //         _ => (null, new LLVMValueRef())
                //     };

                //     var value = constant ? pointer : _builder.BuildLoad(pointer, "tmpvalue");
                //     if (variableType.TypeKind == TypeKind.Pointer)
                //     {
                //         variableType = variableType.Generics[0];
                //     }
                //     var type = ConvertTypeDefinition(variableType);

                //     LLVMValueRef newValue;
                //     if (variableType.PrimitiveType is IntegerType)
                //     {
                //         newValue = changeByOne.Positive
                //             ? _builder.BuildAdd(value, LLVMValueRef.CreateConstInt(type, 1, false), "inc")
                //             : _builder.BuildSub(value, LLVMValueRef.CreateConstInt(type, 1, false), "dec");
                //     }
                //     else
                //     {
                //         newValue = changeByOne.Positive
                //             ? _builder.BuildFAdd(value, LLVM.ConstReal(type, 1), "incf")
                //             : _builder.BuildFSub(value, LLVM.ConstReal(type, 1), "decf");
                //     }

                //     if (!constant) // Values are either readonly or constants, so don't store
                //     {
                //         LLVM.BuildStore(_builder, newValue, pointer);
                //     }
                //     return changeByOne.Prefix ? (variableType, newValue) : (variableType, value);
                // }
                // case UnaryAst unary:
                // {
                //     if (unary.Operator == UnaryOperator.Reference)
                //     {
                //         var (valueType, pointer) = unary.Value switch
                //         {
                //             IdentifierAst identifier => localVariables[identifier.Name],
                //             StructFieldRefAst structField => BuildStructField(structField, localVariables, out _, out _),
                //             IndexAst index => GetIndexPointer(index, localVariables, out _),
                //             // @Cleanup this branch should not be hit
                //             _ => (null, new LLVMValueRef())
                //         };
                //         var pointerType = new TypeDefinition {Name = "*", TypeKind = TypeKind.Pointer};
                //         if (valueType.CArray)
                //         {
                //             pointerType.Generics.Add(valueType.Generics[0]);
                //         }
                //         else
                //         {
                //             pointerType.Generics.Add(valueType);
                //         }
                //         return (pointerType, pointer);
                //     }

                //     var (type, value) = WriteExpression(unary.Value, localVariables);
                //     return unary.Operator switch
                //     {
                //         UnaryOperator.Not => (type, _builder.BuildNot(value, "not")),
                //         UnaryOperator.Negate => type.PrimitiveType switch
                //         {
                //             IntegerType => (type, _builder.BuildNeg(value, "neg")),
                //             FloatType => (type, _builder.BuildFNeg(value, "fneg")),
                //             // @Cleanup This branch should not be hit
                //             _ => (null, new LLVMValueRef())
                //         },
                //         UnaryOperator.Dereference => (type.Generics[0], _builder.BuildLoad(value, "tmpderef")),
                //         // @Cleanup This branch should not be hit
                //         _ => (null, new LLVMValueRef())
                //     };
                // }
                case IndexAst index:
                // {
                //     var (elementType, elementValue) = GetIndexPointer(index, localVariables, out var loaded);
                //     if (!loaded)
                //     {
                //         elementValue = _builder .BuildLoad(elementValue, "tmpindex");
                //     }
                //     return (elementType, elementValue);
                // }
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
                    var typeIndex = new InstructionConstant {Integer = (uint)typeDef.TypeIndex};
                    return new InstructionValue {ValueType = InstructionValueType.Constant, Type = _s32Type, ConstantValue = typeIndex};
                case CastAst cast:
                    var value = EmitIR(function, cast.Value);
                    var targetType = new InstructionValue {ValueType = InstructionValueType.Type, Type = cast.TargetType};
                    var instruction = new Instruction {Type = InstructionType.Cast, Value1 = value, Value2 = targetType};

                    var valueIndex = block.Instructions.Count;
                    block.Instructions.Add(instruction);
                    return new InstructionValue {ValueType = InstructionValueType.Value, ValueIndex = valueIndex};
            }
            return null;
        }
    }
}
