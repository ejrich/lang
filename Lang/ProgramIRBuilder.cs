using System.Collections.Generic;

namespace Lang
{
    public interface IProgramIRBuilder
    {
        ProgramIR Program { get; }
        FunctionIR AddFunction(FunctionAst function, Dictionary<string, IType> types);
        FunctionIR AddOperatorOverload(OperatorOverloadAst overload, Dictionary<string, IType> types);
        void EmitDeclaration(FunctionIR function, DeclarationAst declaration, IType type);
        void EmitIR(FunctionIR function, IAst ast);
    }

    public class ProgramIRBuilder : IProgramIRBuilder
    {
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
                            Value1 = new InstructionValue {Type = InstructionValueType.Argument, ValueIndex = i}
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
                        Value1 = new InstructionValue {Type = InstructionValueType.Argument, ValueIndex = i}
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

        public void EmitIR(FunctionIR function, IAst ast)
        {
        }
    }
}
