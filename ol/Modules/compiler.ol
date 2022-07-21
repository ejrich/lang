//------------------- Compiler module --------------------

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

// insert_code(Scope* scope, Code code) #compiler
