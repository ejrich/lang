#import "ast.ol"
#import "lexer.ol"

library_directory: string;

parse(string entrypoint) {
    init_lexer();

    tokens := load_file_tokens(entrypoint, 0);
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

// add_module(CompilerDirectiveAst module) {
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

struct ParseData {
    file: string;
    file_index: int;
}

parse_file(void* data) {
    parse_data: ParseData* = data;


}
