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
                        struct_ast.backend_name = structAst.name;
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
                    add(&asts, interfaceAst);
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

                attributes.Add(token.Value);
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
    switch token.Type {
        case TokenType.Identifier; {
            if peek(enumerator, &token) {
                if token.Type == TokenType.Colon {
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

            return parse_top_level_directive(enumerator, directory);
        }
        case TokenType.Operator; {
            if (attributes != null)
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
    function := create_ast<FunctionAst>(enumerator);
    function.Attributes = attributes;
    function.Private = enumerator.Private;

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
        returnType := create_ast<TypeDefinition>(function.return_type_definition);
        returnType.Compound = true;
        returnType.Generics.Add(function.return_type_definition);
        function.return_type_definition = returnType;

        while enumerator.Current.Type == TokenType.Comma {
            if !move_next(enumerator)
                break;

            returnType.Generics.Add(parse_type(enumerator));
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
            function.Name = enumerator.Current.Value;
            move_next(enumerator);
        }
        case TokenType.OpenParen; {
            if (function.return_type_definition.Name == "*" || function.return_type_definition.Count != null)
            {
                report_error("Expected the function name to be declared", function.return_type_definition);
            }
            else
            {
                function.Name = function.return_type_definition.Name;
                each generic in function.return_type_definition.Generics {
                    if (generic.Generics.Any())
                    {
                        report_error("Invalid generic in function '{function.Name}'", generic);
                    }
                    else if (function.Generics.Contains(generic.Name))
                    {
                        report_error("Duplicate generic '{generic.Name}' in function '{function.Name}'", generic.file_index, generic.Line, generic.Column);
                    }
                    else
                    {
                        function.Generics.Add(generic.Name);
                    }
                }
                function.return_type_definition = null;
            }
        }
        default; {
            report_error("Expected the function name to be declared", enumerator.file_index, enumerator.Current);
            move_next(enumerator);
        }
    }

    // 2. Parse generics
    if enumerator.Current.Type == TokenType.LessThan {
        commaRequiredBeforeNextType := false;
        // var generics = new HashSet<string>();
        while move_next(enumerator) {
            token = enumerator.Current;

            if token.type == TokenType.GreaterThan {
                if !commaRequiredBeforeNextType
                    report_error("Expected comma in generics of function '{function.Name}'", enumerator.file_index, token);

                break;
            }

            if !commaRequiredBeforeNextType {
                if token.type == TokenType.Identifier {
                    if !generics.Add(token.value)
                        report_error("Duplicate generic '{token.Value}' in function '{function.Name}'", enumerator.file_index, token);
                }
                else
                    report_error("Unexpected token '{token.Value}' in generics of function '{function.Name}'", enumerator.file_index, token);

                commaRequiredBeforeNextType = true;
            }
            else {
                if token.type != TokenType.Comma
                    report_error("Unexpected token '{token.Value}' in function '{function.Name}'", enumerator.file_index, token);

                commaRequiredBeforeNextType = false;
            }
        }

        if (!generics.Any())
        {
            report_error("Expected function to contain generics", enumerator.file_index, enumerator.Current);
        }
        move_next(enumerator);
        function.Generics.AddRange(generics);
    }

    // 3. Search for generics in the function return type
    if (function.return_type_definition != null) {
        each generic, i in function.Generics {
            if search_for_generic(generic, i, function.return_type_definition) {
                function.Flags |= FunctionFlags.ReturnTypeHasGenerics;
            }
        }
    }

    // 4. Find open paren to start parsing arguments
    if (enumerator.Current.Type != TokenType.OpenParen)
    {
        // Add an error to the function AST and continue until open paren
        token = enumerator.Current;
        report_error("Unexpected token '{token.Value}' in function definition", enumerator.file_index, token);
        while (enumerator.Remaining && enumerator.Current.Type != TokenType.OpenParen)
            move_next(enumerator);
    }

    // 5. Parse arguments until a close paren
    commaRequiredBeforeNextArgument := false;
    currentArgument: DeclarationAst*;
    while move_next(enumerator) {
        token = enumerator.Current;

        switch token.type {
            case TokenType.CloseParen; {
                if commaRequiredBeforeNextArgument {
                    function.Arguments.Add(currentArgument);
                    currentArgument = null;
                }
                break;
            }
            case TokenType.Identifier;
            case TokenType.VarArgs;
                if commaRequiredBeforeNextArgument {
                    report_error("Comma required after declaring an argument", enumerator.file_index, token);
                }
                else if (currentArgument == null)
                {
                    currentArgument = create_ast<DeclarationAst>(token, enumerator.file_index);
                    currentArgument.TypeDefinition = parse_type(enumerator, argument = true);
                    each generic, i in function.Generics {
                        if search_for_generic(generic, i, currentArgument.TypeDefinition) {
                            currentArgument.HasGenerics = true;
                        }
                    }
                }
                else
                {
                    currentArgument.Name = token.Value;
                    commaRequiredBeforeNextArgument = true;
                }
            case TokenType.Comma; {
                if (commaRequiredBeforeNextArgument)
                {
                    function.Arguments.Add(currentArgument);
                }
                else
                {
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);
                }
                currentArgument = null;
                commaRequiredBeforeNextArgument = false;
            }
            case TokenType.Equals; {
                if (commaRequiredBeforeNextArgument)
                {
                    move_next(enumerator);
                    currentArgument.Value = ParseExpression(enumerator, function, null, TokenType.Comma, TokenType.CloseParen);
                    if (!enumerator.Remaining)
                    {
                        report_error("Incomplete definition for function '{function.Name}'", enumerator.file_index, enumerator.Last);
                        return null;
                    }

                    switch enumerator.current.type {
                        case TokenType.Comma; {
                            commaRequiredBeforeNextArgument = false;
                            function.Arguments.Add(currentArgument);
                            currentArgument = null;
                        }
                        case TokenType.CloseParen; {
                            function.Arguments.Add(currentArgument);
                            currentArgument = null;
                        }
                        default;
                            report_error("Unexpected token '{enumerator.Current.Value}' in arguments of function '{function.Name}'", enumerator.file_index, enumerator.Current);
                    }
                }
                else
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);
            }
            default;
                report_error("Unexpected token '{token.Value}' in arguments", enumerator.file_index, token);
        }

        if enumerator.current.type == TokenType.CloseParen
            break;
    }

    if currentArgument
        report_error("Incomplete function argument in function '{function.Name}'", enumerator.file_index, enumerator.Current);

    if !commaRequiredBeforeNextArgument && function.Arguments.Any()
        report_error("Unexpected comma in arguments", enumerator.file_index, enumerator.Current);

    if !move_next(enumerator) {
        report_error("Unexpected function body or compiler directive", enumerator.file_index, enumerator.Last);
        return null;
    }

    // 6. Handle compiler directives
    while enumerator.current.type == TokenType.Pound {
        if !move_next(enumerator) {
            report_error("Expected compiler directive value", enumerator.file_index, enumerator.Last);
            return null;
        }

        switch enumerator.current.value {
            case "extern"; {
                function.Flags |= FunctionFlags.Extern;
                externError := "Extern function definition should be followed by the library in use"; #const
                if !peek(enumerator, &token) {
                    report_error(externError, enumerator.file_index, token);
                }
                else if token.type == TokenType.Literal {
                    move_next(enumerator);
                    function.ExternLib = token.Value;
                }
                else if token.type == TokenType.Identifier {
                    move_next(enumerator);
                    function.LibraryName = token.Value;
                }
                else
                    report_error(externError, enumerator.file_index, token);

                return function;
            }
            case "compiler"; {
                function.Flags |= FunctionFlags.Compiler;
                return function;
            }
            case "syscall"; {
                function.Flags |= FunctionFlags.Syscall;
                syscallError := "Syscall function definition should be followed by the number for the system call"; #const
                if !peek(enumerator, &token) {
                    report_error(syscallError, enumerator.file_index, token);
                }
                else if token.type == TokenType.Number {
                    move_next(enumerator);
                    value: int;
                    if (token.Flags == TokenFlags.None && int.TryParse(token.value, &value)) {
                        function.Syscall = value;
                    }
                    else report_error(syscallError, enumerator.file_index, token);
                }
                else report_error(syscallError, enumerator.file_index, token);

                return function;
            }
            case "print_ir";
                function.Flags |= FunctionFlags.PrintIR;
            case "call_location";
                function.Flags |= FunctionFlags.PassCallLocation;
            case "inline";
                function.Flags |= FunctionFlags.Inline;
            default;
                report_error("Unexpected compiler directive '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
        }
        move_next(enumerator);
    }

    // 7. Find open brace to start parsing body
    if (enumerator.Current.Type != TokenType.OpenBrace)
    {
        // Add an error and continue until open paren
        token = enumerator.Current;
        report_error("Unexpected token '{token.Value}' in function definition", enumerator.file_index, token);
        while move_next(enumerator) && enumerator.current.type != TokenType.OpenBrace {}

        if !enumerator.Remaining
            return function;
    }

    // 8. Parse function body
    function.Body = parse_scope(enumerator, function);

    return function;
}

StructAst* parse_struct(TokenEnumerator* enumerator, Array<string> attributes) {
    structAst := create_ast<StructAst>(enumerator);
    structAst.Attributes = attributes;
    structAst.Private = enumerator.Private;

    // 1. Determine name of struct
    if !move_next(enumerator) {
        report_error("Expected struct to have name", enumerator.file_index, enumerator.Last);
        return null;
    }

    if enumerator.current.type == TokenType.Identifier
        structAst.Name = enumerator.Current.Value;
    else
        report_error("Unexpected token '{enumerator.Current.Value}' in struct definition", enumerator.file_index, enumerator.Current);

    // 2. Parse struct generics
    token: Token;
    if !peek(enumerator, &token) {
        report_error("Unexpected struct body", enumerator.file_index, token);
        return null;
    }

    if token.type == TokenType.LessThan {
        // Clear the '<' before entering loop
        move_next(enumerator);
        commaRequiredBeforeNextType := false;
        // var generics = new HashSet<string>();
        while move_next(enumerator) {
            token = enumerator.Current;

            if token.type == TokenType.GreaterThan {
                if !commaRequiredBeforeNextType
                    report_error("Expected comma in generics for struct '{structAst.Name}'", enumerator.file_index, token);

                break;
            }

            if !commaRequiredBeforeNextType {
                if token.Type == TokenType.Identifier {
                    if !generics.Add(token.Value) {
                        report_error("Duplicate generic '{token.Value}' in struct '{structAst.Name}'", enumerator.file_index, token);
                    }
                }
                else report_error("Unexpected token '{token.Value}' in generics for struct '{structAst.Name}'", enumerator.file_index, token);

                commaRequiredBeforeNextType = true;
            }
            else {
                if token.type != TokenType.Comma
                    report_error("Unexpected token '{token.Value}' in definition of struct '{structAst.Name}'", enumerator.file_index, token);

                commaRequiredBeforeNextType = false;
            }
        }

        if !generics.Any()
            report_error("Expected struct '{structAst.Name}' to contain generics", enumerator.file_index, enumerator.Current);

        structAst.Generics = generics.ToList();
    }

    // 3. Get any inherited structs
    move_next(enumerator);
    if enumerator.current.type == TokenType.Colon {
        move_next(enumerator);
        structAst.BaseTypeDefinition = parse_type(enumerator);
        move_next(enumerator);
    }

    // 4. Parse over the open brace
    if enumerator.current.type != TokenType.OpenBrace {
        report_error("Expected '{' token in struct definition", enumerator.file_index, enumerator.Current);
        while move_next(enumerator) && enumerator.current.type != TokenType.OpenBrace {}
    }

    // 5. Iterate through fields
    while move_next(enumerator) {
        if enumerator.Current.Type == TokenType.CloseBrace
            break;

        structAst.Fields.Add(parse_struct_field(enumerator));
    }

    // 6. Mark field types as generic if necessary
    if structAst.generics.length {
        each generic, i in structAst.generics {
            each field in structAst.fields {
                if field.TypeDefinition != null && search_for_generic(generic, i, field.TypeDefinition) {
                    field.HasGenerics = true;
                }
            }
        }
    }

    return structAst;
}

StructFieldAst* parse_struct_field(TokenEnumerator* enumerator) {
    attributes := ParseAttributes(enumerator);
    structField := create_ast<StructFieldAst>(enumerator);
    structField.Attributes = attributes;

    if enumerator.current.type != TokenType.Identifier
        report_error("Expected name of struct field, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);

    structField.name = enumerator.current.value;

    // 1. Expect to get colon
    token: Token;
    move_next(enumerator);
    if enumerator.Current.Type != TokenType.Colon {
        token = enumerator.current;
        report_error("Unexpected token in struct field '{token.Value}'", enumerator.file_index, token);
        // Parse to a ; or }
        while move_next(enumerator) {
            tokenType := enumerator.current.type;
            if tokenType == TokenType.SemiColon || tokenType == TokenType.CloseBrace
                break;
        }
        return structField;
    }

    // 2. Check if type is given
    if !peek(enumerator, &token) {
        report_error("Expected type of struct field", enumerator.file_index, token);
        return null;
    }

    if token.type == TokenType.Identifier {
        move_next(enumerator);
        structField.TypeDefinition = parse_type(enumerator, null);
    }

    // 3. Get the value or return
    if !move_next(enumerator) {
        report_error("Expected declaration to have value", enumerator.file_index, enumerator.Last);
        return null;
    }

    token = enumerator.Current;
    switch token.Type {
        case TokenType.Equals;
            parse_value(structField, enumerator, null);
        case TokenType.SemiColon;
            if (structField.TypeDefinition == null)
                report_error("Expected struct field to have value", enumerator.file_index, token);
        default; {
            report_error("Unexpected token '{token.Value}' in struct field", enumerator.file_index, token);
            // Parse until there is an equals sign or semicolon
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.SemiColon break;
                if enumerator.current.type == TokenType.Equals {
                    parse_value(structField, enumerator, null);
                    break;
                }
            }
        }
    }

    return structField;
}

EnumAst* parse_enum(TokenEnumerator* enumerator, Array<string> attributes) {
    enumAst := create_ast<EnumAst>(enumerator);
    enumAst.Attributes = attributes;
    enumAst.Private = enumerator.Private;

    // 1. Determine name of enum
    if !move_next(enumerator) {
        report_error("Expected enum to have name", enumerator.file_index, enumerator.Last);
        return null;
    }

    if enumerator.current.type == TokenType.Identifier
        enumAst.name = enumAst.backend_name = enumerator.current.value;
    else
        report_error("Unexpected token '{enumerator.Current.Value}' in enum definition", enumerator.file_index, enumerator.Current);

    // 2. Parse over the open brace
    move_next(enumerator);
    if enumerator.Current.Type == TokenType.Colon {
        move_next(enumerator);
        enumAst.BaseTypeDefinition = parse_type(enumerator);
        move_next(enumerator);

        baseType := TypeChecker.VerifyType(enumAst.BaseTypeDefinition, TypeChecker.GlobalScope);
        if baseType.TypeKind != TypeKind.Integer {
            report_error("Base type of enum must be an integer, but got '{TypeChecker.PrintTypeDefinition(enumAst.BaseTypeDefinition)}'", enumAst.BaseTypeDefinition);
            enumAst.BaseType = TypeTable.S32Type;
        }
        else {
            enumAst.BaseType = cast(PrimitiveAst*, baseType);
            enumAst.Alignment = enumAst.Size = enumAst.BaseType.Size;
        }
    }
    else enumAst.BaseType = TypeTable.S32Type;

    if enumerator.Current.Type != TokenType.OpenBrace {
        report_error("Expected '{' token in enum definition", enumerator.file_index, enumerator.Current);
        while move_next(enumerator) && enumerator.Current.Type != TokenType.OpenBrace {}
    }

    // 3. Iterate through fields
    lowestAllowedValue: int;
    largestAllowedValue: int;
    if enumAst.BaseType.Signed {
        lowestAllowedValue = -(2 << (8 * enumAst.Size - 1));
        largestAllowedValue = 2 << (8 * enumAst.Size - 1) - 1;
    }
    else {
        largestAllowedValue = 2 << (8 * enumAst.Size) - 1;
    }
    largestValue := -1;
    enumIndex := 0;

    currentValue: EnumValueAst*;
    parsingValueDefault := false;
    while move_next(enumerator) {
        token := enumerator.Current;

        switch (token.Type)
        {
            case TokenType.CloseBrace;
                break;
            case TokenType.Identifier;
                if currentValue == null {
                    currentValue = create_ast<EnumValueAst>(token, enumerator.file_index);
                    currentValue.Index = enumIndex++;
                    currentValue.Name = token.Value;
                }
                else if parsingValueDefault {
                    parsingValueDefault = false;
                    value: EnumValueAst*;
                    if enumAst.Values.TryGetValue(token.Value, &value) {
                        if !value.Defined
                            report_error("Expected previously defined value '{token.Value}' to have a defined value", enumerator.file_index, token);

                        currentValue.Value = value.Value;
                        currentValue.Defined = true;
                    }
                    else
                        report_error("Expected value '{token.Value}' to be previously defined in enum '{enumAst.Name}'", enumerator.file_index, token);
                }
                else
                    report_error("Unexpected token '{token.Value}' in enum", enumerator.file_index, token);
            case TokenType.SemiColon;
                if currentValue {
                    // Catch if the name hasn't been set
                    if currentValue.Name == null || parsingValueDefault {
                        report_error("Unexpected token '{token.Value}' in enum", enumerator.file_index, token);
                    }
                    // Add the value to the enum and continue
                    else {
                        if !enumAst.Values.TryAdd(currentValue.Name, currentValue)
                            report_error("Enum '{enumAst.Name}' already contains value '{currentValue.Name}'", currentValue);

                        if currentValue.Defined {
                            if currentValue.Value > largestValue
                                largestValue = currentValue.Value;
                        }
                        else
                            currentValue.Value = ++largestValue;

                        if currentValue.Value < lowestAllowedValue || currentValue.Value > largestAllowedValue {
                            report_error("Enum value '{enumAst.Name}.{currentValue.Name}' value '{currentValue.Value}' is out of range", currentValue);
                        }
                    }

                    currentValue = null;
                    parsingValueDefault = false;
                }
            case TokenType.Equals;
                if (currentValue?.Name != null)
                    parsingValueDefault = true;
                else
                    report_error("Unexpected token '{token.Value}' in enum", enumerator.file_index, token);
            case TokenType.Number;
                if currentValue != null && parsingValueDefault {
                    if token.Flags == TokenFlags.None {
                        value: int;
                        if int.TryParse(token.value, &value) {
                            currentValue.Value = value;
                            currentValue.Defined = true;
                        }
                        else
                            report_error("Expected enum value to be an integer, but got '{token.Value}'", enumerator.file_index, token);
                    }
                    else if token.Flags & TokenFlags.Float {
                        report_error("Expected enum value to be an integer, but got '{token.Value}'", enumerator.file_index, token);
                    }
                    else if token.Flags & TokenFlags.HexNumber {
                        if token.Value.Length == 2
                            report_error("Invalid number '{token.Value}'", enumerator.file_index, token);

                        value := token.Value.Substring(2);
                        if value.Length <= 8 {
                            value: u32;
                            if uint.TryParse(value, NumberStyles.HexNumber, &value) {
                                currentValue.Value = value;
                                currentValue.Defined = true;
                            }
                        }
                        else if value.Length <= 16 {
                            value: u64;
                            if ulong.TryParse(value, NumberStyles.HexNumber, &value) {
                                currentValue.Value = value;
                                currentValue.Defined = true;
                            }
                        }
                        else report_error("Expected enum value to be an integer, but got '{token.Value}'", enumerator.file_index, token);
                    }
                    parsingValueDefault = false;
                }
                else report_error("Unexpected token '{token.Value}' in enum", enumerator.file_index, token);
            case TokenType.Character;
                if currentValue != null && parsingValueDefault {
                    currentValue.Value = token.Value[0];
                    currentValue.Defined = true;
                    parsingValueDefault = false;
                }
                else report_error("Unexpected token '{token.Value}' in enum", enumerator.file_index, token);
            default;
                report_error("Unexpected token '{token.Value}' in enum", enumerator.file_index, token);
        }
    }

    if currentValue {
        token := enumerator.Current;
        report_error("Unexpected token '{token.Value}' in enum", enumerator.file_index, token);
    }

    if enumAst.Values.length == 0
        report_error("Expected enum to have 1 or more values", enumAst);

    return enumAst;
}

UnionAst* parse_union(TokenEnumerator* enumerator) {
    union_ast := create_ast<UnionAst>(enumerator);
    union_ast.Private = enumerator.Private;

    // 1. Determine name of union
    if !move_next(enumerator) {
        report_error("Expected union to have name", enumerator.file_index, enumerator.Last);
        return null;
    }

    if enumerator.current.type == TokenType.Identifier
        union_ast.Name = union_ast.BackendName = enumerator.Current.Value;
    else
        report_error("Unexpected token '{enumerator.Current.Value}' in union definition", enumerator.file_index, enumerator.Current);

    // 2. Parse over the open brace
    move_next(enumerator);
    if enumerator.Current.Type != TokenType.OpenBrace {
        report_error("Expected '{' token in union definition", enumerator.file_index, enumerator.Current);
        while move_next(enumerator) && enumerator.Current.Type != TokenType.OpenBrace {}
    }

    // 3. Iterate through fields
    while move_next(enumerator) {
        if enumerator.current.type == TokenType.CloseBrace
            break;

        union_ast.Fields.Add(parse_union_field(enumerator));
    }

    return union_ast;
}

UnionFieldAst* parse_union_field(TokenEnumerator* enumerator) {
    field := create_ast<UnionFieldAst>(enumerator);

    if enumerator.Current.Type != TokenType.Identifier
        report_error("Expected name of union field, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);

    field.Name = enumerator.Current.Value;

    // 1. Expect to get colon
    token: Token;
    move_next(enumerator);
    if enumerator.Current.Type != TokenType.Colon {
        token = enumerator.Current;
        report_error("Unexpected token in union field '{token.Value}'", enumerator.file_index, token);
        // Parse to a ; or }
        while move_next(enumerator) {
            tokenType := enumerator.Current.Type;
            if tokenType == TokenType.SemiColon || tokenType == TokenType.CloseBrace
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
        field.TypeDefinition = parse_type(enumerator, null);
    }

    // 3. Get the value or return
    if !move_next(enumerator) {
        report_error("Expected union to be closed by a '}'", enumerator.file_index, token);
        return null;
    }

    token = enumerator.Current;
    switch token.Type {
        case TokenType.SemiColon;
            if (field.TypeDefinition == null)
                report_error("Expected union field to have a type", enumerator.file_index, token);
        default; {
            report_error("Unexpected token '{token.Value}' in declaration", enumerator.file_index, token);
            // Parse until there is a semicolon or closing brace
            while move_next(enumerator) {
                token = enumerator.Current;
                if token.Type == TokenType.SemiColon || token.Type == TokenType.CloseBrace
                    break;
            }
        }
    }

    return field;
}

bool search_for_generic(string generic, int index, TypeDefinition* type) {
    if (type.Name == generic) {
        type.IsGeneric = true;
        type.GenericIndex = index;
        return true;
    }

    hasGeneric := false;
    each typeGeneric in type.Generics {
        if (search_for_generic(generic, index, typeGeneric)) {
            hasGeneric = true;
        }
    }
    return hasGeneric;
}

Ast* ParseLine(TokenEnumerator* enumerator, Function* current_function) {
    if !enumerator.remaining {
        report_error("End of file reached without closing scope", enumerator.file_index, enumerator.Last);
        return null;
    }

    token := enumerator.Current;
    switch token.Type {
        case TokenType.Return;
            return parse_return(enumerator, currentFunction);
        case TokenType.If;
            return parse_conditional(enumerator, currentFunction);
        case TokenType.While;
            return parse_while(enumerator, currentFunction);
        case TokenType.Each;
            return parse_each(enumerator, currentFunction);
        case TokenType.Identifier; {
            if (!peek(enumerator, &token))
            {
                report_error("End of file reached without closing scope", enumerator.file_index, enumerator.Last);
                return null;
            }
            switch token.Type {
                case TokenType.OpenParen;
                    return parse_call(enumerator, currentFunction, true);
                case TokenType.Colon;
                    return parse_declaration(enumerator, currentFunction);
                case TokenType.Equals;
                    return ParseAssignment(enumerator, currentFunction);
            }
            return ParseExpression(enumerator, currentFunction);
        }
        case TokenType.Increment;
        case TokenType.Decrement;
        case TokenType.Asterisk;
            return ParseExpression(enumerator, currentFunction);
        case TokenType.OpenBrace;
            return parse_scope(enumerator, currentFunction);
        case TokenType.Pound;
            return parse_compiler_directive(enumerator, currentFunction);
        case TokenType.Asm;
            return parse_inline_assembly(enumerator);
        case TokenType.Switch;
            return parse_switch(enumerator, currentFunction);
        case TokenType.Break; {
            breakAst := create_ast<Ast>(token, enumerator.file_index);
            if move_next(enumerator) {
                if enumerator.current.type != TokenType.SemiColon {
                    report_error("Expected ';'", enumerator.file_index, enumerator.current);
                }
            }
            else report_error("End of file reached without closing scope", enumerator.file_index, enumerator.Last);

            return breakAst;
        }
        case TokenType.Continue; {
            continueAst := create_ast<Ast>(token, enumerator.file_index);
            if move_next(enumerator) {
                if enumerator.Current.Type != TokenType.SemiColon {
                    report_error("Expected ';'", enumerator.file_index, enumerator.Current);
                }
            }
            else report_error("End of file reached without closing scope", enumerator.file_index, enumerator.Last);

            return continueAst;
        }
    }

    report_error("Unexpected token '{token.Value}'", enumerator.file_index, token);
    return null;
}

ScopeAst* parse_scope(TokenEnumerator* enumerator, Function* current_function, bool topLevel = false) {
    scopeAst := create_ast<ScopeAst>(enumerator);

    closed := false;
    while move_next(enumerator) {
        if enumerator.current.type == TokenType.CloseBrace {
            closed = true;
            break;
        }

        if topLevel scopeAst.Children.Add(parse_top_level_ast(enumerator, directory));
        else scopeAst.Children.Add(ParseLine(enumerator, currentFunction));
    }

    if !closed
        report_error("Scope not closed by '}'", enumerator.file_index, enumerator.Current);

    return scopeAst;
}

ConditionalAst* parse_conditional(TokenEnumerator* enumerator, Function* current_function, bool topLevel = false) {
    conditionalAst := create_ast<ConditionalAst>(enumerator);

    // 1. Parse the conditional expression by first iterating over the initial 'if'
    move_next(enumerator);
    conditionalAst.Condition = parse_condition_expression(enumerator, currentFunction);

    if !enumerator.Remaining {
        report_error("Expected if to contain conditional expression and body", enumerator.file_index, enumerator.Last);
        return null;
    }

    // 2. Determine how many lines to parse
    if enumerator.Current.Type == TokenType.OpenBrace {
        // Parse until close brace
        conditionalAst.IfBlock = parse_scope(enumerator, currentFunction, topLevel, directory);
    }
    else {
        // Parse single AST
        conditionalAst.IfBlock = create_ast<ScopeAst>(enumerator);
        if topLevel conditionalAst.IfBlock.Children.Add(parse_top_level_ast(enumerator));
        else conditionalAst.IfBlock.Children.Add(ParseLine(enumerator, currentFunction));
    }

    // 3. Parse else block if necessary
    token: Token;
    if !peek(enumerator, &token) {
        return conditionalAst;
    }

    if token.Type == TokenType.Else {
        // First clear the else and then determine how to parse else block
        move_next(enumerator);
        if !move_next(enumerator) {
            report_error("Expected body of else branch", enumerator.file_index, enumerator.Last);
            return null;
        }

        if enumerator.current.type == TokenType.OpenBrace {
            // Parse until close brace
            conditionalAst.ElseBlock = parse_scope(enumerator, currentFunction, topLevel, directory);
        }
        else {
            // Parse single AST
            conditionalAst.ElseBlock = create_ast<ScopeAst>(enumerator);
            if topLevel conditionalAst.ElseBlock.Children.Add(parse_top_level_ast(enumerator));
            else conditionalAst.ElseBlock.Children.Add(ParseLine(enumerator, currentFunction));
        }
    }

    return conditionalAst;
}

WhileAst* parse_while(TokenEnumerator* enumerator, Function* current_function) {
    whileAst := create_ast<WhileAst>(enumerator);

    // 1. Parse the conditional expression by first iterating over the initial 'while'
    move_next(enumerator);
    whileAst.Condition = parse_condition_expression(enumerator, currentFunction);

    // 2. Determine how many lines to parse
    if !enumerator.Remaining {
        report_error("Expected while loop to contain body", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.Current.Type == TokenType.OpenBrace {
        // Parse until close brace
        whileAst.Body = parse_scope(enumerator, currentFunction);
    }
    else {
        // Parse single AST
        whileAst.Body = create_ast<ScopeAst>(enumerator);
        whileAst.Body.Children.Add(ParseLine(enumerator, currentFunction));
    }

    return whileAst;
}

EachAst* parse_each(TokenEnumerator* enumerator, Function* current_function) {
    eachAst := create_ast<EachAst>(enumerator);

    // 1. Parse the iteration variable by first iterating over the initial 'each'
    move_next(enumerator);
    if enumerator.Current.Type == TokenType.Identifier {
        eachAst.IterationVariable = create_ast<VariableAst>(enumerator);
        eachAst.IterationVariable.Name = enumerator.Current.Value;
    }
    else
        report_error("Expected variable in each block definition", enumerator.file_index, enumerator.Current);

    move_next(enumerator);
    if enumerator.Current.Type == TokenType.Comma {
        move_next(enumerator);
        if enumerator.Current.Type == TokenType.Identifier {
            eachAst.IndexVariable = create_ast<VariableAst>(enumerator);
            eachAst.IndexVariable.Name = enumerator.Current.Value;
            move_next(enumerator);
        }
        else
            report_error("Expected index variable after comma in each declaration", enumerator.file_index, enumerator.Current);
    }

    // 2. Parse over the in keyword
    if enumerator.Current.Type != TokenType.In {
        report_error("Expected 'in' token in each block", enumerator.file_index, enumerator.Current);
        while move_next(enumerator) && enumerator.Current.Type != TokenType.In {}
    }

    // 3. Determine the iterator
    move_next(enumerator);
    expression := parse_condition_expression(enumerator, currentFunction);

    // 3a. Check if the next token is a range
    if !enumerator.Remaining {
        report_error("Expected each block to have iteration and body", enumerator.file_index, enumerator.Last);
        return null;
    }

    if enumerator.current.type == TokenType.Range {
        if (eachAst.IndexVariable != null)
            report_error("Range enumerators cannot have iteration and index variable", enumerator.file_index, enumerator.Current);

        eachAst.RangeBegin = expression;
        if !move_next(enumerator) {
            report_error("Expected range to have an end", enumerator.file_index, enumerator.Last);
            return eachAst;
        }

        eachAst.RangeEnd = parse_condition_expression(enumerator, currentFunction);
        if !enumerator.Remaining {
            report_error("Expected each block to have iteration and body", enumerator.file_index, enumerator.Last);
            return eachAst;
        }
    }
    else
        eachAst.Iteration = expression;

    // 4. Determine how many lines to parse
    if enumerator.Current.Type == TokenType.OpenBrace
        eachAst.Body = parse_scope(enumerator, currentFunction);
    else {
        eachAst.Body = create_ast<ScopeAst>(enumerator);
        eachAst.Body.Children.Add(ParseLine(enumerator, currentFunction));
    }

    return eachAst;
}

Ast* parse_condition_expression(TokenEnumerator* enumerator, Function* current_function) {
    expression := create_ast<ExpressionAst>(enumerator);
    operatorRequired := false;

    move(enumerator, -1);
    while move_next(enumerator) {
        token := enumerator.current;

        if token.type == TokenType.OpenBrace
            break;

        if operatorRequired {
            if token.Type == TokenType.Increment || token.Type == TokenType.Decrement {
                // Create subexpression to hold the operation
                // This case would be `var b = 4 + a++`, where we have a value before the operator
                source := expression.Children[expression.Children.length - 1];
                changeByOneAst := create_ast<ChangeByOneAst>(source);
                changeByOneAst.Positive = token.Type == TokenType.Increment;
                changeByOneAst.Value = source;
                expression.Children[expression.Children.length - 1] = changeByOneAst;
                continue;
            }
            if token.Type == TokenType.Number && token.Value[0] == '-' {
                token.Value = substring(token.value, 1, token.value.length - 1);
                expression.Operators.Add(Operator.Subtract);
                constant := parse_constant(token, enumerator.file_index);
                expression.Children.Add(constant);
                continue;
            }
            if token.type == TokenType.Period {
                structFieldRef := parse_struct_field_ref(enumerator, expression.Children[expression.Children.length - 1], currentFunction);
                expression.Children[expression.Children.length - 1] = structFieldRef;
                continue;
            }
            op := convert_operator(token);
            if op != Operator.None {
                expression.Operators.Add(op);
                operatorRequired = false;
            }
            else
                break;
        }
        else {
            ast := parse_next_expression_unit(enumerator, currentFunction, &operatorRequired);
            if ast expression.Children.Add(ast);
        }
    }

    return CheckExpression(enumerator, expression, operatorRequired);
}

DeclarationAst* parse_declaration(TokenEnumerator* enumerator, Function* current_function = null, bool global = false) {
    declaration := create_ast<DeclarationAst>(enumerator);
    declaration.Global = global;
    declaration.Private = enumerator.Private;

    if enumerator.current.type != TokenType.Identifier {
        report_error("Expected variable name to be an identifier, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
    }

    declaration.Name = enumerator.Current.Value;

    // 1. Expect to get colon
    move_next(enumerator);
    if enumerator.current.type != TokenType.Colon {
        report_error("Unexpected token in declaration '{token.Value}'", enumerator.file_index, enumerator.Current);
        return null;
    }

    // 2. Check if type is given
    token: Token;
    if !peek(enumerator, &token) {
        report_error("Expected type or value of declaration", enumerator.file_index, token);
    }

    if token.Type == TokenType.Identifier {
        move_next(enumerator);
        declaration.TypeDefinition = parse_type(enumerator, currentFunction);
        if currentFunction {
            each generic, i in currentFunction.Generics {
                if search_for_generic(generic, i, declaration.TypeDefinition) {
                    declaration.HasGenerics = true;
                }
            }
        }
    }

    // 3. Get the value or return
    if !move_next(enumerator) {
        report_error("Expected declaration to have value", enumerator.file_index, enumerator.Last);
        return null;
    }

    token = enumerator.Current;
    switch token.Type {
        case TokenType.Equals;
            parse_value(declaration, enumerator, currentFunction);
        case TokenType.SemiColon;
            if (declaration.TypeDefinition == null)
                report_error("Expected declaration to have value", enumerator.file_index, token);
        default; {
            report_error("Unexpected token '{token.Value}' in declaration", enumerator.file_index, token);
            // Parse until there is an equals sign or semicolon
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.SemiColon break;
                if enumerator.current.type == TokenType.Equals {
                    parse_value(declaration, enumerator, currentFunction);
                    break;
                }
            }
        }
    }

    // 4. Parse compiler directives
    if peek(enumerator, &token) && token.Type == TokenType.Pound {
        if (peek(enumerator, &token, 1) && token.Value == "const") {
            declaration.Constant = true;
            move_next(enumerator);
            move_next(enumerator);
        }
    }

    return declaration;
}

bool parse_value(Values* values, TokenEnumerator* enumerator, Function* current_function) {
    // 1. Step over '=' sign
    if !move_next(enumerator) {
        report_error("Expected declaration to have a value", enumerator.file_index, enumerator.Last);
        return false;
    }

    // 2. Parse expression, constant, or object/array initialization as the value
    switch enumerator.current.type {
        case TokenType.OpenBrace; {
            // values.Assignments = new Dictionary<string, AssignmentAst>();
            while move_next(enumerator) {
                token := enumerator.Current;
                if token.Type == TokenType.CloseBrace
                    break;

                move_next: bool;
                assignment := ParseAssignment(enumerator, currentFunction, &move_next);

                if !values.Assignments.TryAdd(token.Value, assignment)
                    report_error("Multiple assignments for field '{token.Value}'", enumerator.file_index, token);

                if move_next {
                    peek(enumerator, &token);
                    if (token.Type == TokenType.CloseBrace) {
                        move_next(enumerator);
                        break;
                    }
                }
                else if enumerator.Current.Type == TokenType.CloseBrace
                    break;
            }
            return true;
        }
        case TokenType.OpenBracket; {
            // values.ArrayValues = new List<IAst>();
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.CloseBracket
                    break;

                value := ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.CloseBracket);
                values.ArrayValues.Add(value);
                if enumerator.current.type == TokenType.CloseBracket
                    break;
            }
        }
        default;
            values.Value = ParseExpression(enumerator, currentFunction);
    }

    return false;
}

AssignmentAst* ParseAssignment(TokenEnumerator* enumerator, Function* current_function, bool* moveNext, IAst reference = null) {
    // 1. Set the variable
    assignment: AssignmentAst*;
    if reference assignment = create_ast<AssignmentAst>(reference);
    else assignment = create_ast<AssignmentAst>(enumerator);

    assignment.Reference = reference;
    *moveNext = false;

    // 2. When the original reference is null, set the l-value to an identifier
    if reference == null {
        variableAst := create_ast<IdentifierAst>(enumerator);
        if enumerator.Current.Type != TokenType.Identifier {
            report_error("Expected variable name to be an identifier, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
        }
        variableAst.Name = enumerator.Current.Value;
        assignment.Reference = variableAst;

        // 2a. Expect to get equals sign
        if !move_next(enumerator) {
            report_error("Expected '=' in assignment'", enumerator.file_index, enumerator.Last);
            return assignment;
        }

        // 2b. Assign the operator is there is one
        token := enumerator.Current;
        if token.Type != TokenType.Equals {
            op := convert_operator(token);
            if op != Operator.None {
                assignment.Operator = op;
                if !peek(enumerator, &token) {
                    report_error("Expected '=' in assignment'", enumerator.file_index, token);
                    return null;
                }

                if token.Type == TokenType.Equals
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
        expression: ExpressionAst* = reference;
        if expression.Children.length == 1 {
            assignment.Reference = expression.Children[0];
        }
        if expression.Operators.length > 0 && expression.Children.length == expression.Operators.length {
            assignment.Operator = expression.Operators.Last();
            expression.Operators.RemoveAt(expression.Operators.Count - 1);
        }
    }

    // 4. Parse expression, field assignments, or array values
    switch enumerator.Current.Type {
        case TokenType.Equals;
            *moveNext = parse_value(assignment, enumerator, currentFunction);
        case TokenType.SemiColon;
            report_error("Expected assignment to have value", enumerator.file_index, enumerator.Current);
        default; {
            report_error("Unexpected token '{enumerator.Current.Value}' in assignment", enumerator.file_index, enumerator.Current);
            // Parse until there is an equals sign or semicolon
            while (move_next(enumerator))
            {
                if (enumerator.Current.Type == TokenType.SemiColon) break;
                if (enumerator.Current.Type == TokenType.Equals)
                {
                    moveNext = parse_value(assignment, enumerator, currentFunction);
                    break;
                }
            }
        }
    }

    return assignment;
}

bool is_end_token(TokenType token, Array<TokenType> end_tokens) {

}

Ast* ParseExpression(TokenEnumerator* enumerator, Function* current_function, ExpressionAst* expression = null, Params<TokenType> end_tokens) {
    operatorRequired: bool;

    if expression operatorRequired = true;
    else expression = create_ast<ExpressionAst>(enumerator);

    move(enumerator, -1);
    while move_next(enumerator) {
        token := enumerator.Current;

        if is_end_token(token.type, end_tokens)
            break;

        if token.Type == TokenType.Equals {
            _: bool;
            return ParseAssignment(enumerator, currentFunction, &_, expression);
        }
        else if token.Type == TokenType.Comma {
            if expression.Children.length == 1
                return ParseCompoundExpression(enumerator, currentFunction, expression.Children[0]);

            return ParseCompoundExpression(enumerator, currentFunction, expression);
        }

        if operatorRequired {
            if token.type == TokenType.Increment || token.type == TokenType.Decrement {
                // Create subexpression to hold the operation
                // This case would be `var b = 4 + a++`, where we have a value before the operator
                source := expression.Children[expression.Children.length - 1];
                changeByOneAst := create_ast<ChangeByOneAst>(source);
                changeByOneAst.Positive = token.Type == TokenType.Increment;
                changeByOneAst.Value = source;
                expression.Children[expression.Children.length - 1] = changeByOneAst;
                continue;
            }
            if token.type == TokenType.Number && token.value[0] == '-'
            {
                token.value = substring(token.value, 1, token.value.length - 1);
                expression.Operators.Add(Operator.Subtract);
                constant := parse_constant(token, enumerator.file_index);
                expression.Children.Add(constant);
                continue;
            }
            if token.Type == TokenType.Period {
                structFieldRef := parse_struct_field_ref(enumerator, expression.Children[expression.Children.length - 1], currentFunction);
                expression.Children[expression.Children.length - 1] = structFieldRef;
                continue;
            }
            op := convert_operator(token);
            if op != Operator.None {
                expression.Operators.Add(op);
                operatorRequired = false;
            }
            else {
                report_error("Unexpected token '{token.Value}' when operator was expected", enumerator.file_index, token);
                return null;
            }
        }
        else {
            ast := parse_next_expression_unit(enumerator, currentFunction, &operatorRequired);
            if ast
                expression.Children.Add(ast);
        }
    }

    return CheckExpression(enumerator, expression, operatorRequired);
}

Ast* CheckExpression(TokenEnumerator* enumerator, ExpressionAst expression, bool operatorRequired) {
    if expression.Children.length == 0 {
        report_error("Expression should contain elements", enumerator.file_index, enumerator.Current);
    }
    else if !operatorRequired && expression.Children.length > 0 {
        report_error("Value required after operator", enumerator.file_index, enumerator.Current);
        return expression;
    }

    if expression.Children.length == 1
        return expression.Children[0];

    if errors.length == 0
        set_operator_precedence(expression);

    return expression;
}

Ast* ParseCompoundExpression(TokenEnumerator* enumerator, Function* current_function, Ast* initial) {
    compoundExpression := create_ast<CompoundExpressionAst>(initial);
    compoundExpression.Children.Add(initial);
    firstToken := enumerator.Current;

    if !move_next(enumerator) {
        report_error("Expected compound expression to contain multiple values", enumerator.file_index, firstToken);
        return compoundExpression;
    }

    while enumerator.Remaining {
        token := enumerator.Current;

        switch token.type {
            case TokenType.SemiColon;
                break;
            case TokenType.Equals; {
                if compoundExpression.Children.length == 1
                    report_error("Expected compound expression to contain multiple values", enumerator.file_index, firstToken);

                _: bool;
                return ParseAssignment(enumerator, currentFunction, &_, compoundExpression);
            }
            case TokenType.Colon; {
                compoundDeclaration := create_ast<CompoundDeclarationAst>(compoundExpression);
                // compoundDeclaration.Variables = new VariableAst[compoundExpression.Children.Count];

                // Copy the initial expression to variables
                each variable, i in compoundExpression.Children {
                    if variable.ast_type != AstType.Identifier {
                        report_error("Declaration should contain a variable", variable);
                    }
                    else {
                        variableAst := create_ast<VariableAst>(identifier);
                        variableAst.Name = identifier.Name;
                        compoundDeclaration.Variables[i] = variableAst;
                    }
                }

                if !move_next(enumerator) {
                    report_error("Expected declaration to contain type and/or value", enumerator.file_index, enumerator.Last);
                    return null;
                }

                if enumerator.current.type == TokenType.Identifier {
                    compoundDeclaration.TypeDefinition = parse_type(enumerator);
                    move_next(enumerator);
                }

                if !enumerator.Remaining {
                    return compoundDeclaration;
                }
                switch enumerator.Current.Type {
                    case TokenType.Equals;
                        parse_value(compoundDeclaration, enumerator, currentFunction);
                    case TokenType.SemiColon;
                        if compoundDeclaration.TypeDefinition == null
                            report_error("Expected token declaration to have type and/or value", enumerator.file_index, enumerator.current);
                    default; {
                        report_error("Unexpected token '{enumerator.Current.Value}' in declaration", enumerator.file_index, enumerator.current);
                        // Parse until there is an equals sign or semicolon
                        while move_next(enumerator) {
                            if enumerator.current.type == TokenType.SemiColon break;
                            if enumerator.current.type == TokenType.Equals {
                                parse_value(compoundDeclaration, enumerator, currentFunction);
                                break;
                            }
                        }
                    }
                }

                return compoundDeclaration;
            }
            default; {
                compoundExpression.Children.Add(ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.Colon, TokenType.Equals));
                if enumerator.current.type == TokenType.Comma
                    move_next(enumerator);
            }
        }
    }

    if compoundExpression.Children.length == 1
        report_error("Expected compound expression to contain multiple values", enumerator.file_index, firstToken);

    return compoundExpression;
}

Ast* parse_struct_field_ref(TokenEnumerator* enumerator, Ast* initialAst, Function* current_function) {
    // 1. Initialize and move over the dot operator
    structFieldRef = create_ast<StructFieldRefAst>(initialAst);
    structFieldRef.Children.Add(initialAst);

    // 2. Parse expression units until the operator is not '.'
    operatorRequired := false;
    while move_next(enumerator) {
        if operatorRequired {
            if enumerator.current.type != TokenType.Period {
                enumerator.Move(-1);
                break;
            }
            operatorRequired = false;
        }
        else {
            ast := parse_next_expression_unit(enumerator, currentFunction, &operatorRequired);
            if ast
                structFieldRef.Children.Add(ast);
        }
    }

    return structFieldRef;
}

Ast* parse_next_expression_unit(TokenEnumerator* enumerator, Function* current_function, bool* operatorRequired) {
    token := enumerator.current;
    *operatorRequired = true;
    switch token.Type {
        case TokenType.Number;
        case TokenType.Boolean;
        case TokenType.Literal;
        case TokenType.Character;
            // Parse constant
            return parse_constant(token, enumerator.file_index);
        case TokenType.Null;
            return create_ast<NullAst>(token, enumerator.file_index);
        case TokenType.Identifier; {
            // Parse variable, call, or expression
            nextToken: Token;
            if !peek(enumerator, &nextToken) {
                report_error("Expected token to follow '{token.Value}'", enumerator.file_index, token);
                return null;
            }
            switch nextToken.type {
                case TokenType.OpenParen;
                    return parse_call(enumerator, currentFunction);
                case TokenType.OpenBracket;
                    return parse_index(enumerator, currentFunction);
                case TokenType.Asterisk;
                    if peek(enumerator, &nextToken, 1) {
                        switch nextToken.Type {
                            case TokenType.Comma;
                            case TokenType.CloseParen; {
                                type_definition: TypeDefinition*;
                                if try_parse_type(enumerator, *type_definition) {
                                    each generic, i in currentFunction.Generics {
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
                            callAst := create_ast<CallAst>(typeDefinition);
                            callAst.Name = typeDefinition.Name;
                            callAst.Generics = typeDefinition.Generics;

                            each generic in callAst.Generics {
                                each function_generic, i in currentFunction.Generics {
                                    search_for_generic(function_generic, i, generic);
                                }
                            }

                            move_next(enumerator);
                            parse_arguments(callAst, enumerator, currentFunction);
                            return callAst;
                        }
                        else {
                            each generic, i in currentFunction.Generics {
                                search_for_generic(generic, i, type_definition);
                            }
                            return typeDefinition;
                        }
                    }
                }
            }
            identifier := create_ast<IdentifierAst>(token, enumerator.file_index);
            identifier.Name = token.Value;
            return identifier;
        }
        case TokenType.Increment;
        case TokenType.Decrement; {
            positive := token.Type == TokenType.Increment;
            if move_next(enumerator) {
                changeByOneAst := create_ast<ChangeByOneAst>(enumerator);
                changeByOneAst.Prefix = true;
                changeByOneAst.Positive = positive;
                changeByOneAst.Value = parse_next_expression_unit(enumerator, currentFunction, operatorRequired);
                if peek(enumerator, &token) && token.Type == TokenType.Period {
                    move_next(enumerator);
                    changeByOneAst.Value = parse_struct_field_ref(enumerator, changeByOneAst.Value, currentFunction);
                }
                return changeByOneAst;
            }

            report_error("Expected token to follow '{token.Value}'", enumerator.file_index, token);
            return null;
        }
        case TokenType.OpenParen; {
            // Parse subexpression
            if move_next(enumerator)
                return ParseExpression(enumerator, currentFunction, null, TokenType.CloseParen);

            report_error("Expected token to follow '{token.Value}'", enumerator.file_index, token);
            return null;
        }
        case TokenType.Not;
        case TokenType.Minus;
        case TokenType.Asterisk;
        case TokenType.Ampersand; {
            if move_next(enumerator) {
                unaryAst := create_ast<UnaryAst>(token, enumerator.file_index);
                unaryAst.Operator = cast(UnaryOperator, token.Value[0]);
                unaryAst.Value = parse_next_expression_unit(enumerator, currentFunction, operatorRequired);
                if peek(enumerator, &token) && token.Type == TokenType.Period {
                    move_next(enumerator);
                    unaryAst.Value = parse_struct_field_ref(enumerator, unaryAst.Value, currentFunction);
                }
                return unaryAst;
            }

            report_error("Expected token to follow '{token.Value}'", enumerator.file_index, token);
            return null;
        }
        case TokenType.Cast;
            return parse_cast(enumerator, currentFunction);
        default; {
            report_error("Unexpected token '{token.Value}' in expression", enumerator.file_index, token);
            *operatorRequired = false;
        }
   }
   return null;
}

set_operator_precedence(ExpressionAst expression) {
    // 1. Set the initial operator precedence
    operatorPrecedence := get_operator_precedence(expression.Operators[0]);
    i := 1;
    while i < expression.Operators.length {
        // 2. Get the next operator
        precedence := get_operator_precedence(expression.Operators[i]);

        // 3. Create subexpressions to enforce operator precedence if necessary
        if precedence > operatorPrecedence {
            end: int;
            subExpression := create_sub_expression(expression, precedence, i, &end);
            expression.Children[i] = subExpression;
            expression.Children.RemoveRange(i + 1, end - i);
            expression.Operators.RemoveRange(i, end - i);

            if (i >= expression.Operators.Count) return;
            operatorPrecedence = get_operator_precedence(expression.Operators[--i]);
        }
        else
            operatorPrecedence = precedence;

        i++;
    }
}

ExpressionAst* create_sub_expression(ExpressionAst expression, int parentPrecedence, int i, int* end) {
    subExpression := create_ast<ExpressionAst>(expression.Children[i]);

    subExpression.Children.Add(expression.Children[i]);
    subExpression.Operators.Add(expression.Operators[i]);
    ++i;
    while i < expression.Operators.length
    {
        // 1. Get the next operator
        precedence := get_operator_precedence(expression.Operators[i]);

        // 2. Create subexpressions to enforce operator precedence if necessary
        if precedence > parentPrecedence {
            subExpression.Children.Add(create_sub_expression(expression, precedence, i, &i));
            if i == expression.Operators.length {
                *end = i;
                return subExpression;
            }

            subExpression.Operators.Add(expression.Operators[i]);
        }
        else if precedence < parentPrecedence {
            subExpression.Children.Add(expression.Children[i]);
            *end = i;
            return subExpression;
        }
        else {
            subExpression.Children.Add(expression.Children[i]);
            subExpression.Operators.Add(expression.Operators[i]);
        }

        i++;
    }

    subExpression.Children.Add(expression.Children[expression.Children.length - 1]);
    end = i;
    return subExpression;
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
    callAst := create_ast<CallAst>(enumerator);
    callAst.Name = enumerator.Current.Value;

    // This enumeration is the open paren
    move_next(enumerator);
    // Enumerate over the first argument
    parse_arguments(callAst, enumerator, currentFunction);

    if !enumerator.Remaining {
        report_error("Expected to close call", enumerator.file_index, enumerator.Last);
    }
    else if requiresSemicolon {
        if enumerator.Current.Type == TokenType.SemiColon
            return callAst;

        token: Token;
        if !peek(enumerator, &token) || token.Type != TokenType.SemiColon {
            report_error("Expected ';'", enumerator.file_index, token);
        }
        else
            move_next(enumerator);
    }

    return callAst;
}

parse_arguments(CallAst callAst, TokenEnumerator* enumerator, Function* current_function) {
    nextArgumentRequired := false;
    while move_next(enumerator) {
        token := enumerator.Current;

        if enumerator.current.type == TokenType.CloseParen {
            if nextArgumentRequired
                report_error("Expected argument in call following a comma", enumerator.file_index, token);

            break;
        }

        if token.Type == TokenType.Comma {
            report_error("Expected comma before next argument", enumerator.file_index, token);
        }
        else {
            nextToken: Token;
            if token.Type == TokenType.Identifier && peek(enumerator, &nextToken) && nextToken.Type == TokenType.Equals {
                argumentName := token.Value;

                move_next(enumerator);
                move_next(enumerator);

                // callAst.SpecifiedArguments ??= new Dictionary<string, IAst>();
                argument := ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.CloseParen);
                if !callAst.SpecifiedArguments.TryAdd(argumentName, argument)
                    report_error("Specified argument '{token.Value}' is already in the call", enumerator.file_index, token);
            }
            else
                callAst.Arguments.Add(ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.CloseParen));

            switch enumerator.current.type {
                case TokenType.CloseParen; break;
                case TokenType.Comma; nextArgumentRequired = true;
                case TokenType.SemiColon; {
                    report_error("Expected to close call with ')'", enumerator.file_index, enumerator.current);
                    break;
                }
            }
        }
    }
}

ReturnAst* parse_return(TokenEnumerator* enumerator, Function* current_function) {
    returnAst := create_ast<ReturnAst>(enumerator);

    if move_next(enumerator) {
        if enumerator.current.type != TokenType.SemiColon {
            returnAst.Value = ParseExpression(enumerator, currentFunction);
        }
    }
    else
        report_error("Return does not have value", enumerator.file_index, enumerator.Last);

    return returnAst;
}

IndexAst* parse_index(TokenEnumerator* enumerator, Function* current_function) {
    // 1. Initialize the index ast
    index := create_ast<IndexAst>(enumerator);
    index.Name = enumerator.current.value;
    move_next(enumerator);

    // 2. Parse index expression
    move_next(enumerator);
    index.index = ParseExpression(enumerator, currentFunction, null, TokenType.CloseBracket);

    return index;
}

CompilerDirectiveAst* parse_top_level_directive(TokenEnumerator* enumerator, bool global = false) {
    directive := create_ast<CompilerDirectiveAst>(enumerator);

    if !move_next(enumerator) {
        report_error("Expected compiler directive to have a value", enumerator.file_index, enumerator.Last);
        return null;
    }

    token := enumerator.Current;
    switch token.Value {
        case "run"; {
            directive.Type = DirectiveType.Run;
            move_next(enumerator);
            ast := ParseLine(enumerator, null);
            if ast directive.Value = ast;
        }
        case "if"; {
            directive.Type = DirectiveType.If;
            directive.Value = parse_conditional(enumerator, null, true, directory);
        }
        case "assert"; {
            directive.Type = DirectiveType.Assert;
            move_next(enumerator);
            directive.Value = ParseExpression(enumerator, null);
        }
        case "import"; {
            if !move_next(enumerator) {
                report_error("Expected module name or source file", enumerator.file_index, enumerator.Last);
                return null;
            }
            token = enumerator.Current;
            switch token.Type {
                case TokenType.Identifier; {
                    directive.Type = DirectiveType.ImportModule;
                    module := token.Value;
                    if global
                        AddModule(module, enumerator.file_index, token);
                    else
                        directive.Import = { Name = module; Path = Path.Combine(_libraryDirectory, "{token.Value}.ol"); }
                }
                case TokenType.Literal; {
                    directive.Type = DirectiveType.ImportFile;
                    file := token.Value;
                    if global
                        AddFile(file, directory, enumerator.file_index, token);
                    else
                        directive.Import = { Name = file; Path = Path.Combine(directory, token.Value); }
                }
                default;
                    report_error("Expected module name or source file, but got '{token.Value}'", enumerator.file_index, token);
            }
        }
        case "library"; {
            directive.Type = DirectiveType.Library;
            if (!move_next(enumerator) || enumerator.Current.Type != TokenType.Identifier)
            {
                report_error("Expected library name, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
                return null;
            }
            name := enumerator.current.value;
            if !move_next(enumerator) || enumerator.current.type != TokenType.Literal
            {
                report_error("Expected library path, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
                return null;
            }
            path := enumerator.current.value;
            directive.Library = { Name = name; Path = path; }
            if path[0] == '/'
                directive.Library.AbsolutePath = path;
            else
                directive.Library.AbsolutePath = Path.Combine(directory, path);
        }
        case "system_library"; {
            directive.Type = DirectiveType.SystemLibrary;
            if !move_next(enumerator) || enumerator.Current.Type != TokenType.Identifier {
                report_error("Expected library name, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
                return null;
            }
            name := enumerator.Current.Value;
            if !move_next(enumerator) || enumerator.Current.Type != TokenType.Literal {
                report_error("Expected library file name, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
                return null;
            }
            fileName := enumerator.Current.Value;
            directive.Library = { Name = name; FileName = fileName; }
            if peek(enumerator, &token) && token.Type == TokenType.Literal {
                directive.Library.LibPath = token.Value;
                move_next(enumerator);
            }
        }
        case "private"; {
            if (enumerator.Private)
                report_error("Tried to set #private when already in private scope", enumerator.file_index, token);
            else
                TypeChecker.PrivateScopes[enumerator.file_index] = TODO; //new PrivateScope{Parent = TypeChecker.GlobalScope};
            enumerator.Private = true;
        }
        default; {
            report_error("Unsupported top-level compiler directive '{token.Value}'", enumerator.file_index, token);
            return null;
        }
    }

    return directive;
}

Ast* parse_compiler_directive(TokenEnumerator* enumerator, Function* current_function) {
    directive := create_ast<CompilerDirectiveAst>(enumerator);

    if !move_next(enumerator) {
        report_error("Expected compiler directive to have a value", enumerator.file_index, enumerator.Last);
        return null;
    }

    token := enumerator.current;
    switch token.Value {
        case "if"; {
            directive.Type = DirectiveType.If;
            directive.Value = parse_conditional(enumerator, currentFunction);
            currentFunction.Flags |= FunctionFlags.HasDirectives;
        }
        case "assert"; {
            directive.Type = DirectiveType.Assert;
            move_next(enumerator);
            directive.Value = ParseExpression(enumerator, currentFunction);
            currentFunction.Flags |= FunctionFlags.HasDirectives;
        }
        case "inline"; {
            if !move_next(enumerator) || enumerator.Current.Type != TokenType.Identifier {
                report_error("Expected funciton call following #inline directive", enumerator.file_index, token);
                return null;
            }
            call := parse_call(enumerator, currentFunction, true);
            if call call.Inline = true;
            return call;
        }
        default; {
            report_error("Unsupported function level compiler directive '{token.Value}'", enumerator.file_index, token);
            return null;
        }
    }

    return directive;
}

AssemblyAst* parse_inline_assembly(TokenEnumerator* enumerator) {
    assembly := create_ast<AssemblyAst>(enumerator);

    // First move over the opening '{'
    if !move_next(enumerator) {
        report_error("Expected an opening '{' at asm block", enumerator.file_index, enumerator.Last);
        return null;
    }

    if enumerator.current.type != TokenType.OpenBrace {
        report_error("Expected an opening '{' at asm block", enumerator.file_index, enumerator.Current);
        return null;
    }

    closed := false;
    parsingInRegisters := true;
    parsingOutRegisters := false;
    while move_next(enumerator) {
        token := enumerator.Current;

        switch token.Type {
            case TokenType.CloseBrace; {
                closed = true;
                break;
            }
            case TokenType.In; {
                if parse_in_register(assembly, enumerator) {
                    if parsingOutRegisters || !parsingInRegisters {
                        report_error("In instructions should be declared before body instructions", enumerator.file_index, token);
                    }
                }
                else {
                    // Skip through the next ';' or '}'
                    while enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && move_next(enumerator) {}

                    if enumerator.current.type == TokenType.CloseBrace
                        return assembly;
                }
            }
            case TokenType.Out; {
                if parse_out_value(assembly, enumerator) {
                    if parsingInRegisters {
                        parsingInRegisters = false;
                        parsingOutRegisters = true;
                        report_error("Expected instructions before out registers", enumerator.file_index, token);
                    }
                    else if !parsingOutRegisters
                        parsingOutRegisters = true;
                }
                else {
                    // Skip through the next ';' or '}'
                    while enumerator.current.type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && move_next(enumerator) {}

                    if enumerator.Current.Type == TokenType.CloseBrace
                        return assembly;
                }
            }
            case TokenType.Identifier; {
                instruction := parse_assembly_instruction(enumerator);
                if instruction {
                    if parsingInRegisters parsingInRegisters = false;
                    else if parsingOutRegisters
                        report_error("Expected body instructions before out values", instruction);

                    assembly.Instructions.Add(instruction);
                }
                else {
                    // Skip through the next ';' or '}'
                    while enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && move_next(enumerator) {}

                    if enumerator.Current.Type == TokenType.CloseBrace
                        return assembly;
                }
            }
            default; {
                report_error("Expected instruction in assembly block, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);

                // Skip through the next ';' or '}'
                while (enumerator.Current.Type != TokenType.SemiColon || enumerator.Current.Type != TokenType.CloseBrace) && move_next(enumerator) {}

                if enumerator.Current.Type == TokenType.CloseBrace
                    return assembly;
            }
        }
    }

    if !closed
        report_error("Assembly block not closed by '}'", enumerator.file_index, enumerator.Current);

    return assembly;
}

bool parse_in_register(AssemblyAst* assembly, TokenEnumerator* enumerator) {
    if !move_next(enumerator) {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if enumerator.Current.Type != TokenType.Identifier {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    register := enumerator.Current.Value;
    if assembly.InRegisters.ContainsKey(register) {
        report_error("Duplicate in register '{register}'", enumerator.file_index, enumerator.Current);
        return false;
    }
    input := create_ast<AssemblyInputAst>(enumerator);
    input.Register = register;

    if !move_next(enumerator) {
        report_error("Expected comma or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if enumerator.Current.Type != TokenType.Comma {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    if !move_next(enumerator) {
        report_error("Expected value in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    _: bool;
    input.Ast = parse_next_expression_unit(enumerator, null, &_);

    if !move_next(enumerator) {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if enumerator.Current.Type != TokenType.SemiColon {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    assembly.InRegisters[register] = input;
    return true;
}

bool parse_out_value(AssemblyAst* assembly, TokenEnumerator* enumerator) {
    if !move_next(enumerator) {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    output := create_ast<AssemblyInputAst>(enumerator);

    _: bool;
    output.Ast = parse_next_expression_unit(enumerator, null, &_);
    if output.Ast == null {
        return false;
    }

    if !move_next(enumerator) {
        report_error("Expected comma or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if enumerator.Current.Type != TokenType.Comma {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    if !move_next(enumerator) {
        report_error("Expected value in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if enumerator.Current.Type != TokenType.Identifier {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }
    output.Register = enumerator.Current.Value;

    if !move_next(enumerator) {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if enumerator.Current.Type != TokenType.SemiColon {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    assembly.OutValues.Add(output);
    return true;
}

AssemblyInstructionAst* parse_assembly_instruction(TokenEnumerator* enumerator) {
    instruction := create_ast<AssemblyInstructionAst>(enumerator);
    instruction.Instruction = enumerator.Current.Value;

    if !move_next(enumerator) {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return null;
    }

    switch enumerator.Current.Type {
        case TokenType.Identifier; {
            instruction.Value1 = create_ast<AssemblyValueAst>(enumerator);
            instruction.Value1.Register = enumerator.Current.Value;
        }
        case TokenType.Number; {
            instruction.Value1 = create_ast<AssemblyValueAst>(enumerator);
            instruction.Value1.Constant = parse_constant(enumerator.Current, enumerator.file_index);
        }
        case TokenType.OpenBracket; {
            instruction.Value1 = create_ast<AssemblyValueAst>(enumerator);
            if !parse_assembly_pointer(instruction.Value1, enumerator)
                return null;
        }
        case TokenType.SemiColon; return instruction;
        default; {
            report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
            return null;
        }
    }

    if !move_next(enumerator) {
        report_error("Expected comma or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return null;
    }

    switch enumerator.Current.Type {
        case TokenType.Comma; {}
        case TokenType.SemiColon; return instruction;
        default; {
            report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
            return null;
        }
    }

    if !move_next(enumerator) {
        report_error("Expected value in instruction", enumerator.file_index, enumerator.Last);
        return null;
    }

    instruction.Value2 = create_ast<AssemblyValueAst>(enumerator);
    switch enumerator.Current.Type {
        case TokenType.Identifier;
            instruction.Value2.Register = enumerator.Current.Value;
        case TokenType.Number;
            instruction.Value2.Constant = parse_constant(enumerator.Current, enumerator.file_index);
        case TokenType.OpenBracket;
            if !parse_assembly_pointer(instruction.Value2, enumerator)
                return null;
        default; {
            report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
            return null;
        }
    }

    if !move_next(enumerator) {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.Last);
        return null;
    }

    if enumerator.Current.Type != TokenType.SemiColon {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return null;
    }

    return instruction;
}

bool parse_assembly_pointer(AssemblyValueAst* value, TokenEnumerator* enumerator) {
    if !move_next(enumerator) || enumerator.Current.Type != TokenType.Identifier {
        report_error("Expected register after pointer in instruction", enumerator.file_index, enumerator.Current);
        return false;
    }
    value.Dereference = true;
    value.Register = enumerator.Current.Value;

    if !move_next(enumerator) || enumerator.Current.Type != TokenType.CloseBracket {
        report_error("Expected to close pointer to register with ']'", enumerator.file_index, enumerator.Current);
        return false;
    }

    return true;
}

SwitchAst* parse_switch(TokenEnumerator* enumerator, Function* current_function) {
    switchAst := create_ast<SwitchAst>(enumerator);
    if !move_next(enumerator) {
        report_error("Expected value for switch statement", enumerator.file_index, enumerator.Last);
        return null;
    }

    switchAst.Value = ParseExpression(enumerator, currentFunction, null, TokenType.OpenBrace);

    if enumerator.Current.Type != TokenType.OpenBrace {
        report_error("Expected switch statement value to be followed by '{' and cases", enumerator.file_index, enumerator.Current);
        return null;
    }

    closed := false;
    currentCases: SwitchCases;
    while move_next(enumerator) {
        switch enumerator.current.type {
            case TokenType.CloseBrace; {
                closed = true;
                if currentCases.length {
                    report_error("Switch statement contains case(s) without bodies starting", currentCases[0]);
                    return null;
                }
                break;
            }
            case TokenType.Case; {
                if !move_next(enumerator) {
                    report_error("Expected value after case", enumerator.file_index, enumerator.Last);
                    return null;
                }

                switchCase := ParseExpression(enumerator, currentFunction);
                if currentCases.length
                    currentCases.Add(switchCase);
                else
                    // TODO This is wrong
                    currentCases[0] = switchCase;
            }
            case TokenType.Default; {
                if !move_next(enumerator) || enumerator.Current.Type != TokenType.SemiColon {
                    report_error("Expected ';' after default", enumerator.file_index, enumerator.Current);
                    return null;
                }
                if !move_next(enumerator) {
                    report_error("Expected body after default", enumerator.file_index, enumerator.Last);
                    return null;
                }

                if enumerator.Current.Type == TokenType.OpenBrace
                    switchAst.DefaultCase = parse_scope(enumerator, currentFunction);
                else {
                    switchAst.DefaultCase = create_ast<ScopeAst>(enumerator);
                    switchAst.DefaultCase.Children.Add(ParseLine(enumerator, currentFunction));
                }
            }
            default; {
                if currentCases.length {
                    // Parse through the case body and add to the list
                    if enumerator.Current.Type == TokenType.OpenBrace
                        currentCases.body = parse_scope(enumerator, currentFunction);
                    else {
                        currentCases.body = create_ast<ScopeAst>(enumerator);
                        currentCases.body.Children.Add(ParseLine(enumerator, currentFunction));
                    }

                    switchAst.Cases.Add(currentCases);
                }
                else {
                    report_error("Switch statement contains case body prior to any cases", enumerator.file_index, enumerator.current);
                    // Skip through the next ';' or '}'
                    while enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && move_next(enumerator) {}
                }
            }
        }
    }

    if !closed {
        report_error("Expected switch statement to be closed by '}'", enumerator.file_index, enumerator.Current);
        return null;
    }
    else if switchAst.Cases.length == 0 {
        report_error("Expected switch to have 1 or more non-default cases", switchAst);
        return null;
    }

    return switchAst;
}

OperatorOverloadAst parse_operator_overload(TokenEnumerator* enumerator)
{
    overload := create_ast<OperatorOverloadAst>(enumerator);
    if !move_next(enumerator) {
        report_error("Expected an operator be specified to overload", enumerator.file_index, enumerator.Last);
        return null;
    }

    // 1. Determine the operator
    token: Token;
    if enumerator.Current.Type == TokenType.OpenBracket && peek(enumerator, &token) && token.Type == TokenType.CloseBracket {
        overload.Operator = Operator.Subscript;
        move_next(enumerator);
    }
    else {
        overload.Operator = convert_operator(enumerator.Current);
        if overload.Operator == Operator.None
            report_error("Expected an operator to be be specified, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
    }

    if !move_next(enumerator) {
        report_error("Expected to get the type to overload the operator", enumerator.file_index, enumerator.Last);
        return null;
    }

    // 2. Determine generics if necessary
    if enumerator.Current.Type == TokenType.LessThan {
        commaRequiredBeforeNextType := false;
        // var generics = new HashSet<string>();
        while move_next(enumerator) {
            token = enumerator.Current;
            if token.Type == TokenType.GreaterThan {
                if !commaRequiredBeforeNextType
                    report_error("Expected comma in generics", enumerator.file_index, token);

                break;
            }

            if !commaRequiredBeforeNextType {
                if token.type == TokenType.Identifier {
                    if (!generics.Add(token.Value))
                        report_error("Duplicate generic '{token.Value}'", enumerator.file_index, token);
                }
                else
                    report_error("Unexpected token '{token.Value}' in generics", enumerator.file_index, token);

                commaRequiredBeforeNextType = true;
            }
            else {
                if token.type != TokenType.Comma {
                    report_error("Unexpected token '{token.Value}' when defining generics", enumerator.file_index, token);
                }

                commaRequiredBeforeNextType = false;
            }
        }

        if generics.length == 0
            report_error("Expected operator overload to contain generics", enumerator.file_index, enumerator.Current);

        move_next(enumerator);
        overload.Generics.AddRange(generics);
    }

    // 3. Find open paren to start parsing arguments
    if enumerator.Current.Type != TokenType.OpenParen {
        // Add an error to the function AST and continue until open paren
        token = enumerator.Current;
        report_error("Unexpected token '{token.Value}' in operator overload definition", enumerator.file_index, token);
        while move_next(enumerator) && enumerator.Current.Type != TokenType.OpenParen {}
    }

    // 4. Get the arguments for the operator overload
    commaRequiredBeforeNextArgument := false;
    currentArgument: DeclarationAst*;
    while move_next(enumerator) {
        token = enumerator.Current;


        switch token.Type {
            case TokenType.CloseParen; {
                if commaRequiredBeforeNextArgument {
                    overload.Arguments.Add(currentArgument);
                    currentArgument = null;
                }
                break;
            }
            case TokenType.Identifier; {
                if commaRequiredBeforeNextArgument {
                    report_error("Comma required after declaring an argument", enumerator.file_index, token);
                }
                else if currentArgument == null {
                    currentArgument = create_ast<DeclarationAst>(token, enumerator.file_index);
                    currentArgument.TypeDefinition = parse_type(enumerator, argument = true);
                    each generic, i in overload.Generics {
                        if search_for_generic(generic, i, currentArgument.TypeDefinition) {
                            currentArgument.HasGenerics = true;
                        }
                    }
                    if overload.Arguments.length == 0
                        overload.Type = currentArgument.TypeDefinition;
                }
                else {
                    currentArgument.Name = token.Value;
                    commaRequiredBeforeNextArgument = true;
                }
            }
            case TokenType.Comma; {
                if commaRequiredBeforeNextArgument
                    overload.Arguments.Add(currentArgument);
                else
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);

                currentArgument = null;
                commaRequiredBeforeNextArgument = false;
            }
            default;
                report_error("Unexpected token '{token.Value}' in arguments", enumerator.file_index, token);
        }

        if enumerator.Current.Type == TokenType.CloseParen
            break;
    }

    if currentArgument
        report_error("Incomplete argument in overload for type '{overload.Type.Name}'", enumerator.file_index, enumerator.Current);

    if !commaRequiredBeforeNextArgument && overload.Arguments.length
        report_error("Unexpected comma in arguments", enumerator.file_index, enumerator.Current);

    // 5. Set the return type based on the operator
    move_next(enumerator);
    switch overload.Operator {
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
            if enumerator.Current.Type != TokenType.Colon {
                report_error("Unexpected to define return type for subscript", enumerator.file_index, enumerator.Current);
            }
            else if move_next(enumerator) {
                overload.return_type_definition = parse_type(enumerator);
                each generic, i in overload.Generics {
                    if search_for_generic(generic, i, overload.return_type_definition) {
                        overload.Flags |= FunctionFlags.ReturnTypeHasGenerics;
                    }
                }
                move_next(enumerator);
            }
        default; {
            overload.return_type_definition = overload.Type;
            if overload.Generics.length {
                overload.Flags |= FunctionFlags.ReturnTypeHasGenerics;
            }
            each generic, i in overload.Generics {
                search_for_generic(generic, i, overload.return_type_definition);
            }
        }
    }

    // 6. Handle compiler directives
    if enumerator.Current.Type == TokenType.Pound {
        if !move_next(enumerator) {
            report_error("Expected compiler directive value", enumerator.file_index, enumerator.Last);
            return null;
        }
        switch enumerator.Current.Value {
            case "print_ir";
                overload.Flags |= FunctionFlags.PrintIR;
            default;
                report_error("Unexpected compiler directive '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
        }
        move_next(enumerator);
    }

    // 7. Find open brace to start parsing body
    if enumerator.Current.Type != TokenType.OpenBrace {
        // Add an error and continue until open paren
        token = enumerator.Current;
        report_error("Unexpected token '{token.Value}' in operator overload definition", enumerator.file_index, token);
        while move_next(enumerator) && enumerator.Current.Type != TokenType.OpenBrace {}
    }

    // 8. Parse body
    overload.Body = parse_scope(enumerator, overload);

    return overload;
}

InterfaceAst* parse_interface(TokenEnumerator* enumerator) {
    interfaceAst := create_ast<InterfaceAst>(enumerator);
    interfaceAst.Private = enumerator.Private;
    move_next(enumerator);

    // 1a. Check if the return type is void
    token: Token;
    if !peek(enumerator, &token) {
        report_error("Expected interface definition", enumerator.file_index, token);
        return null;
    }
    if token.Type != TokenType.OpenParen {
        interfaceAst.return_type_definition = parse_type(enumerator);
        move_next(enumerator);
    }

    // 1b. Handle multiple return values
    if enumerator.Current.Type == TokenType.Comma {
        returnType := create_ast<TypeDefinition>(interfaceAst.return_type_definition);
        returnType.Compound = true;
        returnType.Generics.Add(interfaceAst.return_type_definition);
        interfaceAst.return_type_definition = returnType;

        while enumerator.Current.Type == TokenType.Comma {
            if !move_next(enumerator)
                break;

            returnType.Generics.Add(parse_type(enumerator));
            move_next(enumerator);
        }
    }

    // 1b. Set the name of the interface or get the name from the type
    if !enumerator.Remaining {
        report_error("Expected the interface name to be declared", enumerator.file_index, enumerator.Last);
        return null;
    }
    switch enumerator.Current.Type {
        case TokenType.Identifier; {
            interfaceAst.Name = enumerator.Current.Value;
            move_next(enumerator);
        }
        case TokenType.OpenParen;
            if interfaceAst.return_type_definition.Name == "*" || interfaceAst.return_type_definition.Count != null {
                report_error("Expected the interface name to be declared", interfaceAst.return_type_definition);
            }
            else {
                interfaceAst.Name = interfaceAst.return_type_definition.Name;
                if interfaceAst.return_type_definition.Generics.length
                    report_error("Interface '{interfaceAst.Name}' cannot have generics", interfaceAst.return_type_definition);

                interfaceAst.return_type_definition = null;
            }
        default; {
            report_error("Expected the interface name to be declared", enumerator.file_index, enumerator.Current);
            move_next(enumerator);
        }
    }

    // 2. Parse arguments until a close paren
    commaRequiredBeforeNextArgument := false;
    currentArgument: DeclarationAst*;
    while move_next(enumerator) {
        token = enumerator.Current;

        switch token.type {
            case TokenType.CloseParen; {
                if commaRequiredBeforeNextArgument {
                    interfaceAst.Arguments.Add(currentArgument);
                    currentArgument = null;
                }
                break;
            }
            case TokenType.Identifier;
                if commaRequiredBeforeNextArgument {
                    report_error("Comma required after declaring an argument", enumerator.file_index, token);
                }
                else if currentArgument == null {
                    currentArgument = create_ast<DeclarationAst>(token, enumerator.file_index);
                    currentArgument.TypeDefinition = parse_type(enumerator);
                }
                else {
                    currentArgument.Name = token.Value;
                    commaRequiredBeforeNextArgument = true;
                }
            case TokenType.Comma; {
                if commaRequiredBeforeNextArgument
                    interfaceAst.Arguments.Add(currentArgument);
                else
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);

                currentArgument = null;
                commaRequiredBeforeNextArgument = false;
            }
            case TokenType.Equals;
                if commaRequiredBeforeNextArgument
                {
                    report_error("Interface '{interfaceAst.Name}' cannot have default argument values", enumerator.file_index, token);
                    while move_next(enumerator) {
                        if enumerator.Current.Type == TokenType.Comma {
                            commaRequiredBeforeNextArgument = false;
                            interfaceAst.Arguments.Add(currentArgument);
                            currentArgument = null;
                            break;
                        }
                        else if enumerator.Current.Type == TokenType.Comma {
                            interfaceAst.Arguments.Add(currentArgument);
                            currentArgument = null;
                            break;
                        }
                    }
                }
                else
                    report_error("Unexpected token '=' in arguments", enumerator.file_index, token);
            default;
                report_error("Unexpected token '{token.Value}' in arguments", enumerator.file_index, token);
        }

        if enumerator.Current.Type == TokenType.CloseParen
            break;
    }

    if currentArgument
        report_error("Incomplete argument in interface '{interfaceAst.Name}'", enumerator.file_index, enumerator.Current);

    if !commaRequiredBeforeNextArgument && interfaceAst.Arguments.length
        report_error("Unexpected comma in arguments", enumerator.file_index, enumerator.Current);

    return interfaceAst;
}

TypeDefinition* parse_type(TokenEnumerator* enumerator, Function* current_function = null, bool argument = false, int depth = 0) {
    typeDefinition := create_ast<TypeDefinition>(enumerator);
    typeDefinition.Name = enumerator.Current.Value;

    // Alias int to s32
    if typeDefinition.Name == "int"
        typeDefinition.Name = "s32";

    if enumerator.Current.Type == TokenType.VarArgs {
        if !argument
            report_error("Variable args type can only be used as an argument type", enumerator.file_index, enumerator.Current);

        return typeDefinition;
    }

    // Determine whether to parse a generic type, otherwise return
    token: Token;
    if peek(enumerator, &token) && token.Type == TokenType.LessThan {
        // Clear the '<' before entering loop
        move_next(enumerator);
        commaRequiredBeforeNextType := false;
        while move_next(enumerator) {
            token = enumerator.Current;

            if token.Type == TokenType.GreaterThan {
                if !commaRequiredBeforeNextType && typeDefinition.Generics.length
                    report_error("Unexpected comma in type", enumerator.file_index, token);

                break;
            }
            else if token.Type == TokenType.ShiftRight {
                // Split the token and insert a greater than after the current token
                token.Value = ">";
                newToken: Token = { Type = TokenType.GreaterThan; Value = ">"; Line = token.Line; Column = token.Column + 1; }
                enumerator.Insert(newToken);
                break;
            }
            else if token.Type == TokenType.RotateRight {
                // Split the token and insert a shift right after the current token
                token.Value = ">";
                newToken: Token = { Type = TokenType.ShiftRight; Value = ">>"; Line = token.Line; Column = token.Column + 1; }
                enumerator.Insert(newToken);
                break;
            }

            if !commaRequiredBeforeNextType {
                if token.type == TokenType.Identifier
                    typeDefinition.Generics.Add(parse_type(enumerator, currentFunction, depth = depth + 1));
                else
                    report_error("Unexpected token '{token.Value}' in type definition", enumerator.file_index, token);

                commaRequiredBeforeNextType = true;
            }
            else {
                if token.type != TokenType.Comma
                    report_error("Unexpected token '{token.Value}' in type definition", enumerator.file_index, token);

                commaRequiredBeforeNextType = false;
            }
        }

        if typeDefinition.Generics.length == 0
            report_error("Expected type to contain generics", enumerator.file_index, enumerator.Current);
    }

    while peek(enumerator, &token) && token.Type == TokenType.Asterisk {
        move_next(enumerator);
        pointerType := create_ast<TypeDefinition>(enumerator);
        pointerType.Name = "*";
        pointerType.Generics.Add(typeDefinition);
        typeDefinition = pointerType;
    }

    if peek(enumerator, &token) && token.Type == TokenType.OpenBracket {
        // Skip over the open bracket and parse the expression
        move_next(enumerator);
        move_next(enumerator);
        typeDefinition.Count = ParseExpression(enumerator, currentFunction, null, TokenType.CloseBracket);
    }

    return typeDefinition;
}

bool try_parse_type(TokenEnumerator* enumerator, TypeDefinition** typeDef) {
    steps := 0;
    _: bool;
    success, typeDefinition := try_parse_type(enumerator.Current, enumerator, &steps, typeDef, &_, &_);
    if success {
        move(enumerator, steps);
        *typeDef = typeDefinition;
        return true;
    }
    return false;
}

bool, TypeDefinition* try_parse_type(Token name, TokenEnumerator* enumerator, int* steps, bool* endsWithShift, bool* endsWithRotate, int depth = 0) {
    typeDefinition := create_ast<TypeDefinition>(name, enumerator.file_index);
    typeDefinition.Name = name.Value;
    *endsWithShift = false;
    *endsWithRotate = false;

    // Alias int to s32
    if typeDefinition.Name == "int"
        typeDefinition.Name = "s32";

    // Determine whether to parse a generic type, otherwise return
    token: Token;
    if peek(enumerator, &token, steps) && token.Type == TokenType.LessThan {
        // Clear the '<' before entering loop
        steps++;
        commaRequiredBeforeNextType := false;
        while peek(enumerator, &token, steps) {
            steps++;
            if token.Type == TokenType.GreaterThan {
                if !commaRequiredBeforeNextType && typeDefinition.Generics.length > 0
                    return false, null;

                break;
            }
            else if token.Type == TokenType.ShiftRight {
                if (depth % 3 != 1 && !endsWithShift) || (!commaRequiredBeforeNextType && typeDefinition.Generics.length > 0)
                    return false, null;

                *endsWithShift = true;
                break;
            }
            else if (token.Type == TokenType.RotateRight)
            {
                if (depth % 3 != 2 && !endsWithRotate) || (!commaRequiredBeforeNextType && typeDefinition.Generics.length > 0)
                    return false, null;

                *endsWithRotate = true;
                break;
            }

            if !commaRequiredBeforeNextType {
                if token.type == TokenType.Identifier {
                    success, genericType := try_parse_type(token, enumerator, steps, endsWithShift, endsWithRotate, depth + 1);

                    if !success return false, null;
                    if endsWithShift || endsWithRotate
                        steps--;

                    typeDefinition.Generics.Add(genericType);
                    commaRequiredBeforeNextType = true;
                }
                else
                    return false, null;
            }
            else
            {
                if token.type != TokenType.Comma
                    return false, null;

                commaRequiredBeforeNextType = false;
            }
        }

        if typeDefinition.Generics.length == 0
            return false, null;
    }

    while peek(enumerator, &token, steps) && token.Type == TokenType.Asterisk {
        pointerType := create_ast<TypeDefinition>(token, enumerator.file_index);
        pointerType.Name = "*";
        pointerType.Generics.Add(typeDefinition);
        typeDefinition = pointerType;
        steps++;
        endsWithShift = false;
        endsWithRotate = false;
    }

    return true, typeDefinition;
}

ConstantAst* parse_constant(Token token, int fileIndex) {
    constant := create_ast<ConstantAst>(token, fileIndex);
    switch token.Type {
        case TokenType.Literal; {
            constant.TypeName = "string";
            constant.String = token.Value;
        }
        case TokenType.Character; {
            constant.TypeName = "u8";
            constant.Value.UnsignedInteger = token.Value[0];
        }
        case TokenType.Number; {
            if token.Flags == TokenFlags.None {
                if int.TryParse(token.Value, &constant.value.integer) {
                    constant.TypeName = "s32";
                }
                else if long.TryParse(token.Value, &constant.value.integer) {
                    constant.TypeName = "s64";
                }
                else if (ulong.TryParse(token.Value, &constant.value.unsigned_integer)) {
                    constant.TypeName = "u64";
                }
                else {
                    report_error("Invalid integer '{token.Value}', must be 64 bits or less", fileIndex, token);
                    return null;
                }
            }
            else if token.Flags & TokenFlags.Float {
                if float.TryParse(token.Value, &constant.value.double) {
                    constant.TypeName = "float";
                }
                else if double.TryParse(token.Value, &constant.value.double) {
                    constant.TypeName = "float64";
                }
                else {
                    report_error("Invalid floating point number '{token.Value}', must be single or double precision", fileIndex, token);
                    return null;
                }
            }
            else if token.Flags & TokenFlags.HexNumber {
                if token.value.length == 2 {
                    report_error("Invalid number '{token.Value}'", fileIndex, token);
                    return null;
                }

                value := substring(token.Value, 2, token.value.length - 2);
                if value.length <= 8 {
                    if uint.TryParse(value, NumberStyles.HexNumber, &constant.value.unsigned_integer) {
                        constant.TypeName = "u32";
                    }
                }
                else if value.Length <= 16 {
                    if ulong.TryParse(value, NumberStyles.HexNumber, &constant.value.unsigned_integer) {
                        constant.TypeName = "u64";
                    }
                }
                else {
                    report_error("Invalid integer '{token.Value}'", fileIndex, token);
                    return null;
                }
            }
            else {
                report_error("Unable to determine type of token '{token.Value}'", fileIndex, token);
                return null;
            }
        }
        case TokenType.Boolean; {
            constant.TypeName = "bool";
            constant.Value.boolean = token.value == "true";
        }
        default; {
            report_error("Unable to determine type of token '{token.Value}'", fileIndex, token);
            return null;
        }
    }
    return constant;
}

CastAst* parse_cast(TokenEnumerator* enumerator, Function* current_function) {
    castAst := create_ast<CastAst>(enumerator);

    // 1. Try to get the open paren to begin the cast
    if !move_next(enumerator) || enumerator.Current.Type != TokenType.OpenParen {
        report_error("Expected '(' after 'cast'", enumerator.file_index, enumerator.Current);
        return null;
    }

    // 2. Get the target type
    if !move_next(enumerator) {
        report_error("Expected to get the target type for the cast", enumerator.file_index, enumerator.Last);
        return null;
    }
    castAst.TargetTypeDefinition = parse_type(enumerator, currentFunction);
    if currentFunction {
        each generic, i in currentFunction.Generics {
            if search_for_generic(generic, i, castAst.TargetTypeDefinition) {
                castAst.HasGenerics = true;
            }
        }
    }

    // 3. Expect to get a comma
    if !move_next(enumerator) || enumerator.Current.Type != TokenType.Comma {
        report_error("Expected ',' after type in cast", enumerator.file_index, enumerator.Current);
        return null;
    }

    // 4. Get the value expression
    if !move_next(enumerator) {
        report_error("Expected to get the value for the cast", enumerator.file_index, enumerator.Last);
        return null;
    }
    castAst.Value = ParseExpression(enumerator, currentFunction, null, TokenType.CloseParen);

    return castAst;
}

T* create_ast<T>(Ast* source, AstType type = AstType.None) {
    ast := new<T>();
    ast.ast_type = type;

    if source {
        ast.file_index = source.file_index;
        ast.line = source.Line;
        ast.column = source.Column;
    }

    return ast;
}

T* create_ast<T>(TokenEnumerator* enumerator, AstType type = AstType.None) {
    return create_ast<T>(enumerator.current, enumerator.file_index, type);
}

T create_ast<T>(Token token, int fileIndex, AstType type = AstType.None) {
    ast := new<T>();
    ast.type = type;
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
            return cast(Operator, token.type);
    }

    return Operator.None;
}
