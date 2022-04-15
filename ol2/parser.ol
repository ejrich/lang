#import "ast.ol"
#import "lexer.ol"

library_directory: string;

asts: LinkedList<Ast*>;
directives: LinkedList<CompilerDirectiveAst*>;

parse(string entrypoint) {
    init_lexer();
    set_library_directory();

    add_module("runtime");
    queue_file_if_not_exists(entrypoint);

    complete_work();
}

set_library_directory() {
    executable_path: CArray<u8>[4096];
    #if os == OS.Linux {
        self_path := "/proc/self/exe"; #const
        bytes := readlink(self_path.data, &executable_path, 4095);
        dir_char := '/'; #const
    }
    #if os == OS.Windows {
        bytes := GetModuleFileNameA(null, &executable_path, 4096);
        dir_char := '\\'; #const
    }

    length := 0;
    each i in 1..bytes-1 {
        if executable_path[i] == dir_char {
            length = i;
        }
    }

    executable_str: string = { length = length; data = &executable_path; }
    library_directory = format_string("%/%", allocate, executable_str, "../../ol/Modules");
}

add_module(string module) {
    file := format_string("%/%.ol", allocate, library_directory, module);
    if file_exists(file)
        queue_file_if_not_exists(file);
}

add_module(string module, int fileIndex, Token token) {
    file := format_string("%/%.ol", allocate, library_directory, module);
    if file_exists(file)
        queue_file_if_not_exists(file);
    else
        report_error("Undefined module '%'", fileIndex, token, module);
}

// add_module(CompilerDirectiveAst* module) {
//     if file_exists(module.import.path)
//         queue_file_if_not_exists(module.Import.Path);
//     else
//         report_error("Undefined module '%'", module, module.import.name);
// }

add_file(string file, string directory, int fileIndex, Token token) {
    file_path := format_string("%/%", directory, file);
    if file_exists(file_path)
        queue_file_if_not_exists(file_path);
    else
        report_error("File '%' does not exist", fileIndex, token, file);
}

// add_file(CompilerDirectiveAst* import) {
//     if file_exists(import.import.path)
//         queue_file_if_not_exists(import.Import.Path);
//     else
//         report_error("File '%' does not exist", import, import.import.name);
// }

queue_file_if_not_exists(string file) {
    each file_name in file_names {
        if file_name == file return;
    }

    file_index := file_names.length;
    array_insert(&file_names, file, allocate, reallocate);
    // TypeChecker.PrivateScopes.Add(null);

    data := new<ParseData>();
    data.file = file;
    data.file_index = file_index;
    queue_work(parse_file, data);
}

struct ParseData {
    file: string;
    file_index: int;
}

#private

struct TokenEnumerator {
    tokens: Array<Token>;
    index: int;
    file_index: int;
    current: Token;
    last: Token;
    remaining := true;
    private: bool;
}

bool move_next(TokenEnumerator* enumerator) {
    if (enumerator.tokens.length > enumerator.index)
    {
        enumerator.current = enumerator.tokens[enumerator.index++];
        return true;
    }
    enumerator.remaining = false;
    return false;
}

bool move(TokenEnumerator* enumerator, int steps) {
    enumerator.index += steps;
    if (enumerator.tokens.length > enumerator.index)
    {
        enumerator.current = enumerator.tokens[enumerator.index];
        return true;
    }
    return false;
}

bool peek(TokenEnumerator* enumerator, Token* token, int steps = 0) {
    if (enumerator.tokens.length > enumerator.index + steps)
    {
        *token = enumerator.tokens[enumerator.index + steps];
        return true;
    }
    *token = enumerator.last;
    return false;
}

insert(TokenEnumerator* enumerator, Token token) {
    array_insert(&enumerator.tokens, enumerator.index, token, allocate, reallocate);
}

parse_file(void* data) {
    parse_data: ParseData* = data;
    file_index := parse_data.file_index;

    print("Parsing file: %\n", parse_data.file);
    tokens := load_file_tokens(parse_data.file, file_index);
}
