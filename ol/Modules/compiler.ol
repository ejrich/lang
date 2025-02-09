//------------------- Compiler module --------------------

///// Build configurations and options

enum BuildEnv : u8 {
    None;
    Debug;
    Release;
    Other;
}

build_env: BuildEnv; #const

enum LinkerType : u8 {
    Static;
    Dynamic;
}

set_linker(LinkerType linker) #compiler

set_executable_name(string name) #compiler

enum OutputTypeTableConfiguration : u8 {
    Full;
    Used;
    None;
}

set_output_type_table(OutputTypeTableConfiguration config) #compiler

set_output_directory(string path) #compiler

add_library_directory(string path) #compiler

copy_to_output_directory(string file) #compiler

enum OutputArchitecture : u8 {
    None;
    X64;
    Arm64;
}

set_output_architecture(OutputArchitecture arch) #compiler

#if os == OS.Windows {
    enum WindowsSubsystem {
        Console;
        Windows;
    }

    set_windows_subsystem(WindowsSubsystem subsystem) #compiler
}

struct ReleaseOptimizations {
    loop_interleaving: bool;
    loop_vectorization: bool;
    loop_unrolling: bool;
    forget_scalar_evolution_in_loop_unrolling: bool;
    instruction_vectorization: bool;
    profile_call_graph: bool;
    merge_functions: bool;
    inline_threshold: InlineThreshold;
    global_value_numbering: bool;
    combine_redudant_instruction: bool;
    promote_memory_to_register: bool;
    simplify_control_flow: bool;
}

enum InlineThreshold : u8 {
    Default = 0;
    Small   = 1;
    Minimum = 2;
}

get_current_optimizations(ReleaseOptimizations* optimizations) #compiler

set_optimizations(ReleaseOptimizations* optimizations) #compiler


///// Metaprogramming features for code exploration and generation

add_source_file(string file) #compiler

string get_compiler_working_directory() #compiler

report_error(string error, CompilerAst* ast = null) #compiler

// Ast definitions
enum AstType {
    None;
    Function;
    OperatorOverload;
    Enum;
    Struct;
    Union;
    Interface;
    GlobalVariable;
}

struct CompilerAst {
    type: AstType;
    file: string;
    line: u32;
    column: u32;
}

struct FunctionAst : CompilerAst {
    name: string;
    flags: FunctionFlags;
    return_type: TypeInfo*;
    arguments: Array<ArgumentType>;
    attributes: Array<string>;
}

[flags]
enum FunctionFlags {
    None     = 0x0;
    Extern   = 0x1;
    Compiler = 0x2;
    Syscall  = 0x4;
    Varargs  = 0x8;
    Inline   = 0x4000;
}

struct EnumAst : CompilerAst {
    name: string;
    base_type: TypeInfo*;
    values: Array<EnumValue>;
    attributes: Array<string>;
}

struct StructAst : CompilerAst {
    name: string;
    fields: Array<TypeField>;
    attributes: Array<string>;
}

struct UnionAst : CompilerAst {
    name: string;
    fields: Array<EnumValue>;
}

struct InterfaceAst : CompilerAst {
    name: string;
    return_type: TypeInfo*;
    arguments: Array<ArgumentType>;
}

struct GlobalVariableAst : CompilerAst {
    name: string;
    variable_type: TypeInfo*;
}

// Compiler messaging infrastructure
enum CompilerMessageType {
    ReadyToBeTypeChecked = 0;
    TypeCheckSuccessful;
    TypeCheckFailed;
    IRGenerated;
    ReadyForCodeGeneration;
    CodeGenerationFailed;
    CodeGenerated;
    ExecutableLinked;
}

union CompilerMessageValue {
    ast: CompilerAst*;
    name: string;
}

struct CompilerMessage {
    type: CompilerMessageType;
    value: CompilerMessageValue;
}

bool intercept_compiler_messages() #compiler

bool get_next_compiler_message(CompilerMessage* message) #compiler

// Functions for code insertion and modification
FunctionAst* get_function(string name) #compiler

insert_code(FunctionAst* function, string code) #compiler

add_code(string code) #compiler

set_global_variable_value(GlobalVariableAst* global, string value) #compiler
