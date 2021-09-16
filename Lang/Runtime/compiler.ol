//------------------- Compiler module --------------------

enum OS {
    None;
    Linux;
    Windows; // Not supported
    Mac;     // Not supported
}

enum BuildEnv {
    None;
    Debug;
    Release;
    Other;
}

os: OS; #const
build_env: BuildEnv; #const

add_dependency(string library) #compiler
