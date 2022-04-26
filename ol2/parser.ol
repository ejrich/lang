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
    // TypeChecker.privateScopes.Add(null);

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

    enumerator: TokenEnumerator = { tokens = tokens; file_index = file_index; }

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
                        if add_global_variable(variable)
                            add(&asts, variable);
                    }
                    else {
                        function := parse_function(&enumerator, attributes);
                        if function {
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
                        struct_ast.backend_name = struct_ast.name;
                        if add_struct(struct_ast) {
                            if struct_ast.name == "string" {
                                string_type = struct_ast;
                                struct_ast.type_kind = TypeKind.String;
                                struct_ast.used = true;
                            }
                            else if struct_ast.name == "Any" {
                                any_type = struct_ast;
                                struct_ast.type_kind = TypeKind.Any;
                                struct_ast.used = true;
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
                add_enum(enum_ast);
            }
            case TokenType.Union; {
                if attributes.length
                    report_error("Unions cannot have attributes", file_index, token);

                union_ast := parse_union(&enumerator);
                if union_ast {
                    add_union(union_ast);
                    add(&asts, union_ast);
                }
            }
            case TokenType.Pound; {
                if attributes.length
                    report_error("Compiler directives cannot have attributes", file_index, token);

                directive := parse_top_level_directive(&enumerator, true);
                if directive {
                    if directive.directive_type == DirectiveType.Library {
                        add_library(directive);
                    }
                    else if directive.directive_type == DirectiveType.SystemLibrary {
                        add_system_library(directive);
                    }
                    else add(&directives, directive);
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
                    add_interface(interface_ast);
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
                if (comma_required)
                    report_error("Expected comma between attributes", enumerator.file_index, token);

                attributes.Add(token.value);
                comma_required = true;
            }
            case TokenType.Comma; {
                if !comma_required
                    report_error("Expected attribute after comma or at beginning of attribute list", enumerator.file_index, token);

                comma_required = false;
            }
            default;
                report_error("Unexpected token '%' in attribute list", enumerator.file_index, token, token.value);
        }
    }

    if attributes.length == 0 report_error("Expected attribute(s) to be in attribute list", enumerator.file_index, enumerator.current);
    else if !comma_required report_error("Expected attribute after comma in attribute list", enumerator.file_index, enumerator.current);

    move_next(enumerator);

    return attributes;
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

FunctionAst* parse_function(TokenEnumerator* enumerator, Array<string> attributes) {
    // 1. Determine return type and name of the function
    function := create_ast<FunctionAst>(enumerator, AstType.Function);
    function.attributes = attributes;
    function.private = enumerator.private;

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
    if enumerator.current.type == TokenType.Comma
    {
        returnType := create_ast<TypeDefinition>(function.return_type_definition, AstType.TypeDefinition);
        returnType.compound = true;
        returnType.generics.Add(function.return_type_definition);
        function.return_type_definition = returnType;

        while enumerator.current.type == TokenType.Comma {
            if !move_next(enumerator)
                break;

            returnType.generics.Add(parse_type(enumerator));
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
                    else if function.generics.contains(generic.name)
                        report_error("Duplicate generic '%' in function '%'", generic.file_index, generic.line, generic.column, generic.name, function.name);
                    else
                        function.generics.Add(generic.name);
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
        // var generics = new HashSet<string>();
        while move_next(enumerator) {
            token = enumerator.current;

            if token.type == TokenType.GreaterThan {
                if !comma_required_before_next_type
                    report_error("Expected comma in generics of function '%'", enumerator.file_index, token, function.name);

                break;
            }

            if !comma_required_before_next_type {
                if token.type == TokenType.Identifier {
                    if !generics.Add(token.value)
                        report_error("Duplicate generic '%' in function '%'", enumerator.file_index, token, token.value, function.name);
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

        if generics.length == 0
            report_error("Expected function to contain generics", enumerator.file_index, enumerator.current);

        move_next(enumerator);
        function.generics.AddRange(generics);
    }

    // 3. Search for generics in the function return type
    if (function.return_type_definition != null) {
        each generic, i in function.generics {
            if search_for_generic(generic, i, function.return_type_definition) {
                function.flags |= FunctionFlags.ReturnTypeHasGenerics;
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
                    function.arguments.Add(current_argument);
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
                            current_argument.has_generics = true;
                        }
                    }
                }
                else {
                    current_argument.name = token.value;
                    comma_required_before_next_argument = true;
                }
            case TokenType.Comma; {
                if comma_required_before_next_argument
                    function.arguments.Add(current_argument);
                else
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);

                current_argument = null;
                comma_required_before_next_argument = false;
            }
            case TokenType.Equals; {
                if comma_required_before_next_argument {
                    move_next(enumerator);
                    current_argument.value = parse_expression(enumerator, function, null, TokenType.Comma, TokenType.CloseParen);
                    if !enumerator.remaining {
                        report_error("Incomplete definition for function '%'", enumerator.file_index, enumerator.last, function.name);
                        return null;
                    }

                    switch enumerator.current.type {
                        case TokenType.Comma; {
                            comma_required_before_next_argument = false;
                            function.arguments.Add(current_argument);
                            current_argument = null;
                        }
                        case TokenType.CloseParen; {
                            function.arguments.Add(current_argument);
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
        if !move_next(enumerator) {
            report_error("Expected compiler directive value", enumerator.file_index, enumerator.last);
            return null;
        }

        switch enumerator.current.value {
            case "extern"; {
                function.flags |= FunctionFlags.Extern;
                externError := "Extern function definition should be followed by the library in use"; #const
                if !peek(enumerator, &token) {
                    report_error(externError, enumerator.file_index, token);
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
                    report_error(externError, enumerator.file_index, token);

                return function;
            }
            case "compiler"; {
                function.flags |= FunctionFlags.Compiler;
                return function;
            }
            case "syscall"; {
                function.flags |= FunctionFlags.Syscall;
                syscall_error := "Syscall function definition should be followed by the number for the system call"; #const
                if !peek(enumerator, &token) {
                    report_error(syscall_error, enumerator.file_index, token);
                }
                else if token.type == TokenType.Number {
                    move_next(enumerator);
                    value: int;
                    if token.flags == TokenFlags.None && int.TryParse(token.value, &value) {
                        function.syscall = value;
                    }
                    else report_error(syscall_error, enumerator.file_index, token);
                }
                else report_error(syscall_error, enumerator.file_index, token);

                return function;
            }
            case "print_ir";
                function.flags |= FunctionFlags.PrintIR;
            case "call_location";
                function.flags |= FunctionFlags.PassCallLocation;
            case "inline";
                function.flags |= FunctionFlags.Inline;
            default;
                report_error("Unexpected compiler directive '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
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
    struct_ast := create_ast<StructAst>(enumerator, AstType.Struct);
    struct_ast.attributes = attributes;
    struct_ast.private = enumerator.private;

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
        // var generics = new HashSet<string>();
        while move_next(enumerator) {
            token = enumerator.current;

            if token.type == TokenType.GreaterThan {
                if !comma_required_before_next_type
                    report_error("Expected comma in generics for struct '%'", enumerator.file_index, token, struct_ast.name);

                break;
            }

            if !comma_required_before_next_type {
                if token.type == TokenType.Identifier {
                    if !generics.Add(token.value) {
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

        if !generics.Any()
            report_error("Expected struct '' to contain generics", enumerator.file_index, enumerator.current, struct_ast.name);

        struct_ast.generics = generics.ToList();
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

        struct_ast.fields.Add(parse_struct_field(enumerator));
    }

    // 6. Mark field types as generic if necessary
    if struct_ast.generics.length {
        each generic, i in struct_ast.generics {
            each field in struct_ast.fields {
                if field.type_definition != null && search_for_generic(generic, i, field.type_definition) {
                    field.has_generics = true;
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
    enum_ast := create_ast<EnumAst>(enumerator, AstType.Enum);
    enum_ast.attributes = attributes;
    enum_ast.private = enumerator.private;

    // 1. Determine name of enum
    if !move_next(enumerator) {
        report_error("Expected enum to have name", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.current.type == TokenType.Identifier {
        enum_ast.name = enumerator.current.value;
        enum_ast.backend_name = enumerator.current.value;
    }
    else
        report_error("Unexpected token '%' in enum definition", enumerator.file_index, enumerator.current, enumerator.current.value);

    // 2. Parse over the open brace
    move_next(enumerator);
    if enumerator.current.type == TokenType.Colon {
        move_next(enumerator);
        enum_ast.base_type_definition = parse_type(enumerator);
        move_next(enumerator);

        base_type := verify_type(enum_ast.base_type_definition, &global_scope);
        if base_type == null || base_type.type_kind != TypeKind.Integer {
            report_error("Base type of enum must be an integer, but got '%'", enum_ast.base_type_definition, print_type_definition(enum_ast.base_type_definition));
            enum_ast.base_type = &s32_type;
        }
        else {
            enum_ast.base_type = cast(PrimitiveAst*, base_type);
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
    lowest_allowed_value: int;
    largest_allowed_value: int;
    if enum_ast.base_type.signed {
        lowest_allowed_value = -(2 << (8 * enum_ast.size - 1));
        largest_allowed_value = 2 << (8 * enum_ast.size - 1) - 1;
    }
    else {
        largest_allowed_value = 2 << (8 * enum_ast.size) - 1;
    }
    largest_value := -1;
    enumIndex := 0;

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
                    value: EnumValueAst*;
                    if enum_ast.values.TryGetValue(token.value, &value) {
                        if !value.defined
                            report_error("Expected previously defined value '%' to have a defined value", enumerator.file_index, token, token.value);

                        current_value.value = value.value;
                        current_value.defined = true;
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
                        if !enum_ast.values.TryAdd(current_value.name, current_value)
                            report_error("Enum '%' already contains value '%'", current_value, enum_ast.name, current_value.name);

                        if current_value.defined {
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
                        if int.TryParse(token.value, &value) {
                            current_value.value = value;
                            current_value.defined = true;
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
                        if sub_value.length <= 8 {
                            value: u32;
                            if uint.TryParse(sub_value, NumberStyles.HexNumber, &value) {
                                current_value.value = value;
                                current_value.defined = true;
                            }
                        }
                        else if sub_value.length <= 16 {
                            value: u64;
                            if ulong.TryParse(sub_value, NumberStyles.HexNumber, &value) {
                                current_value.value = value;
                                current_value.defined = true;
                            }
                        }
                        else report_error("Expected enum value to be an integer, but got '%'", enumerator.file_index, token, token.value);
                    }
                    parsing_value_default = false;
                }
                else report_error("Unexpected token '%' in enum", enumerator.file_index, token, token.value);
            case TokenType.Character;
                if current_value != null && parsing_value_default {
                    current_value.value = token.value[0];
                    current_value.defined = true;
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
    union_ast := create_ast<UnionAst>(enumerator, AstType.Union);
    union_ast.private = enumerator.private;

    // 1. Determine name of union
    if !move_next(enumerator) {
        report_error("Expected union to have name", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.current.type == TokenType.Identifier {
        union_ast.name = enumerator.current.value;
        union_ast.backend_name = enumerator.current.value;
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

        union_ast.fields.Add(parse_union_field(enumerator));
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
        type.is_generic = true;
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

Ast* parse_line(TokenEnumerator* enumerator, Function* current_function) {
    if !enumerator.remaining {
        report_error("End of file reached without closing scope", enumerator.file_index, enumerator.last);
        return null;
    }

    token := enumerator.current;
    switch token.type {
        case TokenType.Return;
            return parse_return(enumerator, current_function);
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

ScopeAst* parse_scope(TokenEnumerator* enumerator, Function* current_function, bool topLevel = false) {
    scope_ast := create_ast<ScopeAst>(enumerator, AstType.Scope);

    closed := false;
    while move_next(enumerator) {
        if enumerator.current.type == TokenType.CloseBrace {
            closed = true;
            break;
        }

        if topLevel scope_ast.children.Add(parse_top_level_ast(enumerator));
        else scope_ast.children.Add(parse_line(enumerator, current_function));
    }

    if !closed
        report_error("Scope not closed by '}'", enumerator.file_index, enumerator.current);

    return scope_ast;
}

ConditionalAst* parse_conditional(TokenEnumerator* enumerator, Function* current_function, bool topLevel = false) {
    conditional_ast := create_ast<ConditionalAst>(enumerator, AstType.Conditional);

    // 1. Parse the conditional expression by first iterating over the initial 'if'
    move_next(enumerator);
    conditional_ast.condition = parse_condition_expression(enumerator, current_function);

    if !enumerator.remaining {
        report_error("Expected if to contain conditional expression and body", enumerator.file_index, enumerator.last);
        return null;
    }

    // 2. Determine how many lines to parse
    if enumerator.current.type == TokenType.OpenBrace {
        // Parse until close brace
        conditional_ast.if_block = parse_scope(enumerator, current_function, topLevel);
    }
    else {
        // Parse single AST
        conditional_ast.if_block = create_ast<ScopeAst>(enumerator, AstType.Scope);
        if topLevel conditional_ast.if_block.children.Add(parse_top_level_ast(enumerator));
        else conditional_ast.if_block.children.Add(parse_line(enumerator, current_function));
    }

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

        if enumerator.current.type == TokenType.OpenBrace {
            // Parse until close brace
            conditional_ast.else_block = parse_scope(enumerator, current_function, topLevel);
        }
        else {
            // Parse single AST
            conditional_ast.else_block = create_ast<ScopeAst>(enumerator, AstType.Scope);
            if topLevel conditional_ast.else_block.children.Add(parse_top_level_ast(enumerator));
            else conditional_ast.else_block.children.Add(parse_line(enumerator, current_function));
        }
    }

    return conditional_ast;
}

WhileAst* parse_while(TokenEnumerator* enumerator, Function* current_function) {
    while_ast := create_ast<WhileAst>(enumerator, AstType.While);

    // 1. Parse the conditional expression by first iterating over the initial 'while'
    move_next(enumerator);
    while_ast.condition = parse_condition_expression(enumerator, current_function);

    // 2. Determine how many lines to parse
    if !enumerator.remaining {
        report_error("Expected while loop to contain body", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.current.type == TokenType.OpenBrace {
        // Parse until close brace
        while_ast.body = parse_scope(enumerator, current_function);
    }
    else {
        // Parse single AST
        while_ast.body = create_ast<ScopeAst>(enumerator, AstType.Scope);
        while_ast.body.children.Add(parse_line(enumerator, current_function));
    }

    return while_ast;
}

EachAst* parse_each(TokenEnumerator* enumerator, Function* current_function) {
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
    if enumerator.current.type == TokenType.OpenBrace
        each_ast.body = parse_scope(enumerator, current_function);
    else {
        each_ast.body = create_ast<ScopeAst>(enumerator, AstType.Scope);
        each_ast.body.children.Add(parse_line(enumerator, current_function));
    }

    return each_ast;
}

Ast* parse_condition_expression(TokenEnumerator* enumerator, Function* current_function) {
    expression := create_ast<ExpressionAst>(enumerator, AstType.Expression);
    operator_required := false;

    move(enumerator, -1);
    while move_next(enumerator) {
        token := enumerator.current;

        if token.type == TokenType.OpenBrace
            break;

        if operator_required {
            if token.type == TokenType.Increment || token.type == TokenType.Decrement {
                // Create subexpression to hold the operation
                // This case would be `var b = 4 + a++`, where we have a value before the operator
                source := expression.children[expression.children.length - 1];
                change_by_one := create_ast<ChangeByOneAst>(source, AstType.ChangeByOne);
                change_by_one.positive = token.type == TokenType.Increment;
                change_by_one.value = source;
                expression.children[expression.children.length - 1] = change_by_one;
                continue;
            }
            if token.type == TokenType.Number && token.value[0] == '-' {
                token.value = substring(token.value, 1, token.value.length - 1);
                expression.operators.Add(Operator.Subtract);
                constant := parse_constant(token, enumerator.file_index);
                expression.children.Add(constant);
                continue;
            }
            if token.type == TokenType.Period {
                struct_field_ref := parse_struct_field_ref(enumerator, expression.children[expression.children.length - 1], current_function);
                expression.children[expression.children.length - 1] = struct_field_ref;
                continue;
            }
            op := convert_operator(token);
            if op != Operator.None {
                expression.operators.Add(op);
                operator_required = false;
            }
            else
                break;
        }
        else {
            ast := parse_next_expression_unit(enumerator, current_function, &operator_required);
            if ast expression.children.Add(ast);
        }
    }

    return check_expression(enumerator, expression, operator_required);
}

DeclarationAst* parse_declaration(TokenEnumerator* enumerator, Function* current_function = null, bool global = false) {
    declaration := create_ast<DeclarationAst>(enumerator, AstType.Declaration);
    declaration.global = global;
    declaration.private = enumerator.private;

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
                    declaration.has_generics = true;
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
        if (peek(enumerator, &token, 1) && token.value == "const") {
            declaration.constant = true;
            move_next(enumerator);
            move_next(enumerator);
        }
    }

    return declaration;
}

bool parse_value(Values* values, TokenEnumerator* enumerator, Function* current_function) {
    // 1. Step over '=' sign
    if !move_next(enumerator) {
        report_error("Expected declaration to have a value", enumerator.file_index, enumerator.last);
        return false;
    }

    // 2. Parse expression, constant, or object/array initialization as the value
    switch enumerator.current.type {
        case TokenType.OpenBrace; {
            // values.Assignments = new Dictionary<string, AssignmentAst>();
            while move_next(enumerator) {
                token := enumerator.current;
                if token.type == TokenType.CloseBrace
                    break;

                move_next: bool;
                assignment := parse_assignment(enumerator, current_function, &move_next);

                if !values.assignments.TryAdd(token.value, assignment)
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
            // values.ArrayValues = new List<IAst>();
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.CloseBracket
                    break;

                value := parse_expression(enumerator, current_function, null, TokenType.Comma, TokenType.CloseBracket);
                values.array_values.Add(value);
                if enumerator.current.type == TokenType.CloseBracket
                    break;
            }
        }
        default;
            values.value = parse_expression(enumerator, current_function);
    }

    return false;
}

AssignmentAst* parse_assignment(TokenEnumerator* enumerator, Function* current_function, bool* moveNext, Ast* reference = null) {
    // 1. Set the variable
    assignment: AssignmentAst*;
    if reference assignment = create_ast<AssignmentAst>(reference, AstType.Assignment);
    else assignment = create_ast<AssignmentAst>(enumerator, AstType.Assignment);

    assignment.reference = reference;
    *moveNext = false;

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
        if expression.children.length == 1 {
            assignment.reference = expression.children[0];
        }
        if expression.operators.length > 0 && expression.children.length == expression.operators.length {
            assignment.op = expression.operators.Last();
            expression.operators.RemoveAt(expression.operators.length - 1);
        }
    }

    // 4. Parse expression, field assignments, or array values
    switch enumerator.current.type {
        case TokenType.Equals;
            *moveNext = parse_value(assignment, enumerator, current_function);
        case TokenType.SemiColon;
            report_error("Expected assignment to have value", enumerator.file_index, enumerator.current);
        default; {
            report_error("Unexpected token '%' in assignment", enumerator.file_index, enumerator.current, enumerator.current.value);
            // Parse until there is an equals sign or semicolon
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.SemiColon break;
                if enumerator.current.type == TokenType.Equals {
                    *moveNext = parse_value(assignment, enumerator, current_function);
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

Ast* parse_expression(TokenEnumerator* enumerator, Function* current_function, ExpressionAst* expression = null, Params<TokenType> end_tokens) {
    operator_required: bool;

    if expression operator_required = true;
    else expression = create_ast<ExpressionAst>(enumerator, AstType.Expression);

    move(enumerator, -1);
    while move_next(enumerator) {
        token := enumerator.current;

        if is_end_token(token.type, end_tokens)
            break;

        if token.type == TokenType.Equals {
            _: bool;
            return parse_assignment(enumerator, current_function, &_, expression);
        }
        else if token.type == TokenType.Comma {
            if expression.children.length == 1
                return parse_compound_expression(enumerator, current_function, expression.children[0]);

            return parse_compound_expression(enumerator, current_function, expression);
        }

        if operator_required {
            if token.type == TokenType.Increment || token.type == TokenType.Decrement {
                // Create subexpression to hold the operation
                // This case would be `var b = 4 + a++`, where we have a value before the operator
                source := expression.children[expression.children.length - 1];
                change_by_one := create_ast<ChangeByOneAst>(source, AstType.ChangeByOne);
                change_by_one.positive = token.type == TokenType.Increment;
                change_by_one.value = source;
                expression.children[expression.children.length - 1] = change_by_one;
                continue;
            }
            if token.type == TokenType.Number && token.value[0] == '-'
            {
                token.value = substring(token.value, 1, token.value.length - 1);
                expression.operators.Add(Operator.Subtract);
                constant := parse_constant(token, enumerator.file_index);
                expression.children.Add(constant);
                continue;
            }
            if token.type == TokenType.Period {
                struct_field_ref := parse_struct_field_ref(enumerator, expression.children[expression.children.length - 1], current_function);
                expression.children[expression.children.length - 1] = struct_field_ref;
                continue;
            }
            op := convert_operator(token);
            if op != Operator.None {
                expression.operators.Add(op);
                operator_required = false;
            }
            else {
                report_error("Unexpected token '%' when operator was expected", enumerator.file_index, token, token.value);
                return null;
            }
        }
        else {
            ast := parse_next_expression_unit(enumerator, current_function, &operator_required);
            if ast
                expression.children.Add(ast);
        }
    }

    return check_expression(enumerator, expression, operator_required);
}

Ast* check_expression(TokenEnumerator* enumerator, ExpressionAst* expression, bool operator_required) {
    if expression.children.length == 0 {
        report_error("Expression should contain elements", enumerator.file_index, enumerator.current);
    }
    else if !operator_required && expression.children.length > 0 {
        report_error("Value required after operator", enumerator.file_index, enumerator.current);
        return expression;
    }

    if expression.children.length == 1
        return expression.children[0];

    if errors.length == 0
        set_operator_precedence(expression);

    return expression;
}

Ast* parse_compound_expression(TokenEnumerator* enumerator, Function* current_function, Ast* initial) {
    compound_expression := create_ast<CompoundExpressionAst>(initial, AstType.CompoundExpression);
    compound_expression.children.Add(initial);
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
                // compound_declaration.Variables = new VariableAst[compound_expression.Children.Count];

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
                compound_expression.children.Add(parse_expression(enumerator, current_function, null, TokenType.Comma, TokenType.Colon, TokenType.Equals));
                if enumerator.current.type == TokenType.Comma
                    move_next(enumerator);
            }
        }
    }

    if compound_expression.children.length == 1
        report_error("Expected compound expression to contain multiple values", enumerator.file_index, first_token);

    return compound_expression;
}

Ast* parse_struct_field_ref(TokenEnumerator* enumerator, Ast* initialAst, Function* current_function) {
    // 1. Initialize and move over the dot operator
    struct_field_ref := create_ast<StructFieldRefAst>(initialAst, AstType.StructFieldRef);
    struct_field_ref.children.Add(initialAst);

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
            ast := parse_next_expression_unit(enumerator, current_function, &operator_required);
            if ast
                struct_field_ref.children.Add(ast);
        }
    }

    return struct_field_ref;
}

Ast* parse_next_expression_unit(TokenEnumerator* enumerator, Function* current_function, bool* operator_required) {
    token := enumerator.current;
    *operator_required = true;
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

                            each generic in call_ast.generics {
                                each function_generic, i in current_function.generics {
                                    search_for_generic(function_generic, i, generic);
                                }
                            }

                            move_next(enumerator);
                            parse_arguments(call_ast, enumerator, current_function);
                            return call_ast;
                        }
                        else {
                            each generic, i in current_function.generics {
                                search_for_generic(generic, i, type_definition);
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
            positive := token.type == TokenType.Increment;
            if move_next(enumerator) {
                change_by_one := create_ast<ChangeByOneAst>(enumerator, AstType.ChangeByOne);
                change_by_one.prefix = true;
                change_by_one.positive = positive;
                change_by_one.value = parse_next_expression_unit(enumerator, current_function, operator_required);
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
                return parse_expression(enumerator, current_function, null, TokenType.CloseParen);

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
                unaryAst.value = parse_next_expression_unit(enumerator, current_function, operator_required);
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
        default; {
            report_error("Unexpected token '%' in expression", enumerator.file_index, token, token.value);
            *operator_required = false;
        }
   }
   return null;
}

set_operator_precedence(ExpressionAst* expression) {
    // 1. Set the initial operator precedence
    operator_precedence := get_operator_precedence(expression.operators[0]);
    i := 1;
    while i < expression.operators.length {
        // 2. Get the next operator
        precedence := get_operator_precedence(expression.operators[i]);

        // 3. Create subexpressions to enforce operator precedence if necessary
        if precedence > operator_precedence {
            end: int;
            sub_expression := create_sub_expression(expression, precedence, i, &end);
            expression.children[i] = sub_expression;
            expression.children.RemoveRange(i + 1, end - i);
            expression.operators.RemoveRange(i, end - i);

            if (i >= expression.operators.Count) return;
            operator_precedence = get_operator_precedence(expression.operators[--i]);
        }
        else
            operator_precedence = precedence;

        i++;
    }
}

ExpressionAst* create_sub_expression(ExpressionAst* expression, int parent_precedence, int i, int* end) {
    sub_expression := create_ast<ExpressionAst>(expression.children[i], AstType.Expression);

    sub_expression.children.Add(expression.children[i]);
    sub_expression.operators.Add(expression.operators[i]);
    ++i;
    while i < expression.operators.length
    {
        // 1. Get the next operator
        precedence := get_operator_precedence(expression.operators[i]);

        // 2. Create subexpressions to enforce operator precedence if necessary
        if precedence > parent_precedence {
            sub_expression.children.Add(create_sub_expression(expression, precedence, i, &i));
            if i == expression.operators.length {
                *end = i;
                return sub_expression;
            }

            sub_expression.operators.Add(expression.operators[i]);
        }
        else if precedence < parent_precedence {
            sub_expression.children.Add(expression.children[i]);
            *end = i;
            return sub_expression;
        }
        else {
            sub_expression.children.Add(expression.children[i]);
            sub_expression.operators.Add(expression.operators[i]);
        }

        i++;
    }

    sub_expression.children.Add(expression.children[expression.children.length - 1]);
    *end = i;
    return sub_expression;
}

int get_operator_precedence(Operator op) {
    switch op {
        // Boolean comparisons
        case Operator.And;
        case Operator.Or;
        case Operator.BitwiseAnd;
        case Operator.BitwiseOr;
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
        case Operator.Multiply;
        case Operator.Divide;
        case Operator.Modulus;
            return 20;
    }
    return 0;
}

CallAst* parse_call(TokenEnumerator* enumerator, Function* current_function, bool requiresSemicolon = false) {
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

parse_arguments(CallAst* call_ast, TokenEnumerator* enumerator, Function* current_function) {
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

                // call_ast.SpecifiedArguments ??= new Dictionary<string, IAst>();
                argument := parse_expression(enumerator, current_function, null, TokenType.Comma, TokenType.CloseParen);
                if !call_ast.specified_arguments.TryAdd(argument_name, argument)
                    report_error("Specified argument '%' is already in the call", enumerator.file_index, token, token.value);
            }
            else
                call_ast.arguments.Add(parse_expression(enumerator, current_function, null, TokenType.Comma, TokenType.CloseParen));

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

ReturnAst* parse_return(TokenEnumerator* enumerator, Function* current_function) {
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

IndexAst* parse_index(TokenEnumerator* enumerator, Function* current_function) {
    // 1. Initialize the index ast
    index := create_ast<IndexAst>(enumerator, AstType.Index);
    index.name = enumerator.current.value;
    move_next(enumerator);

    // 2. Parse index expression
    move_next(enumerator);
    index.index = parse_expression(enumerator, current_function, null, TokenType.CloseBracket);

    return index;
}

CompilerDirectiveAst* parse_top_level_directive(TokenEnumerator* enumerator, bool global = false) {
    directive := create_ast<CompilerDirectiveAst>(enumerator, AstType.CompilerDirective);

    if !move_next(enumerator) {
        report_error("Expected compiler directive to have a value", enumerator.file_index, enumerator.last);
        return null;
    }

    token := enumerator.current;
    switch token.value {
        case "run"; {
            directive.directive_type = DirectiveType.Run;
            move_next(enumerator);
            ast := parse_line(enumerator, null);
            if ast directive.value = ast;
        }
        case "if"; {
            directive.directive_type = DirectiveType.If;
            directive.value = parse_conditional(enumerator, null, true);
        }
        case "assert"; {
            directive.directive_type = DirectiveType.Assert;
            move_next(enumerator);
            directive.value = parse_expression(enumerator, null);
        }
        case "import"; {
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
        case "library"; {
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
        case "system_library"; {
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
        case "private"; {
            if (enumerator.private)
                report_error("Tried to set #private when already in private scope", enumerator.file_index, token);
            else
                TypeChecker.privateScopes[enumerator.file_index] = TODO; //new PrivateScope{Parent = TypeChecker.GlobalScope};
            enumerator.private = true;
        }
        default; {
            report_error("Unsupported top-level compiler directive '%'", enumerator.file_index, token, token.value);
            return null;
        }
    }

    return directive;
}

Ast* parse_compiler_directive(TokenEnumerator* enumerator, Function* current_function) {
    directive := create_ast<CompilerDirectiveAst>(enumerator, AstType.CompilerDirective);

    if !move_next(enumerator) {
        report_error("Expected compiler directive to have a value", enumerator.file_index, enumerator.last);
        return null;
    }

    token := enumerator.current;
    switch token.value {
        case "if"; {
            directive.directive_type = DirectiveType.If;
            directive.value = parse_conditional(enumerator, current_function);
            current_function.flags |= FunctionFlags.HasDirectives;
        }
        case "assert"; {
            directive.directive_type = DirectiveType.Assert;
            move_next(enumerator);
            directive.value = parse_expression(enumerator, current_function);
            current_function.flags |= FunctionFlags.HasDirectives;
        }
        case "inline"; {
            if !move_next(enumerator) || enumerator.current.type != TokenType.Identifier {
                report_error("Expected funciton call following #inline directive", enumerator.file_index, token);
                return null;
            }
            call := parse_call(enumerator, current_function, true);
            if call call.inline = true;
            return call;
        }
        default; {
            report_error("Unsupported function level compiler directive '%'", enumerator.file_index, token, token.value);
            return null;
        }
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

                    assembly.instructions.Add(instruction);
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
    if assembly.in_registers.ContainsKey(register) {
        report_error("Duplicate in register '%'", enumerator.file_index, enumerator.current, register);
        return false;
    }
    input := create_ast<AssemblyInputAst>(enumerator, AstType.AssemblyInput);
    input.register = register;

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

    _: bool;
    input.ast = parse_next_expression_unit(enumerator, null, &_);

    if !move_next(enumerator) {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    if enumerator.current.type != TokenType.SemiColon {
        report_error("Unexpected token '%' in assembly block", enumerator.file_index, enumerator.current, enumerator.current.value);
        return false;
    }

    assembly.in_registers[register] = input;
    return true;
}

bool parse_out_value(AssemblyAst* assembly, TokenEnumerator* enumerator) {
    if !move_next(enumerator) {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.last);
        return false;
    }

    output := create_ast<AssemblyInputAst>(enumerator, AstType.AssemblyInput);

    _: bool;
    output.ast = parse_next_expression_unit(enumerator, null, &_);
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

    assembly.out_values.Add(output);
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
    value.dereference = true;
    value.register = enumerator.current.value;

    if !move_next(enumerator) || enumerator.current.type != TokenType.CloseBracket {
        report_error("Expected to close pointer to register with ']'", enumerator.file_index, enumerator.current);
        return false;
    }

    return true;
}

SwitchAst* parse_switch(TokenEnumerator* enumerator, Function* current_function) {
    switch_ast := create_ast<SwitchAst>(enumerator, AstType.Switch);
    if !move_next(enumerator) {
        report_error("Expected value for switch statement", enumerator.file_index, enumerator.last);
        return null;
    }

    switch_ast.value = parse_expression(enumerator, current_function, null, TokenType.OpenBrace);

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
                if current_cases.case_ast {
                    report_error("Switch statement contains case(s) without bodies starting", current_cases.case_ast);
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

                if current_cases.case_ast == null
                    current_cases.case_ast = switch_case;
                else {
                    if current_cases.cases.length == 0
                        current_cases.cases[0] = current_cases.case_ast;

                    current_cases.cases.Add(switch_case);
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

                if enumerator.current.type == TokenType.OpenBrace
                    switch_ast.default_case = parse_scope(enumerator, current_function);
                else {
                    switch_ast.default_case = create_ast<ScopeAst>(enumerator, AstType.Scope);
                    switch_ast.default_case.children.Add(parse_line(enumerator, current_function));
                }
            }
            default; {
                if current_cases.case_ast {
                    // Parse through the case body and add to the list
                    if enumerator.current.type == TokenType.OpenBrace
                        current_cases.body = parse_scope(enumerator, current_function);
                    else {
                        current_cases.body = create_ast<ScopeAst>(enumerator, AstType.Scope);
                        current_cases.body.children.Add(parse_line(enumerator, current_function));
                    }

                    switch_ast.cases.Add(current_cases);
                    current_cases = { case_ast = null; body = null; }
                    current_cases.cases.length = 0;
                    current_cases.cases.data = null;
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

OperatorOverloadAst* parse_operator_overload(TokenEnumerator* enumerator)
{
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
        // var generics = new HashSet<string>();
        while move_next(enumerator) {
            token = enumerator.current;
            if token.type == TokenType.GreaterThan {
                if !comma_required_before_next_type
                    report_error("Expected comma in generics", enumerator.file_index, token);

                break;
            }

            if !comma_required_before_next_type {
                if token.type == TokenType.Identifier {
                    if (!generics.Add(token.value))
                        report_error("Duplicate generic '%'", enumerator.file_index, token, token.value);
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

        if generics.length == 0
            report_error("Expected operator overload to contain generics", enumerator.file_index, enumerator.current);

        move_next(enumerator);
        overload.generics.AddRange(generics);
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
                    overload.arguments.Add(current_argument);
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
                            current_argument.has_generics = true;
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
                    overload.arguments.Add(current_argument);
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
            overload.return_type_definition = new<TypeDefinition>();
            overload.return_type_definition.name = "bool"; // TODO Hardcode the ReturnType instead
        }
        case Operator.Subscript;
            if enumerator.current.type != TokenType.Colon {
                report_error("Unexpected to define return type for subscript", enumerator.file_index, enumerator.current);
            }
            else if move_next(enumerator) {
                overload.return_type_definition = parse_type(enumerator);
                each generic, i in overload.generics {
                    if search_for_generic(generic, i, overload.return_type_definition) {
                        overload.flags |= FunctionFlags.ReturnTypeHasGenerics;
                    }
                }
                move_next(enumerator);
            }
        default; {
            overload.return_type_definition = overload.type;
            if overload.generics.length {
                overload.flags |= FunctionFlags.ReturnTypeHasGenerics;
            }
            each generic, i in overload.generics {
                search_for_generic(generic, i, overload.return_type_definition);
            }
        }
    }

    // 6. Handle compiler directives
    if enumerator.current.type == TokenType.Pound {
        if !move_next(enumerator) {
            report_error("Expected compiler directive value", enumerator.file_index, enumerator.last);
            return null;
        }
        switch enumerator.current.value {
            case "print_ir";
                overload.flags |= FunctionFlags.PrintIR;
            default;
                report_error("Unexpected compiler directive '%'", enumerator.file_index, enumerator.current, enumerator.current.value);
        }
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
    interface_ast := create_ast<InterfaceAst>(enumerator, AstType.Interface);
    interface_ast.private = enumerator.private;
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
        returnType := create_ast<TypeDefinition>(interface_ast.return_type_definition, AstType.TypeDefinition);
        returnType.compound = true;
        returnType.generics.Add(interface_ast.return_type_definition);
        interface_ast.return_type_definition = returnType;

        while enumerator.current.type == TokenType.Comma {
            if !move_next(enumerator)
                break;

            returnType.generics.Add(parse_type(enumerator));
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
                    interface_ast.arguments.Add(current_argument);
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
                    interface_ast.arguments.Add(current_argument);
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
                            interface_ast.arguments.Add(current_argument);
                            current_argument = null;
                            break;
                        }
                        else if enumerator.current.type == TokenType.Comma {
                            interface_ast.arguments.Add(current_argument);
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

TypeDefinition* parse_type(TokenEnumerator* enumerator, Function* current_function = null, bool argument = false, int depth = 0) {
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
                    type_definition.generics.Add(parse_type(enumerator, current_function, depth = depth + 1));
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
        pointerType := create_ast<TypeDefinition>(enumerator, AstType.TypeDefinition);
        pointerType.name = "*";
        pointerType.generics.Add(type_definition);
        type_definition = pointerType;
    }

    if peek(enumerator, &token) && token.type == TokenType.OpenBracket {
        // Skip over the open bracket and parse the expression
        move_next(enumerator);
        move_next(enumerator);
        type_definition.count = parse_expression(enumerator, current_function, null, TokenType.CloseBracket);
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

                    type_definition.generics.Add(genericType);
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
        pointerType := create_ast<TypeDefinition>(token, enumerator.file_index, AstType.TypeDefinition);
        pointerType.name = "*";
        pointerType.generics.Add(type_definition);
        type_definition = pointerType;
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
                if int.TryParse(token.value, &constant.value.integer) {
                    constant.type = &s32_type;
                }
                else if long.TryParse(token.value, &constant.value.integer) {
                    constant.type = &s64_type;
                }
                else if (ulong.TryParse(token.value, &constant.value.unsigned_integer)) {
                    constant.type = &u64_type;
                }
                else {
                    report_error("Invalid integer '%', must be 64 bits or less", fileIndex, token, token.value);
                    return null;
                }
            }
            else if token.flags & TokenFlags.Float {
                if float.TryParse(token.value, &constant.value.double) {
                    constant.type = &float_type;
                }
                else if double.TryParse(token.value, &constant.value.double) {
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
                    if uint.TryParse(value, NumberStyles.HexNumber, &constant.value.unsigned_integer) {
                        constant.type = &u32_type;
                    }
                }
                else if value.length <= 16 {
                    if ulong.TryParse(value, NumberStyles.HexNumber, &constant.value.unsigned_integer) {
                        constant.type = &u64_type;
                    }
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

CastAst* parse_cast(TokenEnumerator* enumerator, Function* current_function) {
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
                cast_ast.has_generics = true;
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
    cast_ast.value = parse_expression(enumerator, current_function, null, TokenType.CloseParen);

    return cast_ast;
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

T* create_ast<T>(Token token, int fileIndex, AstType type) {
    ast := new<T>();
    ast.ast_type = type;
    ast.file_index = fileIndex;
    ast.line = token.line;
    ast.column = token.column;

    return ast;
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
            return cast(Operator, cast(s32, token.type));
    }

    return Operator.None;
}
