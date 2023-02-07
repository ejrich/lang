#import "assembly.ol"
#import "ast.ol"
#import "lexer.ol"

library_directory: string;

asts: LinkedList<Ast*>;
directives: LinkedList<CompilerDirectiveAst*>;

parse(string entrypoint) {
    init_lexer();
    set_library_directory();

    add_module("runtime");
    queue_file_if_not_exists(entrypoint, name_with_extension(entrypoint));

    complete_work();
}

set_library_directory() {
    executable_path: CArray<u8>[PATH_MAX];
    #if os == OS.Linux {
        self_path := "/proc/self/exe"; #const
        bytes := readlink(self_path.data, &executable_path, PATH_MAX-1);
        dir_char := '/'; #const
    }
    #if os == OS.Windows {
        bytes := GetModuleFileNameA(null, &executable_path, PATH_MAX);
        dir_char := '\\'; #const
    }

    length := 0;
    each i in 1..bytes-1 {
        if executable_path[i] == dir_char {
            length = i;
        }
    }

    executable_str: string = { length = length; data = &executable_path; }
    relative_library_directory := format_string("%/%", allocate, executable_str, "../../ol/Modules");
    library_directory = get_full_path(relative_library_directory);
}

add_module(string module) {
    file := format_string("%/%.ol", allocate, library_directory, module);
    if file_exists(file)
        queue_file_if_not_exists(file, file);
}

add_module(string module, int fileIndex, Token token) {
    file := format_string("%/%.ol", allocate, library_directory, module);
    if file_exists(file)
        queue_file_if_not_exists(file, file);
    else
        report_error("Undefined module '%'", fileIndex, token, module);
}

bool add_module(CompilerDirectiveAst* module) {
    if file_exists(module.import.path)
        return queue_file_if_not_exists(module.import.path, module.import.path);

    report_error("Undefined module '%'", module, module.import.name);
    return false;
}

add_file(string file, string directory, int fileIndex, Token token) {
    file_path := format_string("%/%", allocate, directory, file);
    if file_exists(file_path)
        queue_file_if_not_exists(file_path, file);
    else
        report_error("File '%' does not exist", fileIndex, token, file);
}

bool add_file(CompilerDirectiveAst* import) {
    if file_exists(import.import.path)
        return queue_file_if_not_exists(import.import.path, import.import.name);

    report_error("File '%' does not exist", import, import.import.name);
    return false;
}

bool queue_file_if_not_exists(string file, string name) {
    each file_name in file_paths {
        if file_name == file return false;
    }

    file_index := file_paths.length;
    array_insert(&file_paths, file, allocate, reallocate);
    array_insert(&file_names, name, allocate, reallocate);
    array_insert(&private_scopes, null, allocate, reallocate);

    data := new<ParseData>();
    data.file = file;
    data.file_index = file_index;
    queue_work(parse_file, data);
    return true;
}

struct ParseData {
    file: string;
    file_index: int;
}

T* create_ast<T>(Ast* source, AstType type) {
    ast := new<T>();
    ast.ast_type = type;

    if source {
        ast.file_index = source.file_index;
        ast.line = source.line;
        ast.column = source.column;
    }

    return ast;
}

T* create_ast<T>(TokenEnumerator* enumerator, AstType type) {
    return create_ast<T>(enumerator.current, enumerator.file_index, type);
}

T* create_ast<T>(Token token, int file_index, AstType type) {
    ast := new<T>();
    ast.ast_type = type;
    ast.file_index = file_index;
    ast.line = token.line;
    ast.column = token.column;

    return ast;
}

T* create_type_ast<T>(TokenEnumerator* enumerator, AstType type, TypeKind type_kind) {
    ast := new<T>();
    ast.ast_type = type;
    ast.type_kind = type_kind;
    ast.file_index = enumerator.file_index;

    token := enumerator.current;
    ast.line = token.line;
    ast.column = token.column;

    if enumerator.private ast.flags = AstFlags.IsType | AstFlags.Private;
    else ast.flags = AstFlags.IsType;

    return ast;
}

#private

struct TokenEnumerator {
    tokens: Array<Token>;
    index: int;
    file_index: int;
    current: Token;
    last: Token;
    directory: string;
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
    if (enumerator.tokens.length > enumerator.index) {
        enumerator.current = enumerator.tokens[enumerator.index];
        return true;
    }
    return false;
}

bool peek(TokenEnumerator* enumerator, Token* token, int steps = 0) {
    if (enumerator.tokens.length > enumerator.index + steps) {
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

    tokens := load_file_tokens(parse_data.file, file_index);

    enumerator: TokenEnumerator = { tokens = tokens; file_index = file_index; last = tokens[tokens.length - 1]; directory = get_directory(parse_data.file); }

    while move_next(&enumerator) {
        attributes := parse_attributes(&enumerator);

        token := enumerator.current;
        switch token.type {
            case TokenType.Identifier;
                if peek(&enumerator, &token) {
                    if token.type == TokenType.Colon {
                        if attributes.length
                            report_error("Global variables cannot have attributes", file_index, token);

                        variable := parse_declaration(&enumerator, global = true);
                        if variable != null && add_global_variable(variable)
                            add(&asts, variable);
                    }
                    else {
                        function := parse_function(&enumerator, attributes);
                        if function != null && !string_is_empty(function.name){
                            add_function(function);
                            add(&asts, function);
                        }
                    }
                }
                else report_error("Unexpected token '%'", file_index, token, token.value);
            case TokenType.Struct; {
                struct_ast := parse_struct(&enumerator, attributes);
                if struct_ast {
                    if struct_ast.generics.length {
                        if add_polymorphic_struct(struct_ast) && struct_ast.name == "Array" {
                            base_array_type = struct_ast;
                        }
                    }
                    else {
                        if add_type(struct_ast) {
                            if struct_ast.name == "string" {
                                string_type = struct_ast;
                                struct_ast.type_kind = TypeKind.String;
                                struct_ast.flags |= AstFlags.Used;
                            }
                            else if struct_ast.name == "Any" {
                                any_type = struct_ast;
                                struct_ast.type_kind = TypeKind.Any;
                                struct_ast.flags |= AstFlags.Used;
                            }
                            else {
                                struct_ast.type_kind = TypeKind.Struct;
                                add(&asts, struct_ast);
                            }
                        }
                    }
                }
            }
            case TokenType.Enum; {
                enum_ast := parse_enum(&enumerator, attributes);
                if add_type(enum_ast) create_type_info(enum_ast);
            }
            case TokenType.Union; {
                if attributes.length
                    report_error("Unions cannot have attributes", file_index, token);

                union_ast := parse_union(&enumerator);
                if union_ast {
                    add_type(union_ast);
                    add(&asts, union_ast);
                }
            }
            case TokenType.Pound; {
                if attributes.length
                    report_error("Compiler directives cannot have attributes", file_index, token);

                directive := parse_top_level_directive(&enumerator, true);
                if directive {
                    switch directive.directive_type {
                        case DirectiveType.Library;
                            add_library(directive);
                        case DirectiveType.SystemLibrary;
                            add_system_library(directive);
                        case DirectiveType.ImportModule;
                        case DirectiveType.ImportFile; {}
                        default;
                            add(&directives, directive);
                    }
                }
            }
            case TokenType.Operator; {
                if attributes.length
                    report_error("Operator overloads cannot have attributes", file_index, token);

                overload := parse_operator_overload(&enumerator);
                add_overload(overload);
                add(&asts, overload);
            }
            case TokenType.Interface; {
                if attributes.length
                    report_error("Interfaces cannot have attributes", file_index, token);

                interface_ast := parse_interface(&enumerator);
                if interface_ast != null && !string_is_empty(interface_ast.name) {
                    add_type(interface_ast);
                    add(&asts, interface_ast);
                }
            }
            default;
                report_error("Unexpected token '%'", file_index, token, token.value);
        }
    }
}

Array<string> parse_attributes(TokenEnumerator* enumerator) {
    attributes: Array<string>;
    if (enumerator.current.type != TokenType.OpenBracket)
        return attributes;

    comma_required := false;
    while move_next(enumerator) {
        token := enumerator.current;

        switch token.type {
            case TokenType.CloseBracket;
                break;
            case TokenType.Identifier; {
                if (comma_required) {
                    report_error("Expected comma between attributes", enumerator.file_index, token);
                    move_past_next_token(enumerator, TokenType.CloseBracket);
                    return attributes;
                }

                array_insert(&attributes, token.value, allocate, reallocate);
                comma_required = true;
            }
            case TokenType.Comma; {
                if !comma_required {
                    report_error("Expected attribute after comma or at beginning of attribute list", enumerator.file_index, token);
                    move_past_next_token(enumerator, TokenType.CloseBracket);
                    return attributes;
                }

                comma_required = false;
            }
            default; {
                report_error("Unexpected token '%' in attribute list", enumerator.file_index, token, token.value);
                move_past_next_token(enumerator, TokenType.CloseBracket);
                return attributes;
            }
        }
    }

    if attributes.length == 0 report_error("Expected attribute(s) to be in attribute list", enumerator.file_index, enumerator.current);
    else if !comma_required report_error("Expected attribute after comma in attribute list", enumerator.file_index, enumerator.current);

    move_next(enumerator);

    return attributes;
}

move_past_next_token(TokenEnumerator* enumerator, TokenType type) {
    while move_next(enumerator) || enumerator.current.type == type {}

    move_next(enumerator);
}

Ast* parse_top_level_ast(TokenEnumerator* enumerator) {
    attributes := parse_attributes(enumerator);

    token := enumerator.current;
    switch token.type {
        case TokenType.Identifier; {
            if peek(enumerator, &token) {
                if token.type == TokenType.Colon {
                    if attributes.length
                        report_error("Global variables cannot have attributes", enumerator.file_index, token);

                    return parse_declaration(enumerator, global = true);
                }
                return parse_function(enumerator, attributes);
            }
            report_error("Unexpected token '%'", enumerator.file_index, token, token.value);
            return null;
        }
        case TokenType.Struct;
            return parse_struct(enumerator, attributes);
        case TokenType.Enum;
            return parse_enum(enumerator, attributes);
        case TokenType.Union; {
            if attributes.length
                report_error("Compiler directives cannot have attributes", enumerator.file_index, token);

            return parse_union(enumerator);
        }
        case TokenType.Pound; {
            if attributes.length
                report_error("Compiler directives cannot have attributes", enumerator.file_index, token);

            return parse_top_level_directive(enumerator);
        }
        case TokenType.Operator; {
            if attributes.length
                report_error("Operator overloads cannot have attributes", enumerator.file_index, token);

            return parse_operator_overload(enumerator);
        }
        case TokenType.Interface; {
            if attributes.length
                report_error("Interfaces cannot have attributes", enumerator.file_index, token);

            return parse_interface(enumerator);
        }
        default;
            report_error("Unexpected token '%'", enumerator.file_index, token, token.value);
    }

    return null;
}

bool try_add_generic(string generic, Array<string>* generics) {
    each existing_generic in *generics {
        if generic == existing_generic return false;
    }

    array_insert(generics, generic, allocate, reallocate);
    return true;
}

FunctionAst* parse_function(TokenEnumerator* enumerator, Array<string> attributes) {
    // 1. Determine return type and name of the function
    function := create_type_ast<FunctionAst>(enumerator, AstType.Function, TypeKind.Function);
    function.attributes = attributes;

    // 1a. Check if the return type is void
    token: Token;
    if !peek(enumerator, &token) {
        report_error("Expected function definition", enumerator.file_index, token);
        return null;
    }

    if token.type != TokenType.OpenParen {
        function.return_type_definition = parse_type(enumerator);
        move_next(enumerator);
    }

    // 1b. Handle multiple return values
    if enumerator.current.type == TokenType.Comma {
        return_type := create_ast<TypeDefinition>(function.return_type_definition, AstType.TypeDefinition);
        return_type.flags = AstFlags.Compound;
        array_insert(&return_type.generics, function.return_type_definition, allocate, reallocate);
        function.return_type_definition = return_type;

        while enumerator.current.type == TokenType.Comma {
            if !move_next(enumerator)
                break;

            array_insert(&return_type.generics, parse_type(enumerator), allocate, reallocate);
            move_next(enumerator);
        }
    }

    // 1b. Set the name of the function or get the name from the type
    if !enumerator.remaining {
        report_error("Expected the function name to be declared", enumerator.file_index, enumerator.last);
        return null;
    }

    switch enumerator.current.type {
        case TokenType.Identifier; {
            function.name = enumerator.current.value;
            move_next(enumerator);
        }
        case TokenType.OpenParen; {
            if function.return_type_definition.name == "*" || function.return_type_definition.count != null {
                report_error("Expected the function name to be declared", function.return_type_definition);
            }
            else {
                function.name = function.return_type_definition.name;
                each generic in function.return_type_definition.generics {
                    if generic.generics.length
                        report_error("Invalid generic in function '%'", generic, function.name);
                    else if !try_add_generic(generic.name, &function.generics)
                        report_error("Duplicate generic '%' in function '%'", generic.file_index, generic.line, generic.column, generic.name, function.name);
                }
                function.return_type_definition = null;
            }
        }
        default; {
            report_error("Expected the function name to be declared", enumerator.file_index, enumerator.current);
            move_next(enumerator);
        }
    }

    // 2. Parse generics
    if enumerator.current.type == TokenType.LessThan {
        comma_required_before_next_type := false;
        while move_next(enumerator) {
            token = enumerator.current;

            if token.type == TokenType.GreaterThan {
                if !comma_required_before_next_type
                    report_error("Expected comma in generics of function '%'", enumerator.file_index, token, function.name);

                break;
            }

            if !comma_required_before_next_type {
                if token.type == TokenType.Identifier {
                    if !try_add_generic(token.value, &function.generics) {
                        report_error("Duplicate generic '%' in function '%'", enumerator.file_index, token, token.value, function.name);
                    }
                }
                else
                    report_error("Unexpected token '%' in generics of function '%'", enumerator.file_index, token, token.value, function.name);

                comma_required_before_next_type = true;
            }
            else {
                if token.type != TokenType.Comma
                    report_error("Unexpected token '%' in function '%'", enumerator.file_index, token, token.value, function.name);

                comma_required_before_next_type = false;
            }
        }

        if function.generics.length == 0
            report_error("Expected function to contain generics", enumerator.file_index, enumerator.current);

        move_next(enumerator);
    }

    // 3. Search for generics in the function return type
    if (function.return_type_definition != null) {
        each generic, i in function.generics {
            if search_for_generic(generic, i, function.return_type_definition) {
                function.flags |= AstFlags.ReturnTypeHasGenerics;
            }
        }
    }

    // 4. Find open paren to start parsing arguments
    if (enumerator.current.type != TokenType.OpenParen)
    {
        // Add an error to the function AST and continue until open paren
        token = enumerator.current;
        report_error("Unexpected token '%' in function definition", enumerator.file_index, token, token.value);
        while (enumerator.remaining && enumerator.current.type != TokenType.OpenParen)
            move_next(enumerator);
    }

    // 5. Parse arguments until a close paren
    comma_required_before_next_argument := false;
    current_argument: DeclarationAst*;
    while move_next(enumerator) {
        token = enumerator.current;

        switch token.type {
            case TokenType.CloseParen; {
                if comma_required_before_next_argument {
                    array_insert(&function.arguments, current_argument, allocate, reallocate);
                    current_argument = null;
                }
                break;
            }
            case TokenType.Identifier;
            case TokenType.VarArgs;
                if comma_required_before_next_argument {
                    report_error("Comma required after declaring an argument", enumerator.file_index, token);
                }
                else if current_argument == null {
                    current_argument = create_ast<DeclarationAst>(token, enumerator.file_index, AstType.Declaration);
                    current_argument.type_definition = parse_type(enumerator, argument = true);
                    each generic, i in function.generics {
                        if search_for_generic(generic, i, current_argument.type_definition) {
                            current_argument.flags |= AstFlags.Generic;
                        }
                    }
                }
                else {
                    current_argument.name = token.value;
                    comma_required_before_next_argument = true;
                }
            case TokenType.Comma; {
                if comma_required_before_next_argument
                    array_insert(&function.arguments, current_argument, allocate, reallocate);
                else
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);

                current_argument = null;
                comma_required_before_next_argument = false;
            }
            case TokenType.Equals; {
                if comma_required_before_next_argument {
                    move_next(enumerator);
                    current_argument.value = parse_expression(enumerator, function, TokenType.Comma, TokenType.CloseParen);
                    if !enumerator.remaining {
                        report_error("Incomplete definition for function '%'", enumerator.file_index, enumerator.last, function.name);
                        return null;
                    }

                    switch enumerator.current.type {
                        case TokenType.Comma; {
                            comma_required_before_next_argument = false;
                            array_insert(&function.arguments, current_argument, allocate, reallocate);
                            current_argument = null;
                        }
                        case TokenType.CloseParen; {
                            array_insert(&function.arguments, current_argument, allocate, reallocate);
                            current_argument = null;
                        }
                        default;
                            report_error("Unexpected token '%' in arguments of function '%'", enumerator.file_index, enumerator.current, enumerator.current.value, function.name);
                    }
                }
                else
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);
            }
            default;
                report_error("Unexpected token '%' in arguments", enumerator.file_index, token, token.value);
        }

        if enumerator.current.type == TokenType.CloseParen
            break;
    }

    if current_argument
        report_error("Incomplete function argument in function '%'", enumerator.file_index, enumerator.current, function.name);

    if !comma_required_before_next_argument && function.arguments.length > 0
        report_error("Unexpected comma in arguments", enumerator.file_index, enumerator.current);

    if !move_next(enumerator) {
        report_error("Unexpected function body or compiler directive", enumerator.file_index, enumerator.last);
        return null;
    }

    // 6. Handle compiler directives
    while enumerator.current.type == TokenType.Pound {
        if !move_next(enumerator) || enumerator.current.type != TokenType.Identifier {
            report_error("Expected compiler directive value", enumerator.file_index, enumerator.current);
            return null;
        }

        directive := enumerator.current.value;
        unexpected_directive := "Unexpected compiler directive '%'"; #const
        switch directive.length {
            case 6; {
                if directive == "extern" {
                    function.flags |= AstFlags.Extern;
                    extern_error := "Extern function definition should be followed by the library in use"; #const
                    if !peek(enumerator, &token) {
                        report_error(extern_error, enumerator.file_index, token);
                    }
                    else if token.type == TokenType.Literal {
                        move_next(enumerator);
                        function.extern_lib = token.value;
                    }
                    else if token.type == TokenType.Identifier {
                        move_next(enumerator);
                        function.library_name = token.value;
                    }
                    else
                        report_error(extern_error, enumerator.file_index, token);

                    return function;
                }
                else if directive == "inline"
                    function.flags |= AstFlags.Inline;
                else
                    report_error(unexpected_directive, enumerator.file_index, enumerator.current, directive);
            }
            case 7; {
                if directive == "syscall" {
                    function.flags |= AstFlags.Syscall;
                    syscall_error := "Syscall function definition should be followed by the number for the system call"; #const
                    if !peek(enumerator, &token) {
                        report_error(syscall_error, enumerator.file_index, token);
                    }
                    else if token.type == TokenType.Number {
                        move_next(enumerator);
                        value: int;
                        if token.flags == TokenFlags.None && try_parse_integer(token.value, &value) {
                            function.syscall = value;
                        }
                        else report_error(syscall_error, enumerator.file_index, token);
                    }
                    else report_error(syscall_error, enumerator.file_index, token);

                    return function;
                }
                else
                    report_error(unexpected_directive, enumerator.file_index, enumerator.current, directive);
            }
            case 8; {
                if directive == "compiler" {
                    function.flags |= AstFlags.Compiler;
                    return function;
                }
                else if directive == "print_ir" {
                    function.flags |= AstFlags.PrintIR;
                }
                else
                    report_error(unexpected_directive, enumerator.file_index, enumerator.current, directive);
            }
            case 13; {
                if directive == "call_location"
                    function.flags |= AstFlags.PassCallLocation;
                else
                    report_error(unexpected_directive, enumerator.file_index, enumerator.current, directive);
            }
            default;
                report_error(unexpected_directive, enumerator.file_index, enumerator.current, directive);
        }
        move_next(enumerator);
    }

    // 7. Find open brace to start parsing body
    if (enumerator.current.type != TokenType.OpenBrace)
    {
        // Add an error and continue until open paren
        token = enumerator.current;
        report_error("Unexpected token '%' in function definition", enumerator.file_index, token, token.value);
        while move_next(enumerator) && enumerator.current.type != TokenType.OpenBrace {}

        if !enumerator.remaining
            return function;
    }

    // 8. Parse function body
    function.body = parse_scope(enumerator, function);

    return function;
}

StructAst* parse_struct(TokenEnumerator* enumerator, Array<string> attributes) {
    struct_ast := create_type_ast<StructAst>(enumerator, AstType.Struct, TypeKind.Struct);
    struct_ast.attributes = attributes;

    // 1. Determine name of struct
    if !move_next(enumerator) {
        report_error("Expected struct to have name", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.current.type == TokenType.Identifier
        struct_ast.name = enumerator.current.value;
    else
        report_error("Unexpected token '%' in struct definition", enumerator.file_index, enumerator.current, enumerator.current.value);

    // 2. Parse struct generics
    token: Token;
    if !peek(enumerator, &token) {
        report_error("Unexpected struct body", enumerator.file_index, token);
        return null;
    }

    if token.type == TokenType.LessThan {
        // Clear the '<' before entering loop
        move_next(enumerator);
        comma_required_before_next_type := false;
        while move_next(enumerator) {
            token = enumerator.current;

            if token.type == TokenType.GreaterThan {
                if !comma_required_before_next_type
                    report_error("Expected comma in generics for struct '%'", enumerator.file_index, token, struct_ast.name);

                break;
            }

            if !comma_required_before_next_type {
                if token.type == TokenType.Identifier {
                    if !try_add_generic(token.value, &struct_ast.generics) {
                        report_error("Duplicate generic '%' in struct '%'", enumerator.file_index, token, token.value, struct_ast.name);
                    }
                }
                else report_error("Unexpected token '%' in generics for struct '%'", enumerator.file_index, token, token.value, struct_ast.name);

                comma_required_before_next_type = true;
            }
            else {
                if token.type != TokenType.Comma
                    report_error("Unexpected token '%' in definition of struct '%'", enumerator.file_index, token, token.value, struct_ast.name);

                comma_required_before_next_type = false;
            }
        }

        if struct_ast.generics.length == 0
            report_error("Expected struct '' to contain generics", enumerator.file_index, enumerator.current, struct_ast.name);
    }

    // 3. Get any inherited structs
    move_next(enumerator);
    if enumerator.current.type == TokenType.Colon {
        move_next(enumerator);
        struct_ast.base_type_definition = parse_type(enumerator);
        move_next(enumerator);
    }

    // 4. Parse over the open brace
    if enumerator.current.type != TokenType.OpenBrace {
        report_error("Expected '{' token in struct definition", enumerator.file_index, enumerator.current);
        while move_next(enumerator) && enumerator.current.type != TokenType.OpenBrace {}
    }

    // 5. Iterate through fields
    while move_next(enumerator) {
        if enumerator.current.type == TokenType.CloseBrace
            break;

        field := parse_struct_field(enumerator);
        if field
            array_insert(&struct_ast.fields, field, allocate, reallocate);
    }

    // 6. Mark field types as generic if necessary
    if struct_ast.generics.length {
        each generic, i in struct_ast.generics {
            each field in struct_ast.fields {
                if field.type_definition != null && search_for_generic(generic, i, field.type_definition) {
                    field.flags |= AstFlags.Generic;
                }
            }
        }
    }

    return struct_ast;
}

StructFieldAst* parse_struct_field(TokenEnumerator* enumerator) {
    attributes := parse_attributes(enumerator);
    struct_field := create_ast<StructFieldAst>(enumerator, AstType.StructField);
    struct_field.attributes = attributes;

    if enumerator.current.type != TokenType.Identifier
        report_error("Expected name of struct field, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);

    struct_field.name = enumerator.current.value;

    // 1. Expect to get colon
    token: Token;
    move_next(enumerator);
    if enumerator.current.type != TokenType.Colon {
        token = enumerator.current;
        report_error("Unexpected token in struct field '%'", enumerator.file_index, token, token.value);
        // Parse to a ; or }
        while move_next(enumerator) {
            tokenType := enumerator.current.type;
            if tokenType == TokenType.SemiColon || tokenType == TokenType.CloseBrace
                break;
        }
        return struct_field;
    }

    // 2. Check if type is given
    if !peek(enumerator, &token) {
        report_error("Expected type of struct field", enumerator.file_index, token);
        return null;
    }

    if token.type == TokenType.Identifier {
        move_next(enumerator);
        struct_field.type_definition = parse_type(enumerator, null);
    }

    // 3. Get the value or return
    if !move_next(enumerator) {
        report_error("Expected declaration to have value", enumerator.file_index, enumerator.last);
        return null;
    }

    token = enumerator.current;
    switch token.type {
        case TokenType.Equals;
            parse_value(struct_field, enumerator, null);
        case TokenType.SemiColon;
            if (struct_field.type_definition == null)
                report_error("Expected struct field to have value", enumerator.file_index, token);
        default; {
            report_error("Unexpected token '%' in struct field", enumerator.file_index, token, token.value);
            // Parse until there is an equals sign or semicolon
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.SemiColon break;
                if enumerator.current.type == TokenType.Equals {
                    parse_value(struct_field, enumerator, null);
                    break;
                }
            }
        }
    }

    return struct_field;
}

EnumAst* parse_enum(TokenEnumerator* enumerator, Array<string> attributes) {
    enum_ast := create_type_ast<EnumAst>(enumerator, AstType.Enum, TypeKind.Enum);
    enum_ast.attributes = attributes;

    // 1. Determine name of enum
    if !move_next(enumerator) {
        report_error("Expected enum to have name", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.current.type == TokenType.Identifier
        enum_ast.name = enumerator.current.value;
    else
        report_error("Unexpected token '%' in enum definition", enumerator.file_index, enumerator.current, enumerator.current.value);

    // 2. Parse over the open brace
    move_next(enumerator);
    if enumerator.current.type == TokenType.Colon {
        move_next(enumerator);
        enum_ast.base_type_definition = parse_type(enumerator);
        move_next(enumerator);

        base_type := verify_type(enum_ast.base_type_definition);
        if base_type == null || base_type.type_kind != TypeKind.Integer {
            report_error("Base type of enum must be an integer, but got '%'", enum_ast.base_type_definition, print_type_definition(enum_ast.base_type_definition));
            enum_ast.base_type = &s32_type;
            enum_ast.size = 4;
            enum_ast.alignment = 4;
        }
        else {
            enum_ast.base_type = base_type;
            enum_ast.size = base_type.size;
            enum_ast.alignment = base_type.size;
        }
    }
    else {
        enum_ast.base_type = &s32_type;
        enum_ast.size = 4;
        enum_ast.alignment = 4;
    }

    if enumerator.current.type != TokenType.OpenBrace {
        report_error("Expected '{' token in enum definition", enumerator.file_index, enumerator.current);
        while move_next(enumerator) && enumerator.current.type != TokenType.OpenBrace {}
    }

    // 3. Iterate through fields
    lowest_allowed_value: s64;
    largest_allowed_value: u64;
    if enum_ast.base_type.flags & AstFlags.Signed {
        lowest_allowed_value = -(1 << (8 * enum_ast.size - 1));
        largest_allowed_value = 1 << (8 * enum_ast.size - 1) - 1;
    }
    else {
        largest_allowed_value = 1 << (8 * enum_ast.size) - 1;
    }
    largest_value := -1;
    enumIndex := 0;

    table_init(&enum_ast.values, 10);
    current_value: EnumValueAst*;
    parsing_value_default := false;
    while move_next(enumerator) {
        token := enumerator.current;

        switch token.type {
            case TokenType.CloseBrace;
                break;
            case TokenType.Identifier;
                if current_value == null {
                    current_value = create_ast<EnumValueAst>(token, enumerator.file_index, AstType.EnumValue);
                    current_value.index = enumIndex++;
                    current_value.name = token.value;
                }
                else if parsing_value_default {
                    parsing_value_default = false;
                    found, value := table_get(enum_ast.values, token.value);
                    if found {
                        if (value.flags & AstFlags.Verified) != AstFlags.Verified
                            report_error("Expected previously defined value '%' to have a defined value", enumerator.file_index, token, token.value);

                        current_value.value = value.value;
                        current_value.flags |= AstFlags.Verified;
                    }
                    else
                        report_error("Expected value '%' to be previously defined in enum '{enum_ast.Name}'", enumerator.file_index, token, token.value);
                }
                else
                    report_error("Unexpected token '%' in enum", enumerator.file_index, token, token.value);
            case TokenType.SemiColon;
                if current_value {
                    // Catch if the name hasn't been set
                    if string_is_null(current_value.name) || parsing_value_default {
                        report_error("Unexpected token '%' in enum", enumerator.file_index, token, token.value);
                    }
                    // Add the value to the enum and continue
                    else {
                        if !table_add(&enum_ast.values, current_value.name, current_value)
                            report_error("Enum '%' already contains value '%'", current_value, enum_ast.name, current_value.name);

                        if current_value.flags & AstFlags.Verified {
                            if current_value.value > largest_value
                                largest_value = current_value.value;
                        }
                        else
                            current_value.value = ++largest_value;

                        if current_value.value < lowest_allowed_value || current_value.value > largest_allowed_value {
                            report_error("Enum value '%.%' value '%' is out of range", current_value, enum_ast.name, current_value.name, current_value.value);
                        }
                    }

                    current_value = null;
                    parsing_value_default = false;
                }
            case TokenType.Equals;
                if current_value != null && !string_is_null(current_value.name)
                    parsing_value_default = true;
                else
                    report_error("Unexpected token '%' in enum", enumerator.file_index, token, token.value);
            case TokenType.Number;
                if current_value != null && parsing_value_default {
                    if token.flags == TokenFlags.None {
                        value: int;
                        if try_parse_integer(token.value, &value) {
                            current_value.value = value;
                            current_value.flags |= AstFlags.Verified;
                        }
                        else
                            report_error("Expected enum value to be an integer, but got '%'", enumerator.file_index, token, token.value);
                    }
                    else if token.flags & TokenFlags.Float {
                        report_error("Expected enum value to be an integer, but got '%'", enumerator.file_index, token, token.value);
                    }
                    else if token.flags & TokenFlags.HexNumber {
                        if token.value.length == 2
                            report_error("Invalid number '%'", enumerator.file_index, token, token.value);

                        sub_value := substring(token.value, 2, token.value.length - 2);
                        if sub_value.length <= 16 {
                            current_value.value = parse_hex_number(sub_value);
                            current_value.flags |= AstFlags.Verified;
                        }
                        else report_error("Expected enum value to be an integer, but got '%'", enumerator.file_index, token, token.value);
                    }
                    parsing_value_default = false;
                }
                else report_error("Unexpected token '%' in enum", enumerator.file_index, token, token.value);
            case TokenType.Character;
                if current_value != null && parsing_value_default {
                    current_value.value = token.value[0];
                    current_value.flags |= AstFlags.Verified;
                    parsing_value_default = false;
                }
                else report_error("Unexpected token '%' in enum", enumerator.file_index, token, token.value);
            default;
                report_error("Unexpected token '%' in enum", enumerator.file_index, token, token.value);
        }
    }

    if current_value {
        token := enumerator.current;
        report_error("Unexpected token '%' in enum", enumerator.file_index, token, token.value);
    }

    if enum_ast.values.length == 0
        report_error("Expected enum to have 1 or more values", enum_ast);

    return enum_ast;
}

UnionAst* parse_union(TokenEnumerator* enumerator) {
    union_ast := create_type_ast<UnionAst>(enumerator, AstType.Union, TypeKind.Union);

    // 1. Determine name of union
    if !move_next(enumerator) {
        report_error("Expected union to have name", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.current.type == TokenType.Identifier {
        union_ast.name = enumerator.current.value;
    }
    else
        report_error("Unexpected token '%' in union definition", enumerator.file_index, enumerator.current, enumerator.current.value);

    // 2. Parse over the open brace
    move_next(enumerator);
    if enumerator.current.type != TokenType.OpenBrace {
        report_error("Expected '{' token in union definition", enumerator.file_index, enumerator.current);
        while move_next(enumerator) && enumerator.current.type != TokenType.OpenBrace {}
    }

    // 3. Iterate through fields
    while move_next(enumerator) {
        if enumerator.current.type == TokenType.CloseBrace
            break;

        array_insert(&union_ast.fields, parse_union_field(enumerator), allocate, reallocate);
    }

    return union_ast;
}

UnionFieldAst* parse_union_field(TokenEnumerator* enumerator) {
    field := create_ast<UnionFieldAst>(enumerator, AstType.UnionField);

    if enumerator.current.type != TokenType.Identifier
        report_error("Expected name of union field, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);

    field.name = enumerator.current.value;

    // 1. Expect to get colon
    token: Token;
    move_next(enumerator);
    if enumerator.current.type != TokenType.Colon {
        token = enumerator.current;
        report_error("Unexpected token in union field '%'", enumerator.file_index, token, token.value);
        // Parse to a ; or }
        while move_next(enumerator) {
            token_type := enumerator.current.type;
            if token_type == TokenType.SemiColon || token_type == TokenType.CloseBrace
                break;
        }
        return field;
    }

    // 2. Check if type is given
    if !peek(enumerator, &token) {
        report_error("Unexpected type of union field", enumerator.file_index, token);
        return null;
    }

    if token.type == TokenType.Identifier {
        move_next(enumerator);
        field.type_definition = parse_type(enumerator, null);
    }

    // 3. Get the value or return
    if !move_next(enumerator) {
        report_error("Expected union to be closed by a '}'", enumerator.file_index, token);
        return null;
    }

    token = enumerator.current;
    switch token.type {
        case TokenType.SemiColon;
            if (field.type_definition == null)
                report_error("Expected union field to have a type", enumerator.file_index, token);
        default; {
            report_error("Unexpected token '%' in declaration", enumerator.file_index, token, token.value);
            // Parse until there is a semicolon or closing brace
            while move_next(enumerator) {
                token = enumerator.current;
                if token.type == TokenType.SemiColon || token.type == TokenType.CloseBrace
                    break;
            }
        }
    }

    return field;
}

bool search_for_generic(string generic, int index, TypeDefinition* type) {
    if (type.name == generic) {
        type.flags |= AstFlags.Generic;
        type.generic_index = index;
        return true;
    }

    has_generic := false;
    each type_generic in type.generics {
        if search_for_generic(generic, index, type_generic) {
            has_generic = true;
        }
    }
    return has_generic;
}

Ast* parse_line(TokenEnumerator* enumerator, ScopeAst* scope, IFunction* current_function, bool in_defer = false) {
    if !enumerator.remaining {
        report_error("End of file reached without closing scope", enumerator.file_index, enumerator.last);
        return null;
    }

    token := enumerator.current;
    switch token.type {
        case TokenType.Return; {
            if in_defer
                report_error("Cannot defer a return statement", enumerator.file_index, token);

            return parse_return(enumerator, current_function);
        }
        case TokenType.If;
            return parse_conditional(enumerator, current_function);
        case TokenType.While;
            return parse_while(enumerator, current_function);
        case TokenType.Each;
            return parse_each(enumerator, current_function);
        case TokenType.Identifier; {
            if (!peek(enumerator, &token))
            {
                report_error("End of file reached without closing scope", enumerator.file_index, enumerator.last);
                return null;
            }
            switch token.type {
                case TokenType.OpenParen;
                    return parse_call(enumerator, current_function, true);
                case TokenType.Colon;
                    return parse_declaration(enumerator, current_function);
                case TokenType.Equals; {
                    _: bool;
                    return parse_assignment(enumerator, current_function, &_);
                }
            }
            return parse_expression(enumerator, current_function);
        }
        case TokenType.Increment;
        case TokenType.Decrement;
        case TokenType.Asterisk;
            return parse_expression(enumerator, current_function);
        case TokenType.OpenBrace;
            return parse_scope(enumerator, current_function);
        case TokenType.Pound;
            return parse_compiler_directive(enumerator, current_function);
        case TokenType.Asm;
            return parse_inline_assembly(enumerator);
        case TokenType.Switch;
            return parse_switch(enumerator, current_function);
        case TokenType.Defer; {
            defer_ast := create_ast<DeferAst>(token, enumerator.file_index, AstType.Defer);
            if in_defer
                report_error("Unable to nest defer statements", enumerator.file_index, token);
            else if move_next(enumerator) {
                scope.defer_count++;
                defer_ast.statement = parse_scope_body(enumerator, current_function, in_defer = true);
            }
            else
                report_error("End of file reached without closing scope", enumerator.file_index, enumerator.last);

            return defer_ast;
        }
        case TokenType.Break; {
            breakAst := create_ast<Ast>(token, enumerator.file_index, AstType.Break);
            if move_next(enumerator) {
                if enumerator.current.type != TokenType.SemiColon {
                    report_error("Expected ';'", enumerator.file_index, enumerator.current);
                }
            }
            else report_error("End of file reached without closing scope", enumerator.file_index, enumerator.last);

            return breakAst;
        }
        case TokenType.Continue; {
            continueAst := create_ast<Ast>(token, enumerator.file_index, AstType.Continue);
            if move_next(enumerator) {
                if enumerator.current.type != TokenType.SemiColon {
                    report_error("Expected ';'", enumerator.file_index, enumerator.current);
                }
            }
            else report_error("End of file reached without closing scope", enumerator.file_index, enumerator.last);

            return continueAst;
        }
    }

    report_error("Unexpected token '%'", enumerator.file_index, token, token.value);
    return null;
}

ScopeAst* parse_scope_body(TokenEnumerator* enumerator, IFunction* current_function, bool top_level = false, bool in_defer = false) {
    if (enumerator.current.type == TokenType.OpenBrace)
        return parse_scope(enumerator, current_function, top_level);

    // Parse single AST
    scope_ast := create_ast<ScopeAst>(enumerator, AstType.Scope);
    array_resize(&scope_ast.children, 1, allocate, reallocate);
    if top_level {
        ast := parse_top_level_ast(enumerator);
        scope_ast.children[0] = ast;
    }
    else
        scope_ast.children[0] = parse_line(enumerator, scope_ast, current_function, in_defer);

    if scope_ast.defer_count
        array_resize(&scope_ast.deferred_asts, scope_ast.defer_count, allocate, reallocate);

    return scope_ast;
}

ScopeAst* parse_scope(TokenEnumerator* enumerator, IFunction* current_function, bool top_level = false) {
    scope_ast := create_ast<ScopeAst>(enumerator, AstType.Scope);

    closed := false;
    while move_next(enumerator) {
        if enumerator.current.type == TokenType.CloseBrace {
            closed = true;
            break;
        }

        if top_level array_insert(&scope_ast.children, parse_top_level_ast(enumerator), allocate, reallocate);
        else array_insert(&scope_ast.children, parse_line(enumerator, scope_ast, current_function), allocate, reallocate);
    }

    if !closed
        report_error("Scope not closed by '}'", enumerator.file_index, enumerator.current);

    if scope_ast.defer_count
        array_resize(&scope_ast.deferred_asts, scope_ast.defer_count, allocate, reallocate);

    return scope_ast;
}

ConditionalAst* parse_conditional(TokenEnumerator* enumerator, IFunction* current_function, bool top_level = false) {
    conditional_ast := create_ast<ConditionalAst>(enumerator, AstType.Conditional);

    // 1. Parse the conditional expression by first iterating over the initial 'if'
    move_next(enumerator);
    conditional_ast.condition = parse_condition_expression(enumerator, current_function);

    if !enumerator.remaining {
        report_error("Expected if to contain conditional expression and body", enumerator.file_index, enumerator.last);
        return null;
    }

    // 2. Determine how many lines to parse
    conditional_ast.if_block = parse_scope_body(enumerator, current_function, top_level);

    // 3. Parse else block if necessary
    token: Token;
    if !peek(enumerator, &token) {
        return conditional_ast;
    }

    if token.type == TokenType.Else {
        // First clear the else and then determine how to parse else block
        move_next(enumerator);
        if !move_next(enumerator) {
            report_error("Expected body of else branch", enumerator.file_index, enumerator.last);
            return null;
        }

        conditional_ast.else_block = parse_scope_body(enumerator, current_function, top_level);
    }

    return conditional_ast;
}

WhileAst* parse_while(TokenEnumerator* enumerator, IFunction* current_function) {
    while_ast := create_ast<WhileAst>(enumerator, AstType.While);

    // 1. Parse the conditional expression by first iterating over the initial 'while'
    move_next(enumerator);
    while_ast.condition = parse_condition_expression(enumerator, current_function);

    // 2. Determine how many lines to parse
    if !enumerator.remaining {
        report_error("Expected while loop to contain body", enumerator.file_index, enumerator.last);
        return null;
    }

    while_ast.body = parse_scope_body(enumerator, current_function);
    return while_ast;
}

EachAst* parse_each(TokenEnumerator* enumerator, IFunction* current_function) {
    each_ast := create_ast<EachAst>(enumerator, AstType.Each);

    // 1. Parse the iteration variable by first iterating over the initial 'each'
    move_next(enumerator);
    if enumerator.current.type == TokenType.Identifier {
        each_ast.iteration_variable = create_ast<VariableAst>(enumerator, AstType.Variable);
        each_ast.iteration_variable.name = enumerator.current.value;
    }
    else
        report_error("Expected variable in each block definition", enumerator.file_index, enumerator.current);

    move_next(enumerator);
    if enumerator.current.type == TokenType.Comma {
        move_next(enumerator);
        if enumerator.current.type == TokenType.Identifier {
            each_ast.index_variable = create_ast<VariableAst>(enumerator, AstType.Variable);
            each_ast.index_variable.name = enumerator.current.value;
            move_next(enumerator);
        }
        else
            report_error("Expected index variable after comma in each declaration", enumerator.file_index, enumerator.current);
    }

    // 2. Parse over the in keyword
    if enumerator.current.type != TokenType.In {
        report_error("Expected 'in' token in each block", enumerator.file_index, enumerator.current);
        while move_next(enumerator) && enumerator.current.type != TokenType.In {}
    }

    // 3. Determine the iterator
    move_next(enumerator);
    expression := parse_condition_expression(enumerator, current_function);

    // 3a. Check if the next token is a range
    if !enumerator.remaining {
        report_error("Expected each block to have iteration and body", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.current.type == TokenType.Range {
        if (each_ast.index_variable != null)
            report_error("Range enumerators cannot have iteration and index variable", enumerator.file_index, enumerator.current);

        each_ast.range_begin = expression;
        if !move_next(enumerator) {
            report_error("Expected range to have an end", enumerator.file_index, enumerator.last);
            return each_ast;
        }

        each_ast.range_end = parse_condition_expression(enumerator, current_function);
        if !enumerator.remaining {
            report_error("Expected each block to have iteration and body", enumerator.file_index, enumerator.last);
            return each_ast;
        }
    }
    else
        each_ast.iteration = expression;

    // 4. Determine how many lines to parse
    each_ast.body = parse_scope_body(enumerator, current_function);
    return each_ast;
}

Ast* parse_condition_expression(TokenEnumerator* enumerator, IFunction* current_function) {
    // Parse l-value
    first_token := enumerator.current;
    l_value := parse_next_expression_unit(enumerator, current_function);
    if l_value == null return null;

    // Handle any additional expressions before the operator
    while true {
        if !move_next(enumerator)
            return l_value;

        switch enumerator.current.type {
            case TokenType.OpenBrace;
                return l_value;
            case TokenType.Period;
                l_value = parse_struct_field_ref(enumerator, l_value, current_function);
            case TokenType.Increment;
                parse_change_by_one(enumerator, &l_value, enumerator.current, AstFlags.Positive);
            case TokenType.Decrement;
                parse_change_by_one(enumerator, &l_value, enumerator.current);
            default; break;
        }
    }

    expression: ExpressionAst*;

    // Get the initial r-value
    token := enumerator.current;
    if token.type == TokenType.Number && token.value[0] == '-' {
        // Get the constant for the initial r_value
        token.value = substring(token.value, 1, token.value.length - 1);

        expression = create_ast<ExpressionAst>(first_token, enumerator.file_index, AstType.Expression);
        expression.op = Operator.Subtract;
        expression.l_value = l_value;
        expression.r_value = parse_constant(token, enumerator.file_index);
        move_next(enumerator);
    }
    else {
        op := convert_operator(token);
        if op == Operator.None {
            return l_value;
        }

        expression = create_ast<ExpressionAst>(first_token, enumerator.file_index, AstType.Expression);
        expression.op = op;
        expression.l_value = l_value;

        move_next(enumerator);
        r_value := parse_next_expression_unit(enumerator, current_function);
        if r_value == null return null;

        while true {
            if !move_next(enumerator) {
                expression.r_value = r_value;
                expression.flags = AstFlags.Final;
                return expression;
            }

            switch enumerator.current.type {
                case TokenType.Period;
                    r_value = parse_struct_field_ref(enumerator, r_value, current_function);
                case TokenType.Increment;
                    parse_change_by_one(enumerator, &r_value, enumerator.current, AstFlags.Positive);
                case TokenType.Decrement;
                    parse_change_by_one(enumerator, &r_value, enumerator.current);
                default; break;
            }
        }

        expression.r_value = r_value;
    }

    // Get any additional values following the r-value
    token = enumerator.current;
    while enumerator.remaining {
        r_value: Ast*;
        op: Operator;
        if token.type == TokenType.Number && token.value[0] == '-' {
            op = Operator.Subtract;
            token.value = substring(token.value, 1, token.value.length - 1);
            r_value = parse_constant(token, enumerator.file_index);
            move_next(enumerator);
        }
        else {
            op = convert_operator(token);
            if op == Operator.None {
                break;
            }

            move_next(enumerator);
            token = enumerator.current;
            r_value = parse_next_expression_unit(enumerator, current_function);
            if r_value == null return null;
        }

        while true {
            if !move_next(enumerator) {
                expression = add_value_to_expression(op, r_value, expression, token, enumerator.file_index);
                expression.flags = AstFlags.Final;
                return expression;
            }

            switch enumerator.current.type {
                case TokenType.Period;
                    r_value = parse_struct_field_ref(enumerator, r_value, current_function);
                case TokenType.Increment;
                    parse_change_by_one(enumerator, &r_value, enumerator.current, AstFlags.Positive);
                case TokenType.Decrement;
                    parse_change_by_one(enumerator, &r_value, enumerator.current);
                default; break;
            }
        }

        expression = add_value_to_expression(op, r_value, expression, token, enumerator.file_index);
        token = enumerator.current;
    }

    expression.flags = AstFlags.Final;
    return expression;
}

DeclarationAst* parse_declaration(TokenEnumerator* enumerator, IFunction* current_function = null, bool global = false) {
    declaration := create_ast<DeclarationAst>(enumerator, AstType.Declaration);
    if global {
        if enumerator.private declaration.flags = AstFlags.Global | AstFlags.Private;
        else declaration.flags = AstFlags.Global;
    }

    if enumerator.current.type != TokenType.Identifier {
        report_error("Expected variable name to be an identifier, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
    }

    declaration.name = enumerator.current.value;

    // 1. Expect to get colon
    move_next(enumerator);
    if enumerator.current.type != TokenType.Colon {
        report_error("Unexpected token in declaration '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
        return null;
    }

    // 2. Check if type is given
    token: Token;
    if !peek(enumerator, &token) {
        report_error("Expected type or value of declaration", enumerator.file_index, token);
    }

    if token.type == TokenType.Identifier {
        move_next(enumerator);
        declaration.type_definition = parse_type(enumerator, current_function);
        if current_function {
            each generic, i in current_function.generics {
                if search_for_generic(generic, i, declaration.type_definition) {
                    declaration.flags |= AstFlags.Generic;
                }
            }
        }
    }

    // 3. Get the value or return
    if !move_next(enumerator) {
        report_error("Expected declaration to have value", enumerator.file_index, enumerator.last);
        return null;
    }

    token = enumerator.current;
    switch token.type {
        case TokenType.Equals;
            parse_value(declaration, enumerator, current_function);
        case TokenType.SemiColon;
            if (declaration.type_definition == null)
                report_error("Expected declaration to have value", enumerator.file_index, token);
        default; {
            report_error("Unexpected token '%' in declaration", enumerator.file_index, token, token.value);
            // Parse until there is an equals sign or semicolon
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.SemiColon break;
                if enumerator.current.type == TokenType.Equals {
                    parse_value(declaration, enumerator, current_function);
                    break;
                }
            }
        }
    }

    // 4. Parse compiler directives
    if peek(enumerator, &token) && token.type == TokenType.Pound {
        if peek(enumerator, &token, 1) && token.value == "const" {
            declaration.flags |= AstFlags.Constant;
            move_next(enumerator);
            move_next(enumerator);
        }
    }

    return declaration;
}

bool parse_value(Values* values, TokenEnumerator* enumerator, IFunction* current_function) {
    // 1. Step over '=' sign
    if !move_next(enumerator) {
        report_error("Expected declaration to have a value", enumerator.file_index, enumerator.last);
        return false;
    }

    // 2. Parse expression, constant, or object/array initialization as the value
    switch enumerator.current.type {
        case TokenType.OpenBrace; {
            values.assignments = table_create<string, AssignmentAst*>();
            while move_next(enumerator) {
                token := enumerator.current;
                if token.type == TokenType.CloseBrace
                    break;

                move_next: bool;
                assignment := parse_assignment(enumerator, current_function, &move_next);

                if !table_add(values.assignments, token.value, assignment)
                    report_error("Multiple assignments for field '%'", enumerator.file_index, token, token.value);

                if move_next {
                    peek(enumerator, &token);
                    if (token.type == TokenType.CloseBrace) {
                        move_next(enumerator);
                        break;
                    }
                }
                else if enumerator.current.type == TokenType.CloseBrace
                    break;
            }
            return true;
        }
        case TokenType.OpenBracket; {
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.CloseBracket
                    break;

                value := parse_expression(enumerator, current_function, TokenType.Comma, TokenType.CloseBracket);
                array_insert(&values.array_values, value);
                if enumerator.current.type == TokenType.CloseBracket
                    break;
            }
        }
        default;
            values.value = parse_expression(enumerator, current_function);
    }

    return false;
}

AssignmentAst* parse_assignment(TokenEnumerator* enumerator, IFunction* current_function, bool* move_next, Ast* reference = null) {
    // 1. Set the variable
    assignment: AssignmentAst*;
    if reference assignment = create_ast<AssignmentAst>(reference, AstType.Assignment);
    else assignment = create_ast<AssignmentAst>(enumerator, AstType.Assignment);

    assignment.reference = reference;
    *move_next = false;

    // 2. When the original reference is null, set the l-value to an identifier
    if reference == null {
        variable_ast := create_ast<IdentifierAst>(enumerator, AstType.Identifier);
        if enumerator.current.type != TokenType.Identifier {
            report_error("Expected variable name to be an identifier, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
        }
        variable_ast.name = enumerator.current.value;
        assignment.reference = variable_ast;

        // 2a. Expect to get equals sign
        if !move_next(enumerator) {
            report_error("Expected '=' in assignment'", enumerator.file_index, enumerator.last);
            return assignment;
        }

        // 2b. Assign the operator is there is one
        token := enumerator.current;
        if token.type != TokenType.Equals {
            op := convert_operator(token);
            if op != Operator.None {
                assignment.op = op;
                if !peek(enumerator, &token) {
                    report_error("Expected '=' in assignment'", enumerator.file_index, token);
                    return null;
                }

                if token.type == TokenType.Equals
                    move_next(enumerator);
                else
                    report_error("Expected '=' in assignment'", enumerator.file_index, token);
            }
            else
                report_error("Expected operator in assignment", enumerator.file_index, token);
        }
    }
    // 3, Get the operator on the reference expression if the expression ends with an operator
    else if reference.ast_type == AstType.Expression {
        expression := cast(ExpressionAst*, reference);
        assignment.op = expression.op;
        assignment.reference = expression.l_value;
    }

    // 4. Parse expression, field assignments, or array values
    switch enumerator.current.type {
        case TokenType.Equals;
            *move_next = parse_value(assignment, enumerator, current_function);
        case TokenType.SemiColon;
            report_error("Expected assignment to have value", enumerator.file_index, enumerator.current);
        default; {
            report_error("Unexpected token '%' in assignment", enumerator.file_index, enumerator.current, enumerator.current.value);
            // Parse until there is an equals sign or semicolon
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.SemiColon break;
                if enumerator.current.type == TokenType.Equals {
                    *move_next = parse_value(assignment, enumerator, current_function);
                    break;
                }
            }
        }
    }

    return assignment;
}

bool is_end_token(TokenType token, Array<TokenType> end_tokens) {
    if token == TokenType.SemiColon return true;

    each end_token in end_tokens {
        if end_token == token return true;
    }
    return false;
}

Ast* parse_expression(TokenEnumerator* enumerator, IFunction* current_function, Params<TokenType> end_tokens) {
    if !enumerator.remaining {
        report_error("Expected expression", enumerator.file_index, enumerator.last);
        return null;
    }

    // Parse l-value
    first_token := enumerator.current;
    l_value := parse_next_expression_unit(enumerator, current_function);
    if l_value == null return null;

    // Handle any additional expressions before the operator
    while true {
        if !move_next(enumerator) || is_end_token(enumerator.current.type, end_tokens) {
            return l_value;
        }

        switch enumerator.current.type {
            case TokenType.Equals; {
                _: bool;
                return parse_assignment(enumerator, current_function, &_, l_value);
            }
            case TokenType.Comma;
                return parse_compound_expression(enumerator, current_function, l_value);
            case TokenType.Period;
                l_value = parse_struct_field_ref(enumerator, l_value, current_function);
            case TokenType.Increment;
                parse_change_by_one(enumerator, &l_value, enumerator.current, AstFlags.Positive);
            case TokenType.Decrement;
                parse_change_by_one(enumerator, &l_value, enumerator.current);
            default; break;
        }
    }

    expression := create_ast<ExpressionAst>(first_token, enumerator.file_index, AstType.Expression);
    expression.l_value = l_value;

    // Get the initial r-value
    token := enumerator.current;
    if token.type == TokenType.Number && token.value[0] == '-' {
        expression.op = Operator.Subtract;

        // Get the constant for the initial r_value
        token.value = substring(token.value, 1, token.value.length - 1);
        expression.r_value = parse_constant(token, enumerator.file_index);
        move_next(enumerator);
    }
    else {
        op := convert_operator(token);
        if op == Operator.None {
            report_error("Unexpected token '%' when operator was expected", enumerator.file_index, token, token.value);
            return null;
        }

        move_next(enumerator);
        expression.op = op;
        if enumerator.current.type == TokenType.Equals {
            _: bool;
            return parse_assignment(enumerator, current_function, &_, expression);
        }

        r_value := parse_next_expression_unit(enumerator, current_function);
        if r_value == null return null;

        while true {
            if !move_next(enumerator) || is_end_token(enumerator.current.type, end_tokens) {
                expression.r_value = r_value;
                expression.flags = AstFlags.Final;
                return expression;
            }

            switch enumerator.current.type {
                case TokenType.Comma; {
                    expression.r_value = r_value;
                    return parse_compound_expression(enumerator, current_function, expression);
                }
                case TokenType.Period;
                    r_value = parse_struct_field_ref(enumerator, r_value, current_function);
                case TokenType.Increment;
                    parse_change_by_one(enumerator, &r_value, enumerator.current, AstFlags.Positive);
                case TokenType.Decrement;
                    parse_change_by_one(enumerator, &r_value, enumerator.current);
                default; break;
            }
        }

        expression.r_value = r_value;
    }

    // Get any additional values following the r-value
    token = enumerator.current;
    while !is_end_token(token.type, end_tokens) {
        r_value: Ast*;
        op: Operator;
        if token.type == TokenType.Number && token.value[0] == '-' {
            op = Operator.Subtract;
            token.value = substring(token.value, 1, token.value.length - 1);
            r_value = parse_constant(token, enumerator.file_index);
            move_next(enumerator);
        }
        else {
            op = convert_operator(token);
            if op == Operator.None {
                report_error("Unexpected token '%' when operator was expected", enumerator.file_index, token, token.value);
                return null;
            }

            move_next(enumerator);
            token = enumerator.current;
            r_value = parse_next_expression_unit(enumerator, current_function);
            if r_value == null return null;
        }

        while true {
            if !move_next(enumerator) || is_end_token(enumerator.current.type, end_tokens) {
                expression = add_value_to_expression(op, r_value, expression, token, enumerator.file_index);
                expression.flags = AstFlags.Final;
                return expression;
            }

            switch enumerator.current.type {
                case TokenType.Comma; {
                    expression = add_value_to_expression(op, r_value, expression, token, enumerator.file_index);
                    return parse_compound_expression(enumerator, current_function, expression);
                }
                case TokenType.Period;
                    r_value = parse_struct_field_ref(enumerator, r_value, current_function);
                case TokenType.Increment;
                    parse_change_by_one(enumerator, &r_value, enumerator.current, AstFlags.Positive);
                case TokenType.Decrement;
                    parse_change_by_one(enumerator, &r_value, enumerator.current);
                default; break;
            }
        }

        expression = add_value_to_expression(op, r_value, expression, token, enumerator.file_index);
        token = enumerator.current;
    }

    expression.flags = AstFlags.Final;
    return expression;
}

ExpressionAst* add_value_to_expression(Operator op, Ast* r_value, ExpressionAst* expression, Token token, int file_index) {
    precedence := get_operator_precedence(op);
    current := expression;
    depth := 0;

    while true {
        if precedence <= get_operator_precedence(current.op) {
            sub_expression := create_ast<ExpressionAst>(token, file_index, AstType.Expression);
            sub_expression.op = op;
            sub_expression.l_value = current;
            sub_expression.r_value = r_value;

            // Replace the top-level expression if necessary
            if current == expression expression = sub_expression;
            break;
        }
        else if r_value.ast_type != AstType.Expression || (r_value.flags & AstFlags.Final) == AstFlags.Final {
            sub_expression := create_ast<ExpressionAst>(token, file_index, AstType.Expression);
            sub_expression.op = op;
            sub_expression.l_value = current.r_value;
            sub_expression.r_value = r_value;
            current.r_value = sub_expression;
            break;
        }
        else current = cast(ExpressionAst*, r_value);
    }

    return expression;
}

int get_operator_precedence(Operator op) {
    switch op {
        // Boolean comparisons
        case Operator.And;
        case Operator.Or;
        case Operator.BitwiseOr;
        case Operator.BitwiseAnd;
        case Operator.Xor;
            return 0;
        // Value comparisons
        case Operator.Equality;
        case Operator.NotEqual;
        case Operator.GreaterThan;
        case Operator.LessThan;
        case Operator.GreaterThanEqual;
        case Operator.LessThanEqual;
            return 5;
        // First order operators
        case Operator.Add;
        case Operator.Subtract;
            return 10;
        // Second order operators
        case Operator.ShiftLeft;
        case Operator.ShiftRight;
        case Operator.RotateLeft;
        case Operator.RotateRight;
        case Operator.Multiply;
        case Operator.Divide;
        case Operator.Modulus;
            return 20;
    }

    return 0;
}

parse_change_by_one(TokenEnumerator* enumerator, Ast** l_value, Token token, AstFlags flags = AstFlags.None) {
    // Create subexpression to hold the operation
    // This case would be `var b = 4 + a++`, where we have a value before the operator
    change_by_one := create_ast<ChangeByOneAst>(*l_value, AstType.ChangeByOne);
    change_by_one.flags = flags;
    change_by_one.value = *l_value;
    *l_value = change_by_one;
}

Ast* parse_compound_expression(TokenEnumerator* enumerator, IFunction* current_function, Ast* initial) {
    compound_expression := create_ast<CompoundExpressionAst>(initial, AstType.CompoundExpression);
    array_insert(&compound_expression.children, initial, allocate, reallocate);
    first_token := enumerator.current;

    if !move_next(enumerator) {
        report_error("Expected compound expression to contain multiple values", enumerator.file_index, first_token);
        return compound_expression;
    }

    while enumerator.remaining {
        token := enumerator.current;

        switch token.type {
            case TokenType.SemiColon;
                break;
            case TokenType.Equals; {
                if compound_expression.children.length == 1
                    report_error("Expected compound expression to contain multiple values", enumerator.file_index, first_token);

                _: bool;
                return parse_assignment(enumerator, current_function, &_, compound_expression);
            }
            case TokenType.Colon; {
                compound_declaration := create_ast<CompoundDeclarationAst>(compound_expression, AstType.CompoundDeclaration);
                array_resize(&compound_declaration.variables, compound_expression.children.length, allocate, reallocate);

                // Copy the initial expression to variables
                each variable, i in compound_expression.children {
                    if variable.ast_type != AstType.Identifier {
                        report_error("Declaration should contain a variable", variable);
                    }
                    else {
                        identifier := cast(IdentifierAst*, variable);
                        variable_ast := create_ast<VariableAst>(identifier, AstType.Variable);
                        variable_ast.name = identifier.name;
                        compound_declaration.variables[i] = variable_ast;
                    }
                }

                if !move_next(enumerator) {
                    report_error("Expected declaration to contain type and/or value", enumerator.file_index, enumerator.last);
                    return null;
                }

                if enumerator.current.type == TokenType.Identifier {
                    compound_declaration.type_definition = parse_type(enumerator);
                    move_next(enumerator);
                }

                if !enumerator.remaining
                    return compound_declaration;

                switch enumerator.current.type {
                    case TokenType.Equals;
                        parse_value(compound_declaration, enumerator, current_function);
                    case TokenType.SemiColon;
                        if compound_declaration.type_definition == null
                            report_error("Expected token declaration to have type and/or value", enumerator.file_index, enumerator.current);
                    default; {
                        report_error("Unexpected token '%' in declaration", enumerator.file_index, enumerator.current, enumerator.current.value);
                        // Parse until there is an equals sign or semicolon
                        while move_next(enumerator) {
                            if enumerator.current.type == TokenType.SemiColon break;
                            if enumerator.current.type == TokenType.Equals {
                                parse_value(compound_declaration, enumerator, current_function);
                                break;
                            }
                        }
                    }
                }

                return compound_declaration;
            }
            default; {
                array_insert(&compound_expression.children, parse_expression(enumerator, current_function, TokenType.Comma, TokenType.Colon, TokenType.Equals), allocate, reallocate);
                if enumerator.current.type == TokenType.Comma
                    move_next(enumerator);
            }
        }
    }

    if compound_expression.children.length == 1
        report_error("Expected compound expression to contain multiple values", enumerator.file_index, first_token);

    return compound_expression;
}

Ast* parse_struct_field_ref(TokenEnumerator* enumerator, Ast* initial_ast, IFunction* current_function) {
    // 1. Initialize and move over the dot operator
    struct_field_ref := create_ast<StructFieldRefAst>(initial_ast, AstType.StructFieldRef);
    array_insert(&struct_field_ref.children, initial_ast, allocate, reallocate);

    // 2. Parse expression units until the operator is not '.'
    operator_required := false;
    while move_next(enumerator) {
        if operator_required {
            if enumerator.current.type != TokenType.Period {
                move(enumerator, -1);
                break;
            }
            operator_required = false;
        }
        else {
            ast := parse_next_expression_unit(enumerator, current_function);
            operator_required = true;
            if ast
                array_insert(&struct_field_ref.children, ast, allocate, reallocate);
        }
    }

    return struct_field_ref;
}

Ast* parse_next_expression_unit(TokenEnumerator* enumerator, IFunction* current_function) {
    token := enumerator.current;
    switch token.type {
        case TokenType.Number;
        case TokenType.Boolean;
        case TokenType.Literal;
        case TokenType.Character;
            // Parse constant
            return parse_constant(token, enumerator.file_index);
        case TokenType.Null;
            return create_ast<NullAst>(token, enumerator.file_index, AstType.Null);
        case TokenType.Identifier; {
            // Parse variable, call, or expression
            next_token: Token;
            if !peek(enumerator, &next_token) {
                report_error("Expected token to follow '%'", enumerator.file_index, token, token.value);
                return null;
            }
            switch next_token.type {
                case TokenType.OpenParen;
                    return parse_call(enumerator, current_function);
                case TokenType.OpenBracket;
                    return parse_index(enumerator, current_function);
                case TokenType.Asterisk;
                    if peek(enumerator, &next_token, 1) {
                        switch next_token.type {
                            case TokenType.Comma;
                            case TokenType.CloseParen; {
                                type_definition: TypeDefinition*;
                                if try_parse_type(enumerator, &type_definition) {
                                    each generic, i in current_function.generics {
                                        search_for_generic(generic, i, type_definition);
                                    }
                                    return type_definition;
                                }
                            }
                        }
                    }
                case TokenType.LessThan; {
                    type_definition: TypeDefinition*;
                    if try_parse_type(enumerator, &type_definition) {
                        if enumerator.current.type == TokenType.OpenParen {
                            call_ast := create_ast<CallAst>(type_definition, AstType.Call);
                            call_ast.name = type_definition.name;
                            call_ast.generics = type_definition.generics;

                            if current_function {
                                each generic in call_ast.generics {
                                    each function_generic, i in current_function.generics {
                                        search_for_generic(function_generic, i, generic);
                                    }
                                }
                            }

                            move_next(enumerator);
                            parse_arguments(call_ast, enumerator, current_function);
                            return call_ast;
                        }
                        else {
                            if current_function {
                                each generic, i in current_function.generics {
                                    search_for_generic(generic, i, type_definition);
                                }
                            }
                            return type_definition;
                        }
                    }
                }
            }
            identifier := create_ast<IdentifierAst>(token, enumerator.file_index, AstType.Identifier);
            identifier.name = token.value;
            return identifier;
        }
        case TokenType.Increment;
        case TokenType.Decrement; {
            if move_next(enumerator) {
                change_by_one := create_ast<ChangeByOneAst>(enumerator, AstType.ChangeByOne);

                if token.type == TokenType.Increment change_by_one.flags = AstFlags.Prefixed | AstFlags.Positive;
                else change_by_one.flags = AstFlags.Prefixed;

                change_by_one.value = parse_next_expression_unit(enumerator, current_function);
                if peek(enumerator, &token) && token.type == TokenType.Period {
                    move_next(enumerator);
                    change_by_one.value = parse_struct_field_ref(enumerator, change_by_one.value, current_function);
                }
                return change_by_one;
            }

            report_error("Expected token to follow '%'", enumerator.file_index, token, token.value);
            return null;
        }
        case TokenType.OpenParen; {
            // Parse subexpression
            if move_next(enumerator)
                return parse_expression(enumerator, current_function, TokenType.CloseParen);

            report_error("Expected token to follow '%'", enumerator.file_index, token, token.value);
            return null;
        }
        case TokenType.Not;
        case TokenType.Minus;
        case TokenType.Asterisk;
        case TokenType.Ampersand; {
            if move_next(enumerator) {
                unaryAst := create_ast<UnaryAst>(token, enumerator.file_index, AstType.Unary);
                unaryAst.op = cast(UnaryOperator, token.value[0]);
                unaryAst.value = parse_next_expression_unit(enumerator, current_function);
                if peek(enumerator, &token) && token.type == TokenType.Period {
                    move_next(enumerator);
                    unaryAst.value = parse_struct_field_ref(enumerator, unaryAst.value, current_function);
                }
                return unaryAst;
            }

            report_error("Expected token to follow '%'", enumerator.file_index, token, token.value);
            return null;
        }
        case TokenType.Cast;
            return parse_cast(enumerator, current_function);
        default;
            report_error("Unexpected token '%' in expression", enumerator.file_index, token, token.value);
   }
   return null;
}

CallAst* parse_call(TokenEnumerator* enumerator, IFunction* current_function, bool requiresSemicolon = false) {
    call_ast := create_ast<CallAst>(enumerator, AstType.Call);
    call_ast.name = enumerator.current.value;

    // This enumeration is the open paren
    move_next(enumerator);
    // Enumerate over the first argument
    parse_arguments(call_ast, enumerator, current_function);

    if !enumerator.remaining {
        report_error("Expected to close call", enumerator.file_index, enumerator.last);
    }
    else if requiresSemicolon {
        if enumerator.current.type == TokenType.SemiColon
            return call_ast;

        token: Token;
        if !peek(enumerator, &token) || token.type != TokenType.SemiColon {
            report_error("Expected ';'", enumerator.file_index, token);
        }
        else
            move_next(enumerator);
    }

    return call_ast;
}

parse_arguments(CallAst* call_ast, TokenEnumerator* enumerator, IFunction* current_function) {
    next_argument_required := false;
    while move_next(enumerator) {
        token := enumerator.current;

        if enumerator.current.type == TokenType.CloseParen {
            if next_argument_required
                report_error("Expected argument in call following a comma", enumerator.file_index, token);

            break;
        }

        if token.type == TokenType.Comma {
            report_error("Expected comma before next argument", enumerator.file_index, token);
        }
        else {
            next_token: Token;
            if token.type == TokenType.Identifier && peek(enumerator, &next_token) && next_token.type == TokenType.Equals {
                argument_name := token.value;

                move_next(enumerator);
                move_next(enumerator);

                table_init(&call_ast.specified_arguments, 5);
                argument := parse_expression(enumerator, current_function, TokenType.Comma, TokenType.CloseParen);
                if !table_add(&call_ast.specified_arguments, argument_name, argument)
                    report_error("Specified argument '%' is already in the call", enumerator.file_index, token, token.value);
            }
            else
                array_insert(&call_ast.arguments, parse_expression(enumerator, current_function, TokenType.Comma, TokenType.CloseParen), allocate, reallocate);

            switch enumerator.current.type {
                case TokenType.CloseParen; break;
                case TokenType.Comma; next_argument_required = true;
                case TokenType.SemiColon; {
                    report_error("Expected to close call with ')'", enumerator.file_index, enumerator.current);
                    break;
                }
            }
        }
    }
}

ReturnAst* parse_return(TokenEnumerator* enumerator, IFunction* current_function) {
    return_ast := create_ast<ReturnAst>(enumerator, AstType.Return);

    if move_next(enumerator) {
        if enumerator.current.type != TokenType.SemiColon {
            return_ast.value = parse_expression(enumerator, current_function);
        }
    }
    else
        report_error("Return does not have value", enumerator.file_index, enumerator.last);

    return return_ast;
}

IndexAst* parse_index(TokenEnumerator* enumerator, IFunction* current_function) {
    // 1. Initialize the index ast
    index := create_ast<IndexAst>(enumerator, AstType.Index);
    index.name = enumerator.current.value;
    move_next(enumerator);

    // 2. Parse index expression
    move_next(enumerator);
    index.index = parse_expression(enumerator, current_function, TokenType.CloseBracket);

    return index;
}

CompilerDirectiveAst* parse_top_level_directive(TokenEnumerator* enumerator, bool global = false) {
    directive := create_ast<CompilerDirectiveAst>(enumerator, AstType.CompilerDirective);

    if !move_next(enumerator) {
        report_error("Expected compiler directive to specify type and value", enumerator.file_index, enumerator.current);
        return null;
    }

    token := enumerator.current;
    if token.value == "run" {
        directive.directive_type = DirectiveType.Run;
        move_next(enumerator);
        directive.value = parse_scope_body(enumerator, null);
    }
    else if token.value == "if" {
        directive.directive_type = DirectiveType.If;
        directive.value = parse_conditional(enumerator, null, true);
    }
    else if token.value == "assert" {
        directive.directive_type = DirectiveType.Assert;
        move_next(enumerator);
        directive.value = parse_expression(enumerator, null);
    }
    else if token.value == "import" {
        if !move_next(enumerator) {
            report_error("Expected module name or source file", enumerator.file_index, enumerator.last);
            return null;
        }
        token = enumerator.current;
        switch token.type {
            case TokenType.Identifier; {
                directive.directive_type = DirectiveType.ImportModule;
                module := token.value;
                if global
                    add_module(module, enumerator.file_index, token);
                else
                    directive.import = { name = module; path = format_string("%/%.ol", allocate, library_directory, token.value); }
            }
            case TokenType.Literal; {
                directive.directive_type = DirectiveType.ImportFile;
                file := token.value;
                if global
                    add_file(file, enumerator.directory, enumerator.file_index, token);
                else
                    directive.import = { name = file; path = format_string("%/%", allocate, enumerator.directory, token.value); }
            }
            default;
                report_error("Expected module name or source file, but got '%'", enumerator.file_index, token, token.value);
        }
    }
    else if token.value == "library" {
        directive.directive_type = DirectiveType.Library;
        if (!move_next(enumerator) || enumerator.current.type != TokenType.Identifier)
        {
            report_error("Expected library name, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
            return null;
        }
        library_name := enumerator.current.value;
        if !move_next(enumerator) || enumerator.current.type != TokenType.Literal
        {
            report_error("Expected library path, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
            return null;
        }
        library_path := enumerator.current.value;
        directive.library = { name = library_name; path = library_path; }
        if path[0] == '/'
            directive.library.absolute_path = library_path;
        else
            directive.library.absolute_path = format_string("%/%", allocate, enumerator.directory, library_path);
    }
    else if token.value == "system_library" {
        directive.directive_type = DirectiveType.SystemLibrary;
        if !move_next(enumerator) || enumerator.current.type != TokenType.Identifier {
            report_error("Expected library name, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
            return null;
        }
        library_name := enumerator.current.value;
        if !move_next(enumerator) || enumerator.current.type != TokenType.Literal {
            report_error("Expected library file name, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
            return null;
        }
        file_name := enumerator.current.value;
        directive.library = { name = library_name; file_name = file_name; }
        if peek(enumerator, &token) && token.type == TokenType.Literal {
            directive.library.lib_path = token.value;
            move_next(enumerator);
        }
    }
    else if token.value == "private" {
        if enumerator.private
            report_error("Tried to set #private when already in private scope", enumerator.file_index, token);
        else {
            private_scope := new<GlobalScope>();
            init_global_scope(private_scope);
            private_scope.parent = &global_scope;
            private_scopes[enumerator.file_index] = private_scope;
        }
        enumerator.private = true;
        return null;
    }
    else {
        report_error("Unsupported top-level compiler directive '%'", enumerator.file_index, token, token.value);
        return null;
    }

    return directive;
}

Ast* parse_compiler_directive(TokenEnumerator* enumerator, IFunction* current_function) {
    directive := create_ast<CompilerDirectiveAst>(enumerator, AstType.CompilerDirective);

    if !move_next(enumerator) {
        report_error("Expected compiler directive to specify type and value", enumerator.file_index, enumerator.last);
        return null;
    }

    token := enumerator.current;
    if token.value == "if" {
        directive.directive_type = DirectiveType.If;
        directive.value = parse_conditional(enumerator, current_function);
    }
    else if token.value == "assert" {
        directive.directive_type = DirectiveType.Assert;
        move_next(enumerator);
        directive.value = parse_expression(enumerator, current_function);
    }
    else if token.value == "inline" {
        if !move_next(enumerator) || enumerator.current.type != TokenType.Identifier {
            report_error("Expected function call following #inline directive", enumerator.file_index, token);
            return null;
        }
        call := parse_call(enumerator, current_function, true);
        if call call.flags |= AstFlags.Inline;
        return call;
    }
    else {
        report_error("Unsupported function level compiler directive '%'", enumerator.file_index, token, token.value);
        return null;
    }

    return directive;
}

AssemblyAst* parse_inline_assembly(TokenEnumerator* enumerator) {
    assembly := create_ast<AssemblyAst>(enumerator, AstType.Assembly);

    // First move over the opening '{'
    if !move_next(enumerator) {
        report_error("Expected an opening '{' at asm block", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.current.type != TokenType.OpenBrace {
        report_error("Expected an opening '{' at asm block", enumerator.file_index, enumerator.current);
        return null;
    }

    closed := false;
    parsing_in_registers := true;
    parsing_out_registers := false;
    while move_next(enumerator) {
        token := enumerator.current;

        switch token.type {
            case TokenType.CloseBrace; {
                closed = true;
                break;
            }
            case TokenType.In; {
                if parse_in_register(assembly, enumerator) {
                    if parsing_out_registers || !parsing_in_registers {
                        report_error("In instructions should be declared before body instructions", enumerator.file_index, token);
                    }
                }
                else {
                    // Skip through the next ';' or '}'
                    while enumerator.current.type != TokenType.SemiColon && enumerator.current.type != TokenType.CloseBrace && move_next(enumerator) {}

                    if enumerator.current.type == TokenType.CloseBrace
                        return assembly;
                }
            }
            case TokenType.Out; {
                if parse_out_value(assembly, enumerator) {
                    if parsing_in_registers {
                        parsing_in_registers = false;
                        parsing_out_registers = true;
                        report_error("Expected instructions before out registers", enumerator.file_index, token);
                    }
                    else if !parsing_out_registers
                        parsing_out_registers = true;
                }
                else {
                    // Skip through the next ';' or '}'
                    while enumerator.current.type != TokenType.SemiColon && enumerator.current.type != TokenType.CloseBrace && move_next(enumerator) {}

                    if enumerator.current.type == TokenType.CloseBrace
                        return assembly;
                }
            }
            case TokenType.Identifier; {
                instruction := parse_assembly_instruction(enumerator);
                if instruction {
                    if parsing_in_registers parsing_in_registers = false;
                    else if parsing_out_registers
                        report_error("Expected body instructions before out values", instruction);

                    array_insert(&assembly.instructions, instruction, allocate, reallocate);
                }
                else {
                    // Skip through the next ';' or '}'
                    while enumerator.current.type != TokenType.SemiColon && enumerator.current.type != TokenType.CloseBrace && move_next(enumerator) {}

                    if enumerator.current.type == TokenType.CloseBrace
                        return assembly;
                }
            }
            default; {
                report_error("Expected instruction in assembly block, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);

                // Skip through the next ';' or '}'
                while (enumerator.current.type != TokenType.SemiColon || enumerator.current.type != TokenType.CloseBrace) && move_next(enumerator) {}

                if enumerator.current.type == TokenType.CloseBrace
                    return assembly;
            }
        }
    }

    if !closed
        report_error("Assembly block not closed by '}'", enumerator.file_index, enumerator.current);

    return assembly;
}

bool parse_in_register(AssemblyAst* assembly, TokenEnumerator* enumerator) {
    if !move_next(enumerator) {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    if enumerator.current.type != TokenType.Identifier {
        report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
        return false;
    }

    register := enumerator.current.value;
    input := create_ast<AssemblyInputAst>(enumerator, AstType.AssemblyInput);
    input.register = register;

    if !table_add(&assembly.in_registers, register, input) {
        report_error("Duplicate in register '%'", enumerator.file_index, enumerator.current, register);
        return false;
    }

    if !move_next(enumerator) {
        report_error("Expected comma or semicolon in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    if enumerator.current.type != TokenType.Comma {
        report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
        return false;
    }

    if !move_next(enumerator) {
        report_error("Expected value in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    input.ast = parse_next_expression_unit(enumerator, null);

    if !move_next(enumerator) {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    if enumerator.current.type != TokenType.SemiColon {
        report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
        return false;
    }

    return true;
}

bool parse_out_value(AssemblyAst* assembly, TokenEnumerator* enumerator) {
    if !move_next(enumerator) {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    output := create_ast<AssemblyInputAst>(enumerator, AstType.AssemblyInput);

    output.ast = parse_next_expression_unit(enumerator, null);
    if output.ast == null {
        return false;
    }

    if !move_next(enumerator) {
        report_error("Expected comma or semicolon in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    if enumerator.current.type != TokenType.Comma {
        report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
        return false;
    }

    if !move_next(enumerator) {
        report_error("Expected value in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    if enumerator.current.type != TokenType.Identifier {
        report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
        return false;
    }
    output.register = enumerator.current.value;

    if !move_next(enumerator) {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    if enumerator.current.type != TokenType.SemiColon {
        report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
        return false;
    }

    array_insert(&assembly.out_values, output, allocate, reallocate);
    return true;
}

AssemblyInstructionAst* parse_assembly_instruction(TokenEnumerator* enumerator) {
    instruction := create_ast<AssemblyInstructionAst>(enumerator, AstType.AssemblyInstruction);
    instruction.instruction = enumerator.current.value;

    if !move_next(enumerator) {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.last);
        return null;
    }

    switch enumerator.current.type {
        case TokenType.Identifier; {
            instruction.value1 = create_ast<AssemblyValueAst>(enumerator, AstType.AssemblyValue);
            instruction.value1.register = enumerator.current.value;
        }
        case TokenType.Number; {
            instruction.value1 = create_ast<AssemblyValueAst>(enumerator, AstType.AssemblyValue);
            instruction.value1.constant = parse_constant(enumerator.current, enumerator.file_index);
        }
        case TokenType.OpenBracket; {
            instruction.value1 = create_ast<AssemblyValueAst>(enumerator, AstType.AssemblyValue);
            if !parse_assembly_pointer(instruction.value1, enumerator)
                return null;
        }
        case TokenType.SemiColon; return instruction;
        default; {
            report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
            return null;
        }
    }

    if !move_next(enumerator) {
        report_error("Expected comma or semicolon in instruction", enumerator.file_index, enumerator.last);
        return null;
    }

    switch enumerator.current.type {
        case TokenType.Comma; {}
        case TokenType.SemiColon; return instruction;
        default; {
            report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
            return null;
        }
    }

    if !move_next(enumerator) {
        report_error("Expected value in instruction", enumerator.file_index, enumerator.last);
        return null;
    }

    instruction.value2 = create_ast<AssemblyValueAst>(enumerator, AstType.AssemblyValue);
    switch enumerator.current.type {
        case TokenType.Identifier;
            instruction.value2.register = enumerator.current.value;
        case TokenType.Number;
            instruction.value2.constant = parse_constant(enumerator.current, enumerator.file_index);
        case TokenType.OpenBracket;
            if !parse_assembly_pointer(instruction.value2, enumerator)
                return null;
        default; {
            report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
            return null;
        }
    }

    if !move_next(enumerator) {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.current.type != TokenType.SemiColon {
        report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
        return null;
    }

    return instruction;
}

bool parse_assembly_pointer(AssemblyValueAst* value, TokenEnumerator* enumerator) {
    if !move_next(enumerator) || enumerator.current.type != TokenType.Identifier {
        report_error("Expected register after pointer in instruction", enumerator.file_index, enumerator.current);
        return false;
    }
    value.flags |= AstFlags.Dereference;
    value.register = enumerator.current.value;

    if !move_next(enumerator) || enumerator.current.type != TokenType.CloseBracket {
        report_error("Expected to close pointer to register with ']'", enumerator.file_index, enumerator.current);
        return false;
    }

    return true;
}

SwitchAst* parse_switch(TokenEnumerator* enumerator, IFunction* current_function) {
    switch_ast := create_ast<SwitchAst>(enumerator, AstType.Switch);
    if !move_next(enumerator) {
        report_error("Expected value for switch statement", enumerator.file_index, enumerator.last);
        return null;
    }

    switch_ast.value = parse_expression(enumerator, current_function, TokenType.OpenBrace);

    if enumerator.current.type != TokenType.OpenBrace {
        report_error("Expected switch statement value to be followed by '{' and cases", enumerator.file_index, enumerator.current);
        return null;
    }

    closed := false;
    current_cases: SwitchCases;
    while move_next(enumerator) {
        switch enumerator.current.type {
            case TokenType.CloseBrace; {
                closed = true;
                if current_cases.first_case {
                    report_error("Switch statement contains case(s) without bodies starting", current_cases.first_case);
                    return null;
                }
                break;
            }
            case TokenType.Case; {
                if !move_next(enumerator) {
                    report_error("Expected value after case", enumerator.file_index, enumerator.last);
                    return null;
                }

                switch_case := parse_expression(enumerator, current_function);

                if current_cases.first_case == null
                    current_cases.first_case = switch_case;
                else {
                    array_insert(&current_cases.additional_cases, switch_case, allocate, reallocate);
                }
            }
            case TokenType.Default; {
                if !move_next(enumerator) || enumerator.current.type != TokenType.SemiColon {
                    report_error("Expected ';' after default", enumerator.file_index, enumerator.current);
                    return null;
                }
                if !move_next(enumerator) {
                    report_error("Expected body after default", enumerator.file_index, enumerator.last);
                    return null;
                }

                switch_ast.default_case = parse_scope_body(enumerator, current_function);
            }
            default; {
                if current_cases.first_case {
                    // Parse through the case body and add to the list
                    current_cases.body = parse_scope_body(enumerator, current_function);

                    array_insert(&switch_ast.cases, current_cases, allocate, reallocate);
                    current_cases = { first_case = null; body = null; }
                    current_cases.additional_cases.length = 0;
                    current_cases.additional_cases.data = null;
                }
                else {
                    report_error("Switch statement contains case body prior to any cases", enumerator.file_index, enumerator.current);
                    // Skip through the next ';' or '}'
                    while enumerator.current.type != TokenType.SemiColon && enumerator.current.type != TokenType.CloseBrace && move_next(enumerator) {}
                }
            }
        }
    }

    if !closed {
        report_error("Expected switch statement to be closed by '}'", enumerator.file_index, enumerator.current);
        return null;
    }
    else if switch_ast.cases.length == 0 {
        report_error("Expected switch to have 1 or more non-default cases", switch_ast);
        return null;
    }

    return switch_ast;
}

OperatorOverloadAst* parse_operator_overload(TokenEnumerator* enumerator) {
    overload := create_ast<OperatorOverloadAst>(enumerator, AstType.OperatorOverload);
    if !move_next(enumerator) {
        report_error("Expected an operator be specified to overload", enumerator.file_index, enumerator.last);
        return null;
    }

    // 1. Determine the operator
    token: Token;
    if enumerator.current.type == TokenType.OpenBracket && peek(enumerator, &token) && token.type == TokenType.CloseBracket {
        overload.op = Operator.Subscript;
        move_next(enumerator);
    }
    else {
        overload.op = convert_operator(enumerator.current);
        if overload.op == Operator.None
            report_error("Expected an operator to be be specified, but got '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
    }

    if !move_next(enumerator) {
        report_error("Expected to get the type to overload the operator", enumerator.file_index, enumerator.last);
        return null;
    }

    // 2. Determine generics if necessary
    if enumerator.current.type == TokenType.LessThan {
        comma_required_before_next_type := false;
        while move_next(enumerator) {
            token = enumerator.current;
            if token.type == TokenType.GreaterThan {
                if !comma_required_before_next_type
                    report_error("Expected comma in generics", enumerator.file_index, token);

                break;
            }

            if !comma_required_before_next_type {
                if token.type == TokenType.Identifier {
                    if !try_add_generic(token.value, &overload.generics) {
                        report_error("Duplicate generic '%'", enumerator.file_index, token, token.value);
                    }
                }
                else
                    report_error("Unexpected token '%' in generics", enumerator.file_index, token, token.value);

                comma_required_before_next_type = true;
            }
            else {
                if token.type != TokenType.Comma {
                    report_error("Unexpected token '%' when defining generics", enumerator.file_index, token, token.value);
                }

                comma_required_before_next_type = false;
            }
        }

        if overload.generics.length == 0
            report_error("Expected operator overload to contain generics", enumerator.file_index, enumerator.current);

        move_next(enumerator);
    }

    // 3. Find open paren to start parsing arguments
    if enumerator.current.type != TokenType.OpenParen {
        // Add an error to the function AST and continue until open paren
        token = enumerator.current;
        report_error("Unexpected token '%' in operator overload definition", enumerator.file_index, token, token.value);
        while move_next(enumerator) && enumerator.current.type != TokenType.OpenParen {}
    }

    // 4. Get the arguments for the operator overload
    comma_required_before_next_argument := false;
    current_argument: DeclarationAst*;
    while move_next(enumerator) {
        token = enumerator.current;


        switch token.type {
            case TokenType.CloseParen; {
                if comma_required_before_next_argument {
                    array_insert(&overload.arguments, current_argument, allocate, reallocate);
                    current_argument = null;
                }
                break;
            }
            case TokenType.Identifier; {
                if comma_required_before_next_argument {
                    report_error("Comma required after declaring an argument", enumerator.file_index, token);
                }
                else if current_argument == null {
                    current_argument = create_ast<DeclarationAst>(token, enumerator.file_index, AstType.Declaration);
                    current_argument.type_definition = parse_type(enumerator, argument = true);
                    each generic, i in overload.generics {
                        if search_for_generic(generic, i, current_argument.type_definition) {
                            current_argument.flags |= AstFlags.Generic;
                        }
                    }
                    if overload.arguments.length == 0
                        overload.type = current_argument.type_definition;
                }
                else {
                    current_argument.name = token.value;
                    comma_required_before_next_argument = true;
                }
            }
            case TokenType.Comma; {
                if comma_required_before_next_argument
                    array_insert(&overload.arguments, current_argument, allocate, reallocate);
                else
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);

                current_argument = null;
                comma_required_before_next_argument = false;
            }
            default;
                report_error("Unexpected token '%' in arguments", enumerator.file_index, token, token.value);
        }

        if enumerator.current.type == TokenType.CloseParen
            break;
    }

    if current_argument
        report_error("Incomplete argument in overload for type '%'", enumerator.file_index, enumerator.current, overload.type.name);

    if !comma_required_before_next_argument && overload.arguments.length > 0
        report_error("Unexpected comma in arguments", enumerator.file_index, enumerator.current);

    // 5. Set the return type based on the operator
    move_next(enumerator);
    switch overload.op {
        case Operator.And;
        case Operator.Or;
        case Operator.Equality;
        case Operator.NotEqual;
        case Operator.GreaterThanEqual;
        case Operator.LessThanEqual;
        case Operator.GreaterThan;
        case Operator.LessThan;
        case Operator.Xor; {
            overload.return_type = &bool_type;
        }
        case Operator.Subscript;
            if enumerator.current.type != TokenType.Colon {
                report_error("Unexpected to define return type for subscript", enumerator.file_index, enumerator.current);
            }
            else if move_next(enumerator) {
                overload.return_type_definition = parse_type(enumerator);
                each generic, i in overload.generics {
                    if search_for_generic(generic, i, overload.return_type_definition) {
                        overload.flags |= AstFlags.ReturnTypeHasGenerics;
                    }
                }
                move_next(enumerator);
            }
        default; {
            if overload.generics.length {
                overload.flags |= AstFlags.ReturnTypeHasGenerics;
            }
            each generic, i in overload.generics {
                search_for_generic(generic, i, overload.type);
            }
        }
    }

    // 6. Handle compiler directives
    if enumerator.current.type == TokenType.Pound {
        if !move_next(enumerator) {
            report_error("Expected compiler directive value", enumerator.file_index, enumerator.last);
            return null;
        }
        if enumerator.current.value == "print_ir"
            overload.flags |= AstFlags.PrintIR;
        else
            report_error("Unexpected compiler directive '%'", enumerator.file_index, enumerator.current, enumerator.current.value);

        move_next(enumerator);
    }

    // 7. Find open brace to start parsing body
    if enumerator.current.type != TokenType.OpenBrace {
        // Add an error and continue until open paren
        token = enumerator.current;
        report_error("Unexpected token '%' in operator overload definition", enumerator.file_index, token, token.value);
        while move_next(enumerator) && enumerator.current.type != TokenType.OpenBrace {}
    }

    // 8. Parse body
    overload.body = parse_scope(enumerator, overload);

    return overload;
}

InterfaceAst* parse_interface(TokenEnumerator* enumerator) {
    interface_ast := create_type_ast<InterfaceAst>(enumerator, AstType.Interface, TypeKind.Interface);
    move_next(enumerator);

    // 1a. Check if the return type is void
    token: Token;
    if !peek(enumerator, &token) {
        report_error("Expected interface definition", enumerator.file_index, token);
        return null;
    }
    if token.type != TokenType.OpenParen {
        interface_ast.return_type_definition = parse_type(enumerator);
        move_next(enumerator);
    }

    // 1b. Handle multiple return values
    if enumerator.current.type == TokenType.Comma {
        return_type := create_ast<TypeDefinition>(interface_ast.return_type_definition, AstType.TypeDefinition);
        return_type.flags = AstFlags.Compound;
        array_insert(&return_type.generics, interface_ast.return_type_definition, allocate, reallocate);
        interface_ast.return_type_definition = return_type;

        while enumerator.current.type == TokenType.Comma {
            if !move_next(enumerator)
                break;

            array_insert(&return_type.generics, parse_type(enumerator), allocate, reallocate);
            move_next(enumerator);
        }
    }

    // 1b. Set the name of the interface or get the name from the type
    if !enumerator.remaining {
        report_error("Expected the interface name to be declared", enumerator.file_index, enumerator.last);
        return null;
    }
    switch enumerator.current.type {
        case TokenType.Identifier; {
            interface_ast.name = enumerator.current.value;
            move_next(enumerator);
        }
        case TokenType.OpenParen;
            if interface_ast.return_type_definition.name == "*" || interface_ast.return_type_definition.count != null {
                report_error("Expected the interface name to be declared", interface_ast.return_type_definition);
            }
            else {
                interface_ast.name = interface_ast.return_type_definition.name;
                if interface_ast.return_type_definition.generics.length
                    report_error("Interface '%' cannot have generics", interface_ast.return_type_definition, interface_ast.name);

                interface_ast.return_type_definition = null;
            }
        default; {
            report_error("Expected the interface name to be declared", enumerator.file_index, enumerator.current);
            move_next(enumerator);
        }
    }

    // 2. Parse arguments until a close paren
    comma_required_before_next_argument := false;
    current_argument: DeclarationAst*;
    while move_next(enumerator) {
        token = enumerator.current;

        switch token.type {
            case TokenType.CloseParen; {
                if comma_required_before_next_argument {
                    array_insert(&interface_ast.arguments, current_argument, allocate, reallocate);
                    current_argument = null;
                }
                break;
            }
            case TokenType.Identifier;
                if comma_required_before_next_argument {
                    report_error("Comma required after declaring an argument", enumerator.file_index, token);
                }
                else if current_argument == null {
                    current_argument = create_ast<DeclarationAst>(token, enumerator.file_index, AstType.Declaration);
                    current_argument.type_definition = parse_type(enumerator);
                }
                else {
                    current_argument.name = token.value;
                    comma_required_before_next_argument = true;
                }
            case TokenType.Comma; {
                if comma_required_before_next_argument
                    array_insert(&interface_ast.arguments, current_argument, allocate, reallocate);
                else
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);

                current_argument = null;
                comma_required_before_next_argument = false;
            }
            case TokenType.Equals;
                if comma_required_before_next_argument {
                    report_error("Interface '%' cannot have default argument values", enumerator.file_index, token, interface_ast.name);
                    while move_next(enumerator) {
                        if enumerator.current.type == TokenType.Comma {
                            comma_required_before_next_argument = false;
                            array_insert(&interface_ast.arguments, current_argument, allocate, reallocate);
                            current_argument = null;
                            break;
                        }
                        else if enumerator.current.type == TokenType.Comma {
                            array_insert(&interface_ast.arguments, current_argument, allocate, reallocate);
                            current_argument = null;
                            break;
                        }
                    }
                }
                else
                    report_error("Unexpected token '=' in arguments", enumerator.file_index, token);
            default;
                report_error("Unexpected token '%' in arguments", enumerator.file_index, token, token.value);
        }

        if enumerator.current.type == TokenType.CloseParen
            break;
    }

    if current_argument
        report_error("Incomplete argument in interface '%'", enumerator.file_index, enumerator.current, interface_ast.name);

    if !comma_required_before_next_argument && interface_ast.arguments.length > 0
        report_error("Unexpected comma in arguments", enumerator.file_index, enumerator.current);

    return interface_ast;
}

TypeDefinition* parse_type(TokenEnumerator* enumerator, IFunction* current_function = null, bool argument = false, int depth = 0) {
    type_definition := create_ast<TypeDefinition>(enumerator, AstType.TypeDefinition);
    type_definition.name = enumerator.current.value;

    // Alias int to s32
    if type_definition.name == "int"
        type_definition.name = "s32";

    if enumerator.current.type == TokenType.VarArgs {
        if !argument
            report_error("Variable args type can only be used as an argument type", enumerator.file_index, enumerator.current);

        return type_definition;
    }

    // Determine whether to parse a generic type, otherwise return
    token: Token;
    if peek(enumerator, &token) && token.type == TokenType.LessThan {
        // Clear the '<' before entering loop
        move_next(enumerator);
        comma_required_before_next_type := false;
        while move_next(enumerator) {
            token = enumerator.current;

            if token.type == TokenType.GreaterThan {
                if !comma_required_before_next_type && type_definition.generics.length > 0
                    report_error("Unexpected comma in type", enumerator.file_index, token);

                break;
            }
            else if token.type == TokenType.ShiftRight {
                // Split the token and insert a greater than after the current token
                token.value = ">";
                new_token: Token = { type = TokenType.GreaterThan; value = ">"; line = token.line; column = token.column + 1; }
                insert(enumerator, new_token);
                break;
            }
            else if token.type == TokenType.RotateRight {
                // Split the token and insert a shift right after the current token
                token.value = ">";
                new_token: Token = { type = TokenType.ShiftRight; value = ">>"; line = token.line; column = token.column + 1; }
                insert(enumerator, new_token);
                break;
            }

            if !comma_required_before_next_type {
                if token.type == TokenType.Identifier
                    array_insert(&type_definition.generics, parse_type(enumerator, current_function, depth = depth + 1), allocate, reallocate);
                else
                    report_error("Unexpected token '%' in type definition", enumerator.file_index, token, token.value);

                comma_required_before_next_type = true;
            }
            else {
                if token.type != TokenType.Comma
                    report_error("Unexpected token '%' in type definition", enumerator.file_index, token, token.value);

                comma_required_before_next_type = false;
            }
        }

        if type_definition.generics.length == 0
            report_error("Expected type to contain generics", enumerator.file_index, enumerator.current);
    }

    while peek(enumerator, &token) && token.type == TokenType.Asterisk {
        move_next(enumerator);
        pointer_type := create_ast<TypeDefinition>(enumerator, AstType.TypeDefinition);
        pointer_type.name = "*";
        array_resize(&pointer_type.generics, 1, allocate, reallocate);
        pointer_type.generics[0] = type_definition;
        type_definition = pointer_type;
    }

    if peek(enumerator, &token) && token.type == TokenType.OpenBracket {
        // Skip over the open bracket and parse the expression
        move_next(enumerator);
        move_next(enumerator);
        type_definition.count = parse_expression(enumerator, current_function, TokenType.CloseBracket);
    }

    return type_definition;
}

bool try_parse_type(TokenEnumerator* enumerator, TypeDefinition** typeDef) {
    steps := 0;
    _: bool;
    success, type_definition := try_parse_type(enumerator.current, enumerator, &steps, &_, &_);
    if success {
        move(enumerator, steps);
        *typeDef = type_definition;
        return true;
    }
    return false;
}

bool, TypeDefinition* try_parse_type(Token name, TokenEnumerator* enumerator, int* steps, bool* ends_with_shift, bool* ends_with_rotate, int depth = 0) {
    type_definition := create_ast<TypeDefinition>(name, enumerator.file_index, AstType.TypeDefinition);
    type_definition.name = name.value;
    *ends_with_shift = false;
    *ends_with_rotate = false;

    // Alias int to s32
    if type_definition.name == "int"
        type_definition.name = "s32";

    // Determine whether to parse a generic type, otherwise return
    token: Token;
    if peek(enumerator, &token, *steps) && token.type == TokenType.LessThan {
        // Clear the '<' before entering loop
        *steps = *steps + 1;
        comma_required_before_next_type := false;
        while peek(enumerator, &token, *steps) {
            *steps = *steps + 1;
            if token.type == TokenType.GreaterThan {
                if !comma_required_before_next_type && type_definition.generics.length > 0
                    return false, null;

                break;
            }
            else if token.type == TokenType.ShiftRight {
                if (depth % 3 != 1 && !*ends_with_shift) || (!comma_required_before_next_type && type_definition.generics.length > 0)
                    return false, null;

                *ends_with_shift = true;
                break;
            }
            else if (token.type == TokenType.RotateRight)
            {
                if (depth % 3 != 2 && !*ends_with_rotate) || (!comma_required_before_next_type && type_definition.generics.length > 0)
                    return false, null;

                *ends_with_rotate = true;
                break;
            }

            if !comma_required_before_next_type {
                if token.type == TokenType.Identifier {
                    success, genericType := try_parse_type(token, enumerator, steps, ends_with_shift, ends_with_rotate, depth + 1);

                    if !success return false, null;
                    if *ends_with_shift || *ends_with_rotate {
                        *steps = *steps - 1;
                    }

                    array_insert(&type_definition.generics, genericType, allocate, reallocate);
                    comma_required_before_next_type = true;
                }
                else
                    return false, null;
            }
            else
            {
                if token.type != TokenType.Comma
                    return false, null;

                comma_required_before_next_type = false;
            }
        }

        if type_definition.generics.length == 0
            return false, null;
    }

    while peek(enumerator, &token, *steps) && token.type == TokenType.Asterisk {
        pointer_type := create_ast<TypeDefinition>(token, enumerator.file_index, AstType.TypeDefinition);
        pointer_type.name = "*";
        array_resize(&pointer_type.generics, 1, allocate, reallocate);
        pointer_type.generics[0] = type_definition;

        type_definition = pointer_type;
        *steps = *steps + 1;
        *ends_with_shift = false;
        *ends_with_rotate = false;
    }

    return true, type_definition;
}

ConstantAst* parse_constant(Token token, int fileIndex) {
    constant := create_ast<ConstantAst>(token, fileIndex, AstType.Constant);
    switch token.type {
        case TokenType.Literal; {
            constant.string = token.value;
        }
        case TokenType.Character; {
            constant.type = &u8_type;
            constant.value.unsigned_integer = token.value[0];
        }
        case TokenType.Number; {
            if token.flags == TokenFlags.None {
                // 32 bit signed integer
                int_value: int;
                if try_parse_integer(token.value, &int_value) {
                    constant.value.integer = int_value;
                    constant.type = &s32_type;
                }
                // 64 bit signed integer
                else if try_parse_s64(token.value, &constant.value.integer) {
                    constant.type = &s64_type;
                }
                // 64 bit unsigned integer
                else if try_parse_u64(token.value, &constant.value.unsigned_integer) {
                    constant.type = &u64_type;
                }
                else {
                    report_error("Invalid integer '%', must be 64 bits or less", fileIndex, token, token.value);
                    return null;
                }
            }
            else if token.flags & TokenFlags.Float {
                if try_parse_float(token.value, &constant.value.double) {
                    constant.type = &float_type;
                }
                else if try_parse_float64(token.value, &constant.value.double) {
                    constant.type = &float64_type;
                }
                else {
                    report_error("Invalid floating point number '%', must be single or double precision", fileIndex, token, token.value);
                    return null;
                }
            }
            else if token.flags & TokenFlags.HexNumber {
                if token.value.length == 2 {
                    report_error("Invalid number '%'", fileIndex, token, token.value);
                    return null;
                }

                value := substring(token.value, 2, token.value.length - 2);
                if value.length <= 8 {
                    constant.type = &u32_type;
                    constant.value.unsigned_integer = parse_hex_number(value);
                }
                else if value.length <= 16 {
                    constant.type = &u64_type;
                    constant.value.unsigned_integer = parse_hex_number(value);
                }
                else {
                    report_error("Invalid integer '%'", fileIndex, token, token.value);
                    return null;
                }
            }
            else {
                report_error("Unable to determine type of token '%'", fileIndex, token, token.value);
                return null;
            }
        }
        case TokenType.Boolean; {
            constant.type = &bool_type;
            constant.value.boolean = token.value == "true";
        }
        default; {
            report_error("Unable to determine type of token '%'", fileIndex, token, token.value);
            return null;
        }
    }
    return constant;
}

bool try_parse_integer(string input, int* value) {
    accum: int;
    if input[0] == '-' {
        if input.length > 11 || (input.length == 11 && input[1] > '2')
            return false;

        each i in 1..input.length - 1 {
            accum *= 10;
            accum += input[i] - '0';
        }

        if accum < 0 return false;
        *value = -accum;
    }
    else if input.length > 10 || (input.length == 10 && input[0] > '2') {
        return false;
    }
    else {
        each i in 0..input.length - 1 {
            accum *= 10;
            accum += input[i] - '0';
        }
        if accum < 0 return false;
        *value = accum;
    }

    return true;
}

bool try_parse_s64(string input, s64* value) {
    accum: s64;
    if input[0] == '-' {
        if input.length > 20
            return false;

        each i in 1..input.length - 1 {
            accum *= 10;
            accum += input[i] - '0';
        }

        if accum < 0 return false;
        *value = -accum;
    }
    else if input.length > 19 {
        return false;
    }
    else {
        each i in 0..input.length - 1 {
            accum *= 10;
            accum += input[i] - '0';
        }
        if accum < 0 return false;
        *value = accum;
    }

    return true;
}

bool try_parse_u64(string input, u64* value) {
    if input[0] == '-' || input.length > 19 || (input.length == 19 && input[0] > '1')
        return false;

    accum: u64;
    each i in 0..input.length - 1 {
        accum *= 10;
        accum += input[i] - '0';
    }
    *value = accum;

    return true;
}

u64 parse_hex_number(string input) {
    value: u64;
    each i in 0..input.length - 1 {
        digit := input[i];
        if digit <= '9' digit -= '0';
        else digit -= 55;

        value += digit << (input.length - i - 1) * 4;
    }

    return value;
}

// Not the best, but will likely work for now
bool try_parse_float(string input, float64* value) {
    accum: float64;
    part: u64;
    divisor: u64 = 1;
    if input[0] == '-' {
        i := 1;
        while i < input.length - 1 {
            char := input[i++];
            if char == '.' break;

            part *= 10;
            part += char - '0';
        }

        accum += part;
        part = 0;

        while i < input.length - 1 {
            part *= 10;
            divisor *= 10;
            part += input[i++] - '0';
        }

        accum += cast(float64, part) / divisor;
        *value = -accum;
    }
    else {
        i := 0;
        while i < input.length - 1 {
            char := input[i++];
            if char == '.' break;

            part *= 10;
            part += char - '0';
        }

        accum += part;
        part = 0;

        while i < input.length - 1 {
            part *= 10;
            divisor *= 10;
            part += input[i++] - '0';
        }

        accum += cast(float64, part) / divisor;
        *value = accum;
    }

    return true;
}

// @Cleanup Shouldn't be necessary
bool try_parse_float64(string input, float64* value) {
    return try_parse_float(input, value);
}

CastAst* parse_cast(TokenEnumerator* enumerator, IFunction* current_function) {
    cast_ast := create_ast<CastAst>(enumerator, AstType.Cast);

    // 1. Try to get the open paren to begin the cast
    if !move_next(enumerator) || enumerator.current.type != TokenType.OpenParen {
        report_error("Expected '(' after 'cast'", enumerator.file_index, enumerator.current);
        return null;
    }

    // 2. Get the target type
    if !move_next(enumerator) {
        report_error("Expected to get the target type for the cast", enumerator.file_index, enumerator.last);
        return null;
    }
    cast_ast.target_type_definition = parse_type(enumerator, current_function);
    if current_function {
        each generic, i in current_function.generics {
            if search_for_generic(generic, i, cast_ast.target_type_definition) {
                cast_ast.flags |= AstFlags.Generic;
            }
        }
    }

    // 3. Expect to get a comma
    if !move_next(enumerator) || enumerator.current.type != TokenType.Comma {
        report_error("Expected ',' after type in cast", enumerator.file_index, enumerator.current);
        return null;
    }

    // 4. Get the value expression
    if !move_next(enumerator) {
        report_error("Expected to get the value for the cast", enumerator.file_index, enumerator.last);
        return null;
    }
    cast_ast.value = parse_expression(enumerator, current_function, TokenType.CloseParen);

    return cast_ast;
}

Operator convert_operator(Token token) {
    switch token.type {
        // Multi-character operators
        case TokenType.And;
            return Operator.And;
        case TokenType.Or;
            return Operator.Or;
        case TokenType.Equality;
            return Operator.Equality;
        case TokenType.NotEqual;
            return Operator.NotEqual;
        case TokenType.GreaterThanEqual;
            return Operator.GreaterThanEqual;
        case TokenType.LessThanEqual;
            return Operator.LessThanEqual;
        case TokenType.ShiftLeft;
            return Operator.ShiftLeft;
        case TokenType.ShiftRight;
            return Operator.ShiftRight;
        case TokenType.RotateLeft;
            return Operator.RotateLeft;
        case TokenType.RotateRight;
            return Operator.RotateRight;
        // Handle single character operators
        case TokenType.Plus;
        case TokenType.Minus;
        case TokenType.Asterisk;
        case TokenType.ForwardSlash;
        case TokenType.GreaterThan;
        case TokenType.LessThan;
        case TokenType.Pipe;
        case TokenType.Ampersand;
        case TokenType.Percent;
        case TokenType.Caret;
            return cast(Operator, cast(s32, token.type));
    }

    return Operator.None;
}
