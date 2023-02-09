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

report_error(string error) #compiler

struct CompilerAst {
    file: string;
    line: u32;
    column: u32;
}

struct Function : CompilerAst {
    name: string;
}

enum CompilerMessageType {
    ReadyToBeTypeChecked = 0;
    TypeCheckFailed;
    IRGenerated;
    ReadyForCodeGeneration;
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

Function* get_function(string name) #compiler

insert_code(Function* function, string code) #compiler

add_code(string code) #compiler
