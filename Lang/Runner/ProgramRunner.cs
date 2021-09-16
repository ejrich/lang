using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Lang.Parsing;

namespace Lang.Runner
{
    public interface IProgramRunner
    {
        void RunProgram(ProgramGraph programGraph);
    }

    public class ProgramRunner : IProgramRunner
    {
        public void RunProgram(ProgramGraph programGraph)
        {
            if (!programGraph.Directives.Any()) return;

            var name = new AssemblyName("ExternFunctions");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
            var modelBuilder = assemblyBuilder.DefineDynamicModule("ExternFunctions");
            var typeBuilder = modelBuilder.DefineType("Functions", TypeAttributes.Class | TypeAttributes.Public);

            foreach (var types in programGraph.Data.Types)
            {
                // TODO Make structs as the types
            }

            foreach (var function in programGraph.Functions.Where(_ => _.Extern))
            {
                if (function.Varargs)
                {
                    // TODO For varargs functions, get usages instead of only using the definition
                    continue;
                }

                var returnType = GetTypeFromDefinition(function.ReturnType);
                var args = function.Arguments.Select(arg => GetTypeFromDefinition(arg.Type)).ToArray();
                CreateFunction(typeBuilder, function.Name, function.ExternLib, returnType, args);
            }

            CreateFunction(typeBuilder, "printf", "libc", null, typeof(string), typeof(int));

            var type = typeBuilder.CreateType();

            var classObject = type!.GetConstructor(Type.EmptyTypes)!.Invoke(new object[]{});
        }

        private static void CreateFunction(TypeBuilder typeBuilder, string name, string library, Type returnType, params Type[] args)
        {
            var method = typeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, args);
            var caBuilder = new CustomAttributeBuilder(typeof(DllImportAttribute).GetConstructor(new []{typeof(string)}), new []{library});
            method.SetCustomAttribute(caBuilder);
        }

        private static Type GetTypeFromDefinition(TypeDefinition typeDef)
        {
            switch (typeDef.PrimitiveType)
            {
                case IntegerType integerType:
                    if (integerType.Signed)
                    {
                        return integerType.Bytes switch
                        {
                            1 => typeof(sbyte),
                            2 => typeof(short),
                            4 => typeof(int),
                            8 => typeof(long),
                            _ => typeof(int)
                        };
                    }
                    return integerType.Bytes switch
                    {
                        1 => typeof(byte),
                        2 => typeof(ushort),
                        4 => typeof(uint),
                        8 => typeof(ulong),
                        _ => typeof(uint)
                    };
                case FloatType floatType:
                    return floatType.Bytes == 4 ? typeof(float) : typeof(double);
            }

            switch (typeDef.Name)
            {
                case "string":
                    return typeof(string);
            }

            return null;
        }
    }
}
