using System;
using System.Collections.Generic;

namespace Lang
{
    public static class TypeTable
    {
        public static int Count { get; set; }
        public static Dictionary<string, IType> Types { get; } = new();

        public static int Add(string name, IType type)
        {
            Types[name] = type;
            return Count++;
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
