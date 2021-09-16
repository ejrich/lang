using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Lang.Runner
{
    public class ProgramRunner
    {
        public void RunProgram()
        {
            var name = new AssemblyName("ExternFunctions");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
            var modelBuilder = assemblyBuilder.DefineDynamicModule("ExternFunctions");
            var typeBuilder = modelBuilder.DefineType("Functions", TypeAttributes.Class | TypeAttributes.Public);

            // CreateMethod(typeBuilder, "SDL_Init", "SDL2", typeof(int), typeof(int));
            // CreateMethod(typeBuilder, "SDL_CreateWindow", "SDL2", null, typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(uint));
            // CreateMethod(typeBuilder, "cosf", "libm", typeof(float), typeof(float));
            CreateMethod(typeBuilder, "puts", "libc", null, typeof(string));

            var type = typeBuilder.CreateType();

            var classObject = type!.GetConstructor(Type.EmptyTypes)!.Invoke(new object[]{});
            // var cos = type.GetMethod("cosf");
            var puts = type.GetMethod("puts");

            puts!.Invoke(classObject, new[] {"Hello world"});
            // var cosine = cos!.Invoke(classObject, new object[]{3.2f});
        }

        private static void CreateMethod(TypeBuilder typeBuilder, string name, string library, Type returnType, params Type[] args)
        {
            var method = typeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, args);
            var caBuilder = new CustomAttributeBuilder(typeof(DllImportAttribute).GetConstructor(new []{typeof(string)}), new []{library});
            method.SetCustomAttribute(caBuilder);
        }
    }
}
