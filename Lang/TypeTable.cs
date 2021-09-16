using System;
using System.Collections.Generic;

namespace Lang
{
    public static class TypeTable
    {
        public static int Count { get; set; }
        public static Dictionary<string, IType> Types { get; } = new();
        public static Dictionary<string, List<FunctionAst>> Functions { get; } = new();

        public static bool Add(string name, IType type)
        {
            type.TypeIndex = Count++;
            return Types.TryAdd(name, type);
        }

        public static List<FunctionAst> AddFunction(string name, FunctionAst function)
        {
            if (!Functions.TryGetValue(function.Name, out var functions))
            {
                Functions[function.Name] = functions = new List<FunctionAst>();
            }
            function.TypeIndex = Count++;
            function.OverloadIndex = functions.Count;
            functions.Add(function);

            return functions;
        }

        public static IType GetType(TypeDefinition typeDef)
        {
            var typeName = typeDef.Name switch
            {
                "Type" => "s32",
                "Params" => $"Array.{typeDef.Generics[0].GenericName}",
                _ => typeDef.GenericName
            };

            Types.TryGetValue(typeName, out var type);
            return type;
        }
    }
}
