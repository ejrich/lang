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

os: OS;
build_env: BuildEnv;

add_dependency(string library) #compiler
