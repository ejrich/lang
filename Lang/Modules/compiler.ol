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

add_dependency(string library) #compiler
