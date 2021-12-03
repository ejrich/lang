//------------------- Compiler module --------------------

enum OS : u8 {
    None;
    Linux;
    Windows; // Not supported
    Mac;     // Not supported
}

enum BuildEnv : u8 {
    None;
    Debug;
    Release;
    Other;
}

os: OS; #const
build_env: BuildEnv; #const

enum LinkerType : u8 {
    Static;
    Dynamic;
}

set_linker(LinkerType linker) #compiler

set_executable_name(string name) #compiler

add_build_file(string name) #compiler

enum OutputTypeTableConfiguration : u8 {
    Full;
    Used;
    None;
}

set_output_type_table(OutputTypeTableConfiguration config) #compiler
