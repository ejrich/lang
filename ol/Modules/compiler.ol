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
    X86;
    X64;
    Arm;
    Arm64;
}

set_output_architecture(OutputArchitecture arch) #compiler


///// Metaprogramming features for code exploration and generation

add_source_file(string file) #compiler

string get_compiler_working_directory() #compiler

report_error(string error) #compiler

// Ast definitions
enum AstType {
    None;
    Function;
    OperatorOverload;
    Enum;
    Struct;
    Union;
    Interface;
}

struct CompilerAst {
    type: AstType;
    file: string;
    line: u32;
    column: u32;
}

struct FunctionAst : CompilerAst {
    name: string;
    return_type: TypeInfo*;
    arguments: Array<ArgumentType>;
    attributes: Array<string>;
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
