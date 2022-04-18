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

Ast* ParseTopLevelAst(TokenEnumerator* enumerator) {
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
                return ParseFunction(enumerator, attributes);
            }
            report_error("Unexpected token '%'", enumerator.file_index, token, token.value);
            return null;
        }
        case TokenType.Struct;
            return ParseStruct(enumerator, attributes);
        case TokenType.Enum;
            return ParseEnum(enumerator, attributes);
        case TokenType.Union; {
            if attributes.length
                report_error("Compiler directives cannot have attributes", enumerator.file_index, token);

            return ParseUnion(enumerator);
        }
        case TokenType.Pound; {
            if attributes.length
                report_error("Compiler directives cannot have attributes", enumerator.file_index, token);

            return ParseTopLevelDirective(enumerator, directory);
        }
        case TokenType.Operator; {
            if (attributes != null)
                report_error("Operator overloads cannot have attributes", enumerator.file_index, token);

            return ParseOperatorOverload(enumerator);
        }
        case TokenType.Interface; {
            if attributes.length
                report_error("Interfaces cannot have attributes", enumerator.file_index, token);

            return ParseInterface(enumerator);
        }
        default;
            report_error("Unexpected token '%'", enumerator.file_index, token, token.value);
    }

    return null;
}

FunctionAst* ParseFunction(TokenEnumerator* enumerator, Array<string> attributes) {
    // 1. Determine return type and name of the function
    function := CreateAst<FunctionAst>(enumerator);
    function.Attributes = attributes;
    function.Private = enumerator.Private;

    // 1a. Check if the return type is void
    token: Token;
    if !peek(enumerator, &token) {
        report_error("Expected function definition", enumerator.file_index, token);
        return null;
    }

    if token.type != TokenType.OpenParen {
        function.ReturnTypeDefinition = ParseType(enumerator);
        move_next(enumerator);
    }

    // 1b. Handle multiple return values
    if enumerator.current.type == TokenType.Comma
    {
        returnType := CreateAst<TypeDefinition>(function.ReturnTypeDefinition);
        returnType.Compound = true;
        returnType.Generics.Add(function.ReturnTypeDefinition);
        function.ReturnTypeDefinition = returnType;

        while enumerator.Current.Type == TokenType.Comma {
            if !move_next(enumerator)
                break;

            returnType.Generics.Add(ParseType(enumerator));
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
            if (function.ReturnTypeDefinition.Name == "*" || function.ReturnTypeDefinition.Count != null)
            {
                report_error("Expected the function name to be declared", function.ReturnTypeDefinition);
            }
            else
            {
                function.Name = function.ReturnTypeDefinition.Name;
                each generic in function.ReturnTypeDefinition.Generics {
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
                function.ReturnTypeDefinition = null;
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
    if (function.ReturnTypeDefinition != null) {
        each generic, i in function.Generics {
            if SearchForGeneric(generic, i, function.ReturnTypeDefinition) {
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
                    currentArgument = CreateAst<DeclarationAst>(token, enumerator.file_index);
                    currentArgument.TypeDefinition = ParseType(enumerator, argument = true);
                    each generic, i in function.Generics {
                        if SearchForGeneric(generic, i, currentArgument.TypeDefinition) {
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
    function.Body = ParseScope(enumerator, function);

    return function;
}

StructAst* ParseStruct(TokenEnumerator* enumerator, Array<string> attributes) {
    structAst := CreateAst<StructAst>(enumerator);
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
        structAst.BaseTypeDefinition = ParseType(enumerator);
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

        structAst.Fields.Add(ParseStructField(enumerator));
    }

    // 6. Mark field types as generic if necessary
    if structAst.generics.length {
        each generic, i in structAst.generics {
            each field in structAst.fields {
                if field.TypeDefinition != null && SearchForGeneric(generic, i, field.TypeDefinition) {
                    field.HasGenerics = true;
                }
            }
        }
    }

    return structAst;
}

StructFieldAst* ParseStructField(TokenEnumerator* enumerator) {
    attributes := ParseAttributes(enumerator);
    structField := CreateAst<StructFieldAst>(enumerator);
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
        structField.TypeDefinition = ParseType(enumerator, null);
    }

    // 3. Get the value or return
    if !move_next(enumerator) {
        report_error("Expected declaration to have value", enumerator.file_index, enumerator.Last);
        return null;
    }

    token = enumerator.Current;
    switch token.Type {
        case TokenType.Equals;
            ParseValue(structField, enumerator, null);
        case TokenType.SemiColon;
            if (structField.TypeDefinition == null)
                report_error("Expected struct field to have value", enumerator.file_index, token);
        default; {
            report_error("Unexpected token '{token.Value}' in struct field", enumerator.file_index, token);
            // Parse until there is an equals sign or semicolon
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.SemiColon break;
                if enumerator.current.type == TokenType.Equals {
                    ParseValue(structField, enumerator, null);
                    break;
                }
            }
        }
    }

    return structField;
}

EnumAst* ParseEnum(TokenEnumerator* enumerator, Array<string> attributes) {
    enumAst := CreateAst<EnumAst>(enumerator);
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
        enumAst.BaseTypeDefinition = ParseType(enumerator);
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
                    currentValue = CreateAst<EnumValueAst>(token, enumerator.file_index);
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

UnionAst* ParseUnion(TokenEnumerator* enumerator) {
    union_ast := CreateAst<UnionAst>(enumerator);
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

        union_ast.Fields.Add(ParseUnionField(enumerator));
    }

    return union_ast;
}

UnionFieldAst* ParseUnionField(TokenEnumerator* enumerator) {
    field := CreateAst<UnionFieldAst>(enumerator);

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
        field.TypeDefinition = ParseType(enumerator, null);
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

bool SearchForGeneric(string generic, int index, TypeDefinition* type) {
    if (type.Name == generic) {
        type.IsGeneric = true;
        type.GenericIndex = index;
        return true;
    }

    hasGeneric := false;
    each typeGeneric in type.Generics {
        if (SearchForGeneric(generic, index, typeGeneric)) {
            hasGeneric = true;
        }
    }
    return hasGeneric;
}

Ast* ParseLine(TokenEnumerator* enumerator, IFunction currentFunction) {
    if !enumerator.remaining {
        report_error("End of file reached without closing scope", enumerator.file_index, enumerator.Last);
        return null;
    }

    token := enumerator.Current;
    switch token.Type {
        case TokenType.Return;
            return ParseReturn(enumerator, currentFunction);
        case TokenType.If;
            return ParseConditional(enumerator, currentFunction);
        case TokenType.While;
            return ParseWhile(enumerator, currentFunction);
        case TokenType.Each;
            return ParseEach(enumerator, currentFunction);
        case TokenType.Identifier; {
            if (!peek(enumerator, &token))
            {
                report_error("End of file reached without closing scope", enumerator.file_index, enumerator.Last);
                return null;
            }
            switch token.Type {
                case TokenType.OpenParen;
                    return ParseCall(enumerator, currentFunction, true);
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
            return ParseScope(enumerator, currentFunction);
        case TokenType.Pound;
            return ParseCompilerDirective(enumerator, currentFunction);
        case TokenType.Asm;
            return ParseInlineAssembly(enumerator);
        case TokenType.Switch;
            return ParseSwitch(enumerator, currentFunction);
        case TokenType.Break; {
            breakAst := CreateAst<Ast>(token, enumerator.file_index);
            if move_next(enumerator) {
                if enumerator.current.type != TokenType.SemiColon {
                    report_error("Expected ';'", enumerator.file_index, enumerator.current);
                }
            }
            else report_error("End of file reached without closing scope", enumerator.file_index, enumerator.Last);

            return breakAst;
        }
        case TokenType.Continue; {
            continueAst := CreateAst<Ast>(token, enumerator.file_index);
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

ScopeAst* ParseScope(TokenEnumerator* enumerator, IFunction currentFunction, bool topLevel = false) {
    scopeAst := CreateAst<ScopeAst>(enumerator);

    closed := false;
    while move_next(enumerator) {
        if enumerator.current.type == TokenType.CloseBrace {
            closed = true;
            break;
        }

        if topLevel scopeAst.Children.Add(ParseTopLevelAst(enumerator, directory));
        else scopeAst.Children.Add(ParseLine(enumerator, currentFunction));
    }

    if !closed
        report_error("Scope not closed by '}'", enumerator.file_index, enumerator.Current);

    return scopeAst;
}

ConditionalAst* ParseConditional(TokenEnumerator* enumerator, IFunction currentFunction, bool topLevel = false) {
    conditionalAst := CreateAst<ConditionalAst>(enumerator);

    // 1. Parse the conditional expression by first iterating over the initial 'if'
    move_next(enumerator);
    conditionalAst.Condition = ParseConditionExpression(enumerator, currentFunction);

    if !enumerator.Remaining {
        report_error("Expected if to contain conditional expression and body", enumerator.file_index, enumerator.Last);
        return null;
    }

    // 2. Determine how many lines to parse
    if enumerator.Current.Type == TokenType.OpenBrace {
        // Parse until close brace
        conditionalAst.IfBlock = ParseScope(enumerator, currentFunction, topLevel, directory);
    }
    else {
        // Parse single AST
        conditionalAst.IfBlock = CreateAst<ScopeAst>(enumerator);
        if topLevel conditionalAst.IfBlock.Children.Add(ParseTopLevelAst(enumerator));
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
            conditionalAst.ElseBlock = ParseScope(enumerator, currentFunction, topLevel, directory);
        }
        else {
            // Parse single AST
            conditionalAst.ElseBlock = CreateAst<ScopeAst>(enumerator);
            if topLevel conditionalAst.ElseBlock.Children.Add(ParseTopLevelAst(enumerator));
            else conditionalAst.ElseBlock.Children.Add(ParseLine(enumerator, currentFunction));
        }
    }

    return conditionalAst;
}

WhileAst* ParseWhile(TokenEnumerator* enumerator, IFunction currentFunction) {
    whileAst := CreateAst<WhileAst>(enumerator);

    // 1. Parse the conditional expression by first iterating over the initial 'while'
    move_next(enumerator);
    whileAst.Condition = ParseConditionExpression(enumerator, currentFunction);

    // 2. Determine how many lines to parse
    if !enumerator.Remaining {
        report_error("Expected while loop to contain body", enumerator.file_index, enumerator.last);
        return null;
    }

    if enumerator.Current.Type == TokenType.OpenBrace {
        // Parse until close brace
        whileAst.Body = ParseScope(enumerator, currentFunction);
    }
    else {
        // Parse single AST
        whileAst.Body = CreateAst<ScopeAst>(enumerator);
        whileAst.Body.Children.Add(ParseLine(enumerator, currentFunction));
    }

    return whileAst;
}

EachAst* ParseEach(TokenEnumerator* enumerator, IFunction currentFunction) {
    eachAst := CreateAst<EachAst>(enumerator);

    // 1. Parse the iteration variable by first iterating over the initial 'each'
    move_next(enumerator);
    if enumerator.Current.Type == TokenType.Identifier {
        eachAst.IterationVariable = CreateAst<VariableAst>(enumerator);
        eachAst.IterationVariable.Name = enumerator.Current.Value;
    }
    else
        report_error("Expected variable in each block definition", enumerator.file_index, enumerator.Current);

    move_next(enumerator);
    if enumerator.Current.Type == TokenType.Comma {
        move_next(enumerator);
        if enumerator.Current.Type == TokenType.Identifier {
            eachAst.IndexVariable = CreateAst<VariableAst>(enumerator);
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
    expression := ParseConditionExpression(enumerator, currentFunction);

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

        eachAst.RangeEnd = ParseConditionExpression(enumerator, currentFunction);
        if !enumerator.Remaining {
            report_error("Expected each block to have iteration and body", enumerator.file_index, enumerator.Last);
            return eachAst;
        }
    }
    else
        eachAst.Iteration = expression;

    // 4. Determine how many lines to parse
    if enumerator.Current.Type == TokenType.OpenBrace
        eachAst.Body = ParseScope(enumerator, currentFunction);
    else {
        eachAst.Body = CreateAst<ScopeAst>(enumerator);
        eachAst.Body.Children.Add(ParseLine(enumerator, currentFunction));
    }

    return eachAst;
}

Ast* ParseConditionExpression(TokenEnumerator* enumerator, IFunction currentFunction) {
    expression := CreateAst<ExpressionAst>(enumerator);
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
                changeByOneAst := CreateAst<ChangeByOneAst>(source);
                changeByOneAst.Positive = token.Type == TokenType.Increment;
                changeByOneAst.Value = source;
                expression.Children[expression.Children.length - 1] = changeByOneAst;
                continue;
            }
            if token.Type == TokenType.Number && token.Value[0] == '-' {
                token.Value = substring(token.value, 1, token.value.length - 1);
                expression.Operators.Add(Operator.Subtract);
                constant := ParseConstant(token, enumerator.file_index);
                expression.Children.Add(constant);
                continue;
            }
            if token.type == TokenType.Period {
                structFieldRef := ParseStructFieldRef(enumerator, expression.Children[expression.Children.length - 1], currentFunction);
                expression.Children[expression.Children.length - 1] = structFieldRef;
                continue;
            }
            op := ConvertOperator(token);
            if op != Operator.None {
                expression.Operators.Add(op);
                operatorRequired = false;
            }
            else
                break;
        }
        else {
            ast := ParseNextExpressionUnit(enumerator, currentFunction, &operatorRequired);
            if ast expression.Children.Add(ast);
        }
    }

    return CheckExpression(enumerator, expression, operatorRequired);
}

DeclarationAst* parse_declaration(TokenEnumerator* enumerator, IFunction currentFunction = null, bool global = false) {
    declaration := CreateAst<DeclarationAst>(enumerator);
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
        declaration.TypeDefinition = ParseType(enumerator, currentFunction);
        if currentFunction {
            each generic, i in currentFunction.Generics {
                if SearchForGeneric(generic, i, declaration.TypeDefinition) {
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
            ParseValue(declaration, enumerator, currentFunction);
        case TokenType.SemiColon;
            if (declaration.TypeDefinition == null)
                report_error("Expected declaration to have value", enumerator.file_index, token);
        default; {
            report_error("Unexpected token '{token.Value}' in declaration", enumerator.file_index, token);
            // Parse until there is an equals sign or semicolon
            while move_next(enumerator) {
                if enumerator.current.type == TokenType.SemiColon break;
                if enumerator.current.type == TokenType.Equals {
                    ParseValue(declaration, enumerator, currentFunction);
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

bool ParseValue(Values* values, TokenEnumerator* enumerator, IFunction currentFunction) {
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

AssignmentAst* ParseAssignment(TokenEnumerator* enumerator, IFunction currentFunction, bool* moveNext, IAst reference = null) {
    // 1. Set the variable
    assignment: AssignmentAst*;
    if reference assignment = CreateAst<AssignmentAst>(reference);
    else assignment = CreateAst<AssignmentAst>(enumerator);

    assignment.Reference = reference;
    *moveNext = false;

    // 2. When the original reference is null, set the l-value to an identifier
    if reference == null {
        variableAst := CreateAst<IdentifierAst>(enumerator);
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
            op := ConvertOperator(token);
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
            *moveNext = ParseValue(assignment, enumerator, currentFunction);
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
                    moveNext = ParseValue(assignment, enumerator, currentFunction);
                    break;
                }
            }
        }
    }

    return assignment;
}

bool is_end_token(TokenType token, Array<TokenType> end_tokens) {

}

Ast* ParseExpression(TokenEnumerator* enumerator, IFunction currentFunction, ExpressionAst* expression = null, Params<TokenType> end_tokens) {
    operatorRequired: bool;

    if expression operatorRequired = true;
    else expression = CreateAst<ExpressionAst>(enumerator);

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
                changeByOneAst := CreateAst<ChangeByOneAst>(source);
                changeByOneAst.Positive = token.Type == TokenType.Increment;
                changeByOneAst.Value = source;
                expression.Children[expression.Children.length - 1] = changeByOneAst;
                continue;
            }
            if token.type == TokenType.Number && token.value[0] == '-'
            {
                token.value = substring(token.value, 1, token.value.length - 1);
                expression.Operators.Add(Operator.Subtract);
                constant := ParseConstant(token, enumerator.file_index);
                expression.Children.Add(constant);
                continue;
            }
            if token.Type == TokenType.Period {
                structFieldRef := ParseStructFieldRef(enumerator, expression.Children[expression.Children.length - 1], currentFunction);
                expression.Children[expression.Children.length - 1] = structFieldRef;
                continue;
            }
            op := ConvertOperator(token);
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
            ast := ParseNextExpressionUnit(enumerator, currentFunction, &operatorRequired);
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
        SetOperatorPrecedence(expression);

    return expression;
}

Ast* ParseCompoundExpression(TokenEnumerator* enumerator, IFunction currentFunction, Ast* initial) {
    compoundExpression := CreateAst<CompoundExpressionAst>(initial);
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
                compoundDeclaration := CreateAst<CompoundDeclarationAst>(compoundExpression);
                // compoundDeclaration.Variables = new VariableAst[compoundExpression.Children.Count];

                // Copy the initial expression to variables
                each variable, i in compoundExpression.Children {
                    if variable.ast_type != AstType.Identifier {
                        report_error("Declaration should contain a variable", variable);
                    }
                    else {
                        variableAst := CreateAst<VariableAst>(identifier);
                        variableAst.Name = identifier.Name;
                        compoundDeclaration.Variables[i] = variableAst;
                    }
                }

                if !move_next(enumerator) {
                    report_error("Expected declaration to contain type and/or value", enumerator.file_index, enumerator.Last);
                    return null;
                }

                if enumerator.current.type == TokenType.Identifier {
                    compoundDeclaration.TypeDefinition = ParseType(enumerator);
                    move_next(enumerator);
                }

                if !enumerator.Remaining {
                    return compoundDeclaration;
                }
                switch enumerator.Current.Type {
                    case TokenType.Equals;
                        ParseValue(compoundDeclaration, enumerator, currentFunction);
                    case TokenType.SemiColon;
                        if compoundDeclaration.TypeDefinition == null
                            report_error("Expected token declaration to have type and/or value", enumerator.file_index, enumerator.current);
                    default; {
                        report_error("Unexpected token '{enumerator.Current.Value}' in declaration", enumerator.file_index, enumerator.current);
                        // Parse until there is an equals sign or semicolon
                        while move_next(enumerator) {
                            if enumerator.current.type == TokenType.SemiColon break;
                            if enumerator.current.type == TokenType.Equals {
                                ParseValue(compoundDeclaration, enumerator, currentFunction);
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

Ast* ParseStructFieldRef(TokenEnumerator* enumerator, Ast* initialAst, IFunction currentFunction) {
    // 1. Initialize and move over the dot operator
    structFieldRef = CreateAst<StructFieldRefAst>(initialAst);
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
            ast := ParseNextExpressionUnit(enumerator, currentFunction, &operatorRequired);
            if ast
                structFieldRef.Children.Add(ast);
        }
    }

    return structFieldRef;
}

Ast* ParseNextExpressionUnit(TokenEnumerator* enumerator, IFunction currentFunction, bool* operatorRequired) {
    token := enumerator.current;
    *operatorRequired = true;
    switch token.Type {
        case TokenType.Number;
        case TokenType.Boolean;
        case TokenType.Literal;
        case TokenType.Character;
            // Parse constant
            return ParseConstant(token, enumerator.file_index);
        case TokenType.Null;
            return CreateAst<NullAst>(token, enumerator.file_index);
        case TokenType.Identifier; {
            // Parse variable, call, or expression
            nextToken: Token;
            if !peek(enumerator, &nextToken) {
                report_error("Expected token to follow '{token.Value}'", enumerator.file_index, token);
                return null;
            }
            switch nextToken.type {
                case TokenType.OpenParen;
                    return ParseCall(enumerator, currentFunction);
                case TokenType.OpenBracket;
                    return ParseIndex(enumerator, currentFunction);
                case TokenType.Asterisk;
                    if peek(enumerator, &nextToken, 1) {
                        switch nextToken.Type {
                            case TokenType.Comma;
                            case TokenType.CloseParen; {
                                type_definition: TypeDefinition*;
                                if TryParseType(enumerator, *type_definition) {
                                    each generic, i in currentFunction.Generics {
                                        SearchForGeneric(generic, i, type_definition);
                                    }
                                    return type_definition;
                                }
                            }
                        }
                    }
                case TokenType.LessThan; {
                    type_definition: TypeDefinition*;
                    if TryParseType(enumerator, &type_definition) {
                        if enumerator.current.type == TokenType.OpenParen {
                            callAst := CreateAst<CallAst>(typeDefinition);
                            callAst.Name = typeDefinition.Name;
                            callAst.Generics = typeDefinition.Generics;

                            each generic in callAst.Generics {
                                each function_generic, i in currentFunction.Generics {
                                    SearchForGeneric(function_generic, i, generic);
                                }
                            }

                            move_next(enumerator);
                            ParseArguments(callAst, enumerator, currentFunction);
                            return callAst;
                        }
                        else {
                            each generic, i in currentFunction.Generics {
                                SearchForGeneric(generic, i, type_definition);
                            }
                            return typeDefinition;
                        }
                    }
                }
            }
            identifier := CreateAst<IdentifierAst>(token, enumerator.file_index);
            identifier.Name = token.Value;
            return identifier;
        }
        case TokenType.Increment;
        case TokenType.Decrement; {
            positive := token.Type == TokenType.Increment;
            if move_next(enumerator) {
                changeByOneAst := CreateAst<ChangeByOneAst>(enumerator);
                changeByOneAst.Prefix = true;
                changeByOneAst.Positive = positive;
                changeByOneAst.Value = ParseNextExpressionUnit(enumerator, currentFunction, operatorRequired);
                if peek(enumerator, &token) && token.Type == TokenType.Period {
                    move_next(enumerator);
                    changeByOneAst.Value = ParseStructFieldRef(enumerator, changeByOneAst.Value, currentFunction);
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
                unaryAst := CreateAst<UnaryAst>(token, enumerator.file_index);
                unaryAst.Operator = cast(UnaryOperator, token.Value[0]);
                unaryAst.Value = ParseNextExpressionUnit(enumerator, currentFunction, operatorRequired);
                if peek(enumerator, &token) && token.Type == TokenType.Period {
                    move_next(enumerator);
                    unaryAst.Value = ParseStructFieldRef(enumerator, unaryAst.Value, currentFunction);
                }
                return unaryAst;
            }

            report_error("Expected token to follow '{token.Value}'", enumerator.file_index, token);
            return null;
        }
        case TokenType.Cast;
            return ParseCast(enumerator, currentFunction);
        default; {
            report_error("Unexpected token '{token.Value}' in expression", enumerator.file_index, token);
            *operatorRequired = false;
        }
   }
   return null;
}

SetOperatorPrecedence(ExpressionAst expression) {
    // 1. Set the initial operator precedence
    operatorPrecedence := GetOperatorPrecedence(expression.Operators[0]);
    i := 1;
    while i < expression.Operators.length {
        // 2. Get the next operator
        precedence := GetOperatorPrecedence(expression.Operators[i]);

        // 3. Create subexpressions to enforce operator precedence if necessary
        if precedence > operatorPrecedence {
            end: int;
            subExpression := CreateSubExpression(expression, precedence, i, &end);
            expression.Children[i] = subExpression;
            expression.Children.RemoveRange(i + 1, end - i);
            expression.Operators.RemoveRange(i, end - i);

            if (i >= expression.Operators.Count) return;
            operatorPrecedence = GetOperatorPrecedence(expression.Operators[--i]);
        }
        else
            operatorPrecedence = precedence;

        i++;
    }
}

ExpressionAst* CreateSubExpression(ExpressionAst expression, int parentPrecedence, int i, int* end) {
    subExpression := CreateAst<ExpressionAst>(expression.Children[i]);

    subExpression.Children.Add(expression.Children[i]);
    subExpression.Operators.Add(expression.Operators[i]);
    ++i;
    while i < expression.Operators.length
    {
        // 1. Get the next operator
        precedence := GetOperatorPrecedence(expression.Operators[i]);

        // 2. Create subexpressions to enforce operator precedence if necessary
        if precedence > parentPrecedence {
            subExpression.Children.Add(CreateSubExpression(expression, precedence, i, &i));
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

int GetOperatorPrecedence(Operator op) {
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

CallAst* ParseCall(TokenEnumerator* enumerator, IFunction currentFunction, bool requiresSemicolon = false) {
    callAst := CreateAst<CallAst>(enumerator);
    callAst.Name = enumerator.Current.Value;

    // This enumeration is the open paren
    move_next(enumerator);
    // Enumerate over the first argument
    ParseArguments(callAst, enumerator, currentFunction);

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

// TODO Fix the rest
ParseArguments(CallAst callAst, TokenEnumerator* enumerator, IFunction currentFunction) {
    var nextArgumentRequired = false;
    while (move_next(enumerator))
    {
        var token = enumerator.Current;

        if (enumerator.Current.Type == TokenType.CloseParen)
        {
            if (nextArgumentRequired)
            {
                report_error("Expected argument in call following a comma", enumerator.file_index, token);
            }
            break;
        }

        if (token.Type == TokenType.Comma)
        {
            report_error("Expected comma before next argument", enumerator.file_index, token);
        }
        else
        {
            if (token.Type == TokenType.Identifier && peek(enumerator, &var nextToken) && nextToken.Type == TokenType.Equals)
            {
                var argumentName = token.Value;

                move_next(enumerator);
                move_next(enumerator);

                callAst.SpecifiedArguments ??= new Dictionary<string, IAst>();
                var argument = ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.CloseParen);
                if (!callAst.SpecifiedArguments.TryAdd(argumentName, argument))
                {
                    report_error("Specified argument '{token.Value}' is already in the call", enumerator.file_index, token);
                }
            }
            else
            {
                callAst.Arguments.Add(ParseExpression(enumerator, currentFunction, null, TokenType.Comma, TokenType.CloseParen));
            }

            var currentType = enumerator.Current.Type;
            if (currentType == TokenType.CloseParen) break;
            if (currentType == TokenType.Comma)
            {
                nextArgumentRequired = true;
            }
            if (currentType == TokenType.SemiColon)
            {
                report_error("Expected to close call with ')'", enumerator.file_index, enumerator.Current);
                break;
            }
        }
    }
}

ReturnAst ParseReturn(TokenEnumerator* enumerator, IFunction currentFunction)
{
    var returnAst = CreateAst<ReturnAst>(enumerator);

    if (move_next(enumerator))
    {
        if (enumerator.Current.Type != TokenType.SemiColon)
        {
            returnAst.Value = ParseExpression(enumerator, currentFunction);
        }
    }
    else
    {
        report_error("Return does not have value", enumerator.file_index, enumerator.Last);
    }

    return returnAst;
}

IndexAst ParseIndex(TokenEnumerator* enumerator, IFunction currentFunction)
{
    // 1. Initialize the index ast
    var index = CreateAst<IndexAst>(enumerator);
    index.Name = enumerator.Current.Value;
    move_next(enumerator);

    // 2. Parse index expression
    move_next(enumerator);
    index.Index = ParseExpression(enumerator, currentFunction, null, TokenType.CloseBracket);

    return index;
}

CompilerDirectiveAst ParseTopLevelDirective(TokenEnumerator* enumerator, string directory, bool global = false)
{
    var directive = CreateAst<CompilerDirectiveAst>(enumerator);

    if (!move_next(enumerator))
    {
        report_error("Expected compiler directive to have a value", enumerator.file_index, enumerator.Last);
        return null;
    }

    var token = enumerator.Current;
    switch (token.Value)
    {
        case "run":
            directive.Type = DirectiveType.Run;
            move_next(enumerator);
            var ast = ParseLine(enumerator, null);
            if (ast != null)
                directive.Value = ast;
            break;
        case "if":
            directive.Type = DirectiveType.If;
            directive.Value = ParseConditional(enumerator, null, true, directory);
            break;
        case "assert":
            directive.Type = DirectiveType.Assert;
            move_next(enumerator);
            directive.Value = ParseExpression(enumerator, null);
            break;
        case "import":
            if (!move_next(enumerator))
            {
                report_error("Expected module name or source file", enumerator.file_index, enumerator.Last);
                return null;
            }
            token = enumerator.Current;
            switch (token.Type)
            {
                case TokenType.Identifier:
                    directive.Type = DirectiveType.ImportModule;
                    var module = token.Value;
                    if (global)
                    {
                        AddModule(module, enumerator.file_index, token);
                    }
                    else
                    {
                        directive.Import = new Import {Name = module, Path = Path.Combine(_libraryDirectory, $"{token.Value}.ol")};
                    }
                    break;
                case TokenType.Literal:
                    directive.Type = DirectiveType.ImportFile;
                    var file = token.Value;
                    if (global)
                    {
                        AddFile(file, directory, enumerator.file_index, token);
                    }
                    else
                    {
                        directive.Import = new Import {Name = file, Path = Path.Combine(directory, token.Value)};
                    }
                    break;
                default:
                    report_error("Expected module name or source file, but got '{token.Value}'", enumerator.file_index, token);
                    break;
            }
            break;
        case "library":
            directive.Type = DirectiveType.Library;
            if (!move_next(enumerator) || enumerator.Current.Type != TokenType.Identifier)
            {
                report_error("Expected library name, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
                return null;
            }
            var name = enumerator.Current.Value;
            if (!move_next(enumerator) || enumerator.Current.Type != TokenType.Literal)
            {
                report_error("Expected library path, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
                return null;
            }
            var path = enumerator.Current.Value;
            directive.Library = new Library {Name = name, Path = path};
            if (path[0] == '/')
            {
                directive.Library.AbsolutePath = path;
            }
            else
            {
                directive.Library.AbsolutePath = Path.Combine(directory, path);
            }
            break;
        case "system_library":
            directive.Type = DirectiveType.SystemLibrary;
            if (!move_next(enumerator) || enumerator.Current.Type != TokenType.Identifier)
            {
                report_error("Expected library name, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
                return null;
            }
            name = enumerator.Current.Value;
            if (!move_next(enumerator) || enumerator.Current.Type != TokenType.Literal)
            {
                report_error("Expected library file name, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
                return null;
            }
            var fileName = enumerator.Current.Value;
            directive.Library = new Library {Name = name, FileName = fileName};
            if (peek(enumerator, &token) && token.Type == TokenType.Literal)
            {
                directive.Library.LibPath = token.Value;
                move_next(enumerator);
            }
            break;
        case "private":
            if (enumerator.Private)
            {
                report_error("Tried to set #private when already in private scope", enumerator.file_index, token);
            }
            else
            {
                TypeChecker.PrivateScopes[enumerator.file_index] = new PrivateScope{Parent = TypeChecker.GlobalScope};
            }
            enumerator.Private = true;
            break;
        default:
            report_error("Unsupported top-level compiler directive '{token.Value}'", enumerator.file_index, token);
            return null;
    }

    return directive;
}

IAst ParseCompilerDirective(TokenEnumerator* enumerator, IFunction currentFunction)
{
    var directive = CreateAst<CompilerDirectiveAst>(enumerator);

    if (!move_next(enumerator))
    {
        report_error("Expected compiler directive to have a value", enumerator.file_index, enumerator.Last);
        return null;
    }

    var token = enumerator.Current;
    switch (token.Value)
    {
        case "if":
            directive.Type = DirectiveType.If;
            directive.Value = ParseConditional(enumerator, currentFunction);
            currentFunction.Flags |= FunctionFlags.HasDirectives;
            break;
        case "assert":
            directive.Type = DirectiveType.Assert;
            move_next(enumerator);
            directive.Value = ParseExpression(enumerator, currentFunction);
            currentFunction.Flags |= FunctionFlags.HasDirectives;
            break;
        case "inline":
            if (!move_next(enumerator) || enumerator.Current.Type != TokenType.Identifier)
            {
                report_error("Expected funciton call following #inline directive", enumerator.file_index, token);
                return null;
            }
            var call = ParseCall(enumerator, currentFunction, true);
            if (call != null)
            {
                call.Inline = true;
            }
            return call;
        default:
            report_error("Unsupported function level compiler directive '{token.Value}'", enumerator.file_index, token);
            return null;
    }

    return directive;
}

AssemblyAst ParseInlineAssembly(TokenEnumerator* enumerator)
{
    var assembly = CreateAst<AssemblyAst>(enumerator);

    // First move over the opening '{'
    if (!move_next(enumerator))
    {
        report_error("Expected an opening '{' at asm block", enumerator.file_index, enumerator.Last);
        return null;
    }

    if (enumerator.Current.Type != TokenType.OpenBrace)
    {
        report_error("Expected an opening '{' at asm block", enumerator.file_index, enumerator.Current);
        return null;
    }

    var closed = false;
    var parsingInRegisters = true;
    var parsingOutRegisters = false;
    while (move_next(enumerator))
    {
        var token = enumerator.Current;
        if (token.Type == TokenType.CloseBrace)
        {
            closed = true;
            break;
        }

        switch (token.Type)
        {
            case TokenType.In:
                if (ParseInRegister(assembly, enumerator))
                {
                    if (parsingOutRegisters || !parsingInRegisters)
                    {
                        report_error("In instructions should be declared before body instructions", enumerator.file_index, token);
                    }
                }
                else
                {
                    // Skip through the next ';' or '}'
                    while (enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && move_next(enumerator));

                    if (enumerator.Current.Type == TokenType.CloseBrace)
                    {
                        return assembly;
                    }
                }
                break;
            case TokenType.Out:
                if (ParseOutValue(assembly, enumerator))
                {
                    if (parsingInRegisters)
                    {
                        parsingInRegisters = false;
                        parsingOutRegisters = true;
                        report_error("Expected instructions before out registers", enumerator.file_index, token);
                    }
                    else if (!parsingOutRegisters)
                    {
                        parsingOutRegisters = true;
                    }
                }
                else
                {
                    // Skip through the next ';' or '}'
                    while (enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && move_next(enumerator));

                    if (enumerator.Current.Type == TokenType.CloseBrace)
                    {
                        return assembly;
                    }
                }
                break;
            case TokenType.Identifier:
                var instruction = ParseAssemblyInstruction(enumerator);
                if (instruction == null)
                {
                    // Skip through the next ';' or '}'
                    while (enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && move_next(enumerator));

                    if (enumerator.Current.Type == TokenType.CloseBrace)
                    {
                        return assembly;
                    }
                }
                else
                {
                    if (parsingInRegisters)
                    {
                        parsingInRegisters = false;
                    }
                    else if (parsingOutRegisters)
                    {
                        report_error("Expected body instructions before out values", instruction);
                    }
                    assembly.Instructions.Add(instruction);
                }
                break;
            default:
                report_error("Expected instruction in assembly block, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);

                // Skip through the next ';' or '}'
                while ((enumerator.Current.Type != TokenType.SemiColon || enumerator.Current.Type != TokenType.CloseBrace) && move_next(enumerator));

                if (enumerator.Current.Type == TokenType.CloseBrace)
                {
                    return assembly;
                }
                break;
        }
    }

    if (!closed)
    {
        report_error("Assembly block not closed by '}'", enumerator.file_index, enumerator.Current);
    }

    return assembly;
}

bool ParseInRegister(AssemblyAst assembly, TokenEnumerator* enumerator)
{
    if (!move_next(enumerator))
    {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if (enumerator.Current.Type != TokenType.Identifier)
    {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    var register = enumerator.Current.Value;
    if (assembly.InRegisters.ContainsKey(register))
    {
        report_error("Duplicate in register '{register}'", enumerator.file_index, enumerator.Current);
        return false;
    }
    var input = CreateAst<AssemblyInputAst>(enumerator);
    input.Register = register;

    if (!move_next(enumerator))
    {
        report_error("Expected comma or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if (enumerator.Current.Type != TokenType.Comma)
    {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    if (!move_next(enumerator))
    {
        report_error("Expected value in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    input.Ast = ParseNextExpressionUnit(enumerator, null, out _);

    if (!move_next(enumerator))
    {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if (enumerator.Current.Type != TokenType.SemiColon)
    {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    assembly.InRegisters[register] = input;
    return true;
}

bool ParseOutValue(AssemblyAst assembly, TokenEnumerator* enumerator)
{
    if (!move_next(enumerator))
    {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    var output = CreateAst<AssemblyInputAst>(enumerator);

    output.Ast = ParseNextExpressionUnit(enumerator, null, out _);
    if (output.Ast == null)
    {
        return false;
    }

    if (!move_next(enumerator))
    {
        report_error("Expected comma or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if (enumerator.Current.Type != TokenType.Comma)
    {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    if (!move_next(enumerator))
    {
        report_error("Expected value in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if (enumerator.Current.Type != TokenType.Identifier)
    {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }
    output.Register = enumerator.Current.Value;

    if (!move_next(enumerator))
    {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.Last);
        return false;
    }

    if (enumerator.Current.Type != TokenType.SemiColon)
    {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return false;
    }

    assembly.OutValues.Add(output);
    return true;
}

AssemblyInstructionAst ParseAssemblyInstruction(TokenEnumerator* enumerator)
{
    var instruction = CreateAst<AssemblyInstructionAst>(enumerator);
    instruction.Instruction = enumerator.Current.Value;

    if (!move_next(enumerator))
    {
        report_error("Expected value or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return null;
    }

    switch (enumerator.Current.Type)
    {
        case TokenType.Identifier:
            instruction.Value1 = CreateAst<AssemblyValueAst>(enumerator);
            instruction.Value1.Register = enumerator.Current.Value;
            break;
        case TokenType.Number:
            instruction.Value1 = CreateAst<AssemblyValueAst>(enumerator);
            instruction.Value1.Constant = ParseConstant(enumerator.Current, enumerator.file_index);
            break;
        case TokenType.OpenBracket:
            instruction.Value1 = CreateAst<AssemblyValueAst>(enumerator);
            if (!ParseAssemblyPointer(instruction.Value1, enumerator))
            {
                return null;
            }
            break;
        case TokenType.SemiColon:
            return instruction;
        default:
            report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
            return null;
    }

    if (!move_next(enumerator))
    {
        report_error("Expected comma or semicolon in instruction", enumerator.file_index, enumerator.Last);
        return null;
    }

    switch (enumerator.Current.Type)
    {
        case TokenType.Comma:
            break;
        case TokenType.SemiColon:
            return instruction;
        default:
            report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
            return null;
    }

    if (!move_next(enumerator))
    {
        report_error("Expected value in instruction", enumerator.file_index, enumerator.Last);
        return null;
    }

    instruction.Value2 = CreateAst<AssemblyValueAst>(enumerator);
    switch (enumerator.Current.Type)
    {
        case TokenType.Identifier:
            instruction.Value2.Register = enumerator.Current.Value;
            break;
        case TokenType.Number:
            instruction.Value2.Constant = ParseConstant(enumerator.Current, enumerator.file_index);
            break;
        case TokenType.OpenBracket:
            if (!ParseAssemblyPointer(instruction.Value2, enumerator))
            {
                return null;
            }
            break;
        default:
            report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
            return null;
    }

    if (!move_next(enumerator))
    {
        report_error("Expected semicolon in instruction", enumerator.file_index, enumerator.Last);
        return null;
    }

    if (enumerator.Current.Type != TokenType.SemiColon)
    {
        report_error("Unexpected token '{enumerator.Current.Value}' in assembly block", enumerator.file_index, enumerator.Current);
        return null;
    }

    return instruction;
}

bool ParseAssemblyPointer(AssemblyValueAst value, TokenEnumerator* enumerator)
{
    if (!move_next(enumerator) || enumerator.Current.Type != TokenType.Identifier)
    {
        report_error("Expected register after pointer in instruction", enumerator.file_index, enumerator.Current);
        return false;
    }
    value.Dereference = true;
    value.Register = enumerator.Current.Value;

    if (!move_next(enumerator) || enumerator.Current.Type != TokenType.CloseBracket)
    {
        report_error("Expected to close pointer to register with ']'", enumerator.file_index, enumerator.Current);
        return false;
    }

    return true;
}

SwitchAst ParseSwitch(TokenEnumerator* enumerator, IFunction currentFunction)
{
    var switchAst = CreateAst<SwitchAst>(enumerator);
    if (!move_next(enumerator))
    {
        report_error("Expected value for switch statement", enumerator.file_index, enumerator.Last);
        return null;
    }

    switchAst.Value = ParseExpression(enumerator, currentFunction, null, TokenType.OpenBrace);

    if (enumerator.Current.Type != TokenType.OpenBrace)
    {
        report_error("Expected switch statement value to be followed by '{' and cases", enumerator.file_index, enumerator.Current);
        return null;
    }

    var closed = false;
    List<IAst> currentCases = null;
    while (move_next(enumerator))
    {
        var token = enumerator.Current;
        if (token.Type == TokenType.CloseBrace)
        {
            closed = true;
            if (currentCases != null)
            {
                report_error("Switch statement contains case(s) without bodies starting", currentCases[0]);
                return null;
            }
            break;
        }

        if (token.Type == TokenType.Case)
        {
            if (!move_next(enumerator))
            {
                report_error("Expected value after case", enumerator.file_index, enumerator.Last);
                return null;
            }

            var switchCase = ParseExpression(enumerator, currentFunction);
            if (currentCases == null)
            {
                currentCases = new() {switchCase};
            }
            else
            {
                currentCases.Add(switchCase);
            }
        }
        else if (token.Type == TokenType.Default)
        {
            if (!move_next(enumerator) || enumerator.Current.Type != TokenType.SemiColon)
            {
                report_error("Expected ';' after default", enumerator.file_index, enumerator.Current);
                return null;
            }
            if (!move_next(enumerator))
            {
                report_error("Expected body after default", enumerator.file_index, enumerator.Last);
                return null;
            }

            if (enumerator.Current.Type == TokenType.OpenBrace)
            {
                switchAst.DefaultCase = ParseScope(enumerator, currentFunction);
            }
            else
            {
                switchAst.DefaultCase = CreateAst<ScopeAst>(enumerator);
                switchAst.DefaultCase.Children.Add(ParseLine(enumerator, currentFunction));
            }
        }
        else if (currentCases == null)
        {
            report_error("Switch statement contains case body prior to any cases", enumerator.file_index, token);
            // Skip through the next ';' or '}'
            while (enumerator.Current.Type != TokenType.SemiColon && enumerator.Current.Type != TokenType.CloseBrace && move_next(enumerator));
        }
        else
        {
            // Parse through the case body and add to the list
            ScopeAst caseScope;
            if (enumerator.Current.Type == TokenType.OpenBrace)
            {
                caseScope = ParseScope(enumerator, currentFunction);
            }
            else
            {
                caseScope = CreateAst<ScopeAst>(enumerator);
                caseScope.Children.Add(ParseLine(enumerator, currentFunction));
            }

            switchAst.Cases.Add((currentCases, caseScope));
            currentCases = null;
        }
    }

    if (!closed)
    {
        report_error("Expected switch statement to be closed by '}'", enumerator.file_index, enumerator.Current);
        return null;
    }
    else if (switchAst.Cases.Count == 0)
    {
        report_error("Expected switch to have 1 or more non-default cases", switchAst);
        return null;
    }

    return switchAst;
}

OperatorOverloadAst ParseOperatorOverload(TokenEnumerator* enumerator)
{
    var overload = CreateAst<OperatorOverloadAst>(enumerator);
    if (!move_next(enumerator))
    {
        report_error("Expected an operator be specified to overload", enumerator.file_index, enumerator.Last);
        return null;
    }

    // 1. Determine the operator
    if (enumerator.Current.Type == TokenType.OpenBracket && peek(enumerator, &var token) && token.Type == TokenType.CloseBracket)
    {
        overload.Operator = Operator.Subscript;
        move_next(enumerator);
    }
    else
    {
        overload.Operator = ConvertOperator(enumerator.Current);
        if (overload.Operator == Operator.None)
        {
            report_error("Expected an operator to be be specified, but got '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
        }
    }
    if (!move_next(enumerator))
    {
        report_error("Expected to get the type to overload the operator", enumerator.file_index, enumerator.Last);
        return null;
    }

    // 2. Determine generics if necessary
    if (enumerator.Current.Type == TokenType.LessThan)
    {
        var commaRequiredBeforeNextType = false;
        var generics = new HashSet<string>();
        while (move_next(enumerator))
        {
            token = enumerator.Current;

            if (token.Type == TokenType.GreaterThan)
            {
                if (!commaRequiredBeforeNextType)
                {
                    report_error("Expected comma in generics", enumerator.file_index, token);
                }

                break;
            }

            if (!commaRequiredBeforeNextType)
            {
                switch (token.Type)
                {
                    case TokenType.Identifier:
                        if (!generics.Add(token.Value))
                        {
                            report_error("Duplicate generic '{token.Value}'", enumerator.file_index, token);
                        }
                        commaRequiredBeforeNextType = true;
                        break;
                    default:
                        report_error("Unexpected token '{token.Value}' in generics", enumerator.file_index, token);
                        commaRequiredBeforeNextType = true;
                        break;
                }
            }
            else
            {
                switch (token.Type)
                {
                    case TokenType.Comma:
                        commaRequiredBeforeNextType = false;
                        break;
                    default:
                        report_error("Unexpected token '{token.Value}' when defining generics", enumerator.file_index, token);
                        commaRequiredBeforeNextType = false;
                        break;
                }
            }
        }

        if (!generics.Any())
        {
            report_error("Expected operator overload to contain generics", enumerator.file_index, enumerator.Current);
        }
        move_next(enumerator);
        overload.Generics.AddRange(generics);
    }

    // 3. Find open paren to start parsing arguments
    if (enumerator.Current.Type != TokenType.OpenParen)
    {
        // Add an error to the function AST and continue until open paren
        token = enumerator.Current;
        report_error("Unexpected token '{token.Value}' in operator overload definition", enumerator.file_index, token);
        while (move_next(enumerator) && enumerator.Current.Type != TokenType.OpenParen);
    }

    // 4. Get the arguments for the operator overload
    var commaRequiredBeforeNextArgument = false;
    DeclarationAst currentArgument = null;
    while (move_next(enumerator))
    {
        token = enumerator.Current;

        if (token.Type == TokenType.CloseParen)
        {
            if (commaRequiredBeforeNextArgument)
            {
                overload.Arguments.Add(currentArgument);
                currentArgument = null;
            }
            break;
        }

        switch (token.Type)
        {
            case TokenType.Identifier:
                if (commaRequiredBeforeNextArgument)
                {
                    report_error("Comma required after declaring an argument", enumerator.file_index, token);
                }
                else if (currentArgument == null)
                {
                    currentArgument = CreateAst<DeclarationAst>(token, enumerator.file_index);
                    currentArgument.TypeDefinition = ParseType(enumerator, argument: true);
                    for (var i = 0; i < overload.Generics.Count; i++)
                    {
                        var generic = overload.Generics[i];
                        if (SearchForGeneric(generic, i, currentArgument.TypeDefinition))
                        {
                            currentArgument.HasGenerics = true;
                        }
                    }
                    if (overload.Arguments.Count == 0)
                    {
                        overload.Type = currentArgument.TypeDefinition;
                    }
                }
                else
                {
                    currentArgument.Name = token.Value;
                    commaRequiredBeforeNextArgument = true;
                }
                break;
            case TokenType.Comma:
                if (commaRequiredBeforeNextArgument)
                {
                    overload.Arguments.Add(currentArgument);
                }
                else
                {
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);
                }
                currentArgument = null;
                commaRequiredBeforeNextArgument = false;
                break;
            default:
                report_error("Unexpected token '{token.Value}' in arguments", enumerator.file_index, token);
                break;
        }

        if (enumerator.Current.Type == TokenType.CloseParen)
        {
            break;
        }
    }

    if (currentArgument != null)
    {
        report_error("Incomplete argument in overload for type '{overload.Type.Name}'", enumerator.file_index, enumerator.Current);
    }

    if (!commaRequiredBeforeNextArgument && overload.Arguments.Any())
    {
        report_error("Unexpected comma in arguments", enumerator.file_index, enumerator.Current);
    }

    // 5. Set the return type based on the operator
    move_next(enumerator);
    switch (overload.Operator)
    {
        case Operator.And:
        case Operator.Or:
        case Operator.Equality:
        case Operator.NotEqual:
        case Operator.GreaterThanEqual:
        case Operator.LessThanEqual:
        case Operator.GreaterThan:
        case Operator.LessThan:
        case Operator.Xor:
            overload.ReturnTypeDefinition = new TypeDefinition {Name = "bool"};
            break;
        case Operator.Subscript:
            if (enumerator.Current.Type != TokenType.Colon)
            {
                report_error("Unexpected to define return type for subscript", enumerator.file_index, enumerator.Current);
            }
            else
            {
                if (move_next(enumerator))
                {
                    overload.ReturnTypeDefinition = ParseType(enumerator);
                    for (var i = 0; i < overload.Generics.Count; i++)
                    {
                        if (SearchForGeneric(overload.Generics[i], i, overload.ReturnTypeDefinition))
                        {
                            overload.Flags |= FunctionFlags.ReturnTypeHasGenerics;
                        }
                    }
                    move_next(enumerator);
                }
            }
            break;
        default:
            overload.ReturnTypeDefinition = overload.Type;
            if (overload.Generics.Any())
            {
                overload.Flags |= FunctionFlags.ReturnTypeHasGenerics;
            }
            for (var i = 0; i < overload.Generics.Count; i++)
            {
                SearchForGeneric(overload.Generics[i], i, overload.ReturnTypeDefinition);
            }
            break;
    }

    // 6. Handle compiler directives
    if (enumerator.Current.Type == TokenType.Pound)
    {
        if (!move_next(enumerator))
        {
            report_error("Expected compiler directive value", enumerator.file_index, enumerator.Last);
            return null;
        }
        switch (enumerator.Current.Value)
        {
            case "print_ir":
                overload.Flags |= FunctionFlags.PrintIR;
                break;
            default:
                report_error("Unexpected compiler directive '{enumerator.Current.Value}'", enumerator.file_index, enumerator.Current);
                break;
        }
        move_next(enumerator);
    }

    // 7. Find open brace to start parsing body
    if (enumerator.Current.Type != TokenType.OpenBrace)
    {
        // Add an error and continue until open paren
        token = enumerator.Current;
        report_error("Unexpected token '{token.Value}' in operator overload definition", enumerator.file_index, token);
        while (move_next(enumerator) && enumerator.Current.Type != TokenType.OpenBrace);
    }

    // 8. Parse body
    overload.Body = ParseScope(enumerator, overload);

    return overload;
}

InterfaceAst ParseInterface(TokenEnumerator* enumerator)
{
    var interfaceAst = CreateAst<InterfaceAst>(enumerator);
    interfaceAst.Private = enumerator.Private;
    move_next(enumerator);

    // 1a. Check if the return type is void
    if (!peek(enumerator, &var token))
    {
        report_error("Expected interface definition", enumerator.file_index, token);
    }
    if (token.Type != TokenType.OpenParen)
    {
        interfaceAst.ReturnTypeDefinition = ParseType(enumerator);
        move_next(enumerator);
    }

    // 1b. Handle multiple return values
    if (enumerator.Current.Type == TokenType.Comma)
    {
        var returnType = CreateAst<TypeDefinition>(interfaceAst.ReturnTypeDefinition);
        returnType.Compound = true;
        returnType.Generics.Add(interfaceAst.ReturnTypeDefinition);
        interfaceAst.ReturnTypeDefinition = returnType;

        while (enumerator.Current.Type == TokenType.Comma)
        {
            if (!move_next(enumerator))
            {
                break;
            }
            returnType.Generics.Add(ParseType(enumerator));
            move_next(enumerator);
        }
    }

    // 1b. Set the name of the interface or get the name from the type
    if (!enumerator.Remaining)
    {
        report_error("Expected the interface name to be declared", enumerator.file_index, enumerator.Last);
        return null;
    }
    switch (enumerator.Current.Type)
    {
        case TokenType.Identifier:
            interfaceAst.Name = enumerator.Current.Value;
            move_next(enumerator);
            break;
        case TokenType.OpenParen:
            if (interfaceAst.ReturnTypeDefinition.Name == "*" || interfaceAst.ReturnTypeDefinition.Count != null)
            {
                report_error("Expected the interface name to be declared", interfaceAst.ReturnTypeDefinition);
            }
            else
            {
                interfaceAst.Name = interfaceAst.ReturnTypeDefinition.Name;
                if (interfaceAst.ReturnTypeDefinition.Generics.Any())
                {
                    report_error("Interface '{interfaceAst.Name}' cannot have generics", interfaceAst.ReturnTypeDefinition);
                }
                interfaceAst.ReturnTypeDefinition = null;
            }
            break;
        default:
            report_error("Expected the interface name to be declared", enumerator.file_index, enumerator.Current);
            move_next(enumerator);
            break;
    }

    // 2. Parse arguments until a close paren
    var commaRequiredBeforeNextArgument = false;
    DeclarationAst currentArgument = null;
    while (move_next(enumerator))
    {
        token = enumerator.Current;

        if (token.Type == TokenType.CloseParen)
        {
            if (commaRequiredBeforeNextArgument)
            {
                interfaceAst.Arguments.Add(currentArgument);
                currentArgument = null;
            }
            break;
        }

        switch (token.Type)
        {
            case TokenType.Identifier:
                if (commaRequiredBeforeNextArgument)
                {
                    report_error("Comma required after declaring an argument", enumerator.file_index, token);
                }
                else if (currentArgument == null)
                {
                    currentArgument = CreateAst<DeclarationAst>(token, enumerator.file_index);
                    currentArgument.TypeDefinition = ParseType(enumerator);
                }
                else
                {
                    currentArgument.Name = token.Value;
                    commaRequiredBeforeNextArgument = true;
                }
                break;
            case TokenType.Comma:
                if (commaRequiredBeforeNextArgument)
                {
                    interfaceAst.Arguments.Add(currentArgument);
                }
                else
                {
                    report_error("Unexpected comma in arguments", enumerator.file_index, token);
                }
                currentArgument = null;
                commaRequiredBeforeNextArgument = false;
                break;
            case TokenType.Equals:
                if (commaRequiredBeforeNextArgument)
                {
                    report_error("Interface '{interfaceAst.Name}' cannot have default argument values", enumerator.file_index, token);
                    while (move_next(enumerator))
                    {
                        if (enumerator.Current.Type == TokenType.Comma)
                        {
                            commaRequiredBeforeNextArgument = false;
                            interfaceAst.Arguments.Add(currentArgument);
                            currentArgument = null;
                            break;
                        }
                        else if (enumerator.Current.Type == TokenType.Comma)
                        {
                            interfaceAst.Arguments.Add(currentArgument);
                            currentArgument = null;
                            break;
                        }
                    }
                }
                else
                {
                    report_error("Unexpected token '=' in arguments", enumerator.file_index, token);
                }
                break;
            default:
                report_error("Unexpected token '{token.Value}' in arguments", enumerator.file_index, token);
                break;
        }

        if (enumerator.Current.Type == TokenType.CloseParen)
        {
            break;
        }
    }

    if (currentArgument != null)
    {
        report_error("Incomplete argument in interface '{interfaceAst.Name}'", enumerator.file_index, enumerator.Current);
    }

    if (!commaRequiredBeforeNextArgument && interfaceAst.Arguments.Any())
    {
        report_error("Unexpected comma in arguments", enumerator.file_index, enumerator.Current);
    }

    return interfaceAst;
}

TypeDefinition ParseType(TokenEnumerator* enumerator, IFunction currentFunction = null, bool argument = false, int depth = 0)
{
    var typeDefinition = CreateAst<TypeDefinition>(enumerator);
    typeDefinition.Name = enumerator.Current.Value;

    // Alias int to s32
    if (typeDefinition.Name == "int")
    {
        typeDefinition.Name = "s32";
    }

    if (enumerator.Current.Type == TokenType.VarArgs)
    {
        if (!argument)
        {
            report_error("Variable args type can only be used as an argument type", enumerator.file_index, enumerator.Current);
        }
        return typeDefinition;
    }

    // Determine whether to parse a generic type, otherwise return
    if (peek(enumerator, &var token) && token.Type == TokenType.LessThan)
    {
        // Clear the '<' before entering loop
        move_next(enumerator);
        var commaRequiredBeforeNextType = false;
        while (move_next(enumerator))
        {
            token = enumerator.Current;

            if (token.Type == TokenType.GreaterThan)
            {
                if (!commaRequiredBeforeNextType && typeDefinition.Generics.Any())
                {
                    report_error("Unexpected comma in type", enumerator.file_index, token);
                }
                break;
            }
            else if (token.Type == TokenType.ShiftRight)
            {
                // Split the token and insert a greater than after the current token
                token.Value = ">";
                var newToken = new Token
                {
                    Type = TokenType.GreaterThan, Value = ">", Line = token.Line, Column = token.Column + 1
                };
                enumerator.Insert(newToken);
                break;
            }
            else if (token.Type == TokenType.RotateRight)
            {
                // Split the token and insert a shift right after the current token
                token.Value = ">";
                var newToken = new Token
                {
                    Type = TokenType.ShiftRight, Value = ">>", Line = token.Line, Column = token.Column + 1
                };
                enumerator.Insert(newToken);
                break;
            }

            if (!commaRequiredBeforeNextType)
            {
                switch (token.Type)
                {
                    case TokenType.Identifier:
                        typeDefinition.Generics.Add(ParseType(enumerator, currentFunction, depth: depth + 1));
                        commaRequiredBeforeNextType = true;
                        break;
                    default:
                        report_error("Unexpected token '{token.Value}' in type definition", enumerator.file_index, token);
                        commaRequiredBeforeNextType = true;
                        break;
                }
            }
            else
            {
                switch (token.Type)
                {
                    case TokenType.Comma:
                        commaRequiredBeforeNextType = false;
                        break;
                    default:
                        report_error("Unexpected token '{token.Value}' in type definition", enumerator.file_index, token);
                        commaRequiredBeforeNextType = false;
                        break;
                }
            }
        }

        if (!typeDefinition.Generics.Any())
        {
            report_error("Expected type to contain generics", enumerator.file_index, enumerator.Current);
        }
    }

    while (peek(enumerator, &token) && token.Type == TokenType.Asterisk)
    {
        move_next(enumerator);
        var pointerType = CreateAst<TypeDefinition>(enumerator);
        pointerType.Name = "*";
        pointerType.Generics.Add(typeDefinition);
        typeDefinition = pointerType;
    }

    if (peek(enumerator, &token) && token.Type == TokenType.OpenBracket)
    {
        // Skip over the open bracket and parse the expression
        move_next(enumerator);
        move_next(enumerator);
        typeDefinition.Count = ParseExpression(enumerator, currentFunction, null, TokenType.CloseBracket);
    }

    return typeDefinition;
}

bool TryParseType(TokenEnumerator* enumerator, out TypeDefinition typeDef)
{
    var steps = 0;
    if (TryParseType(enumerator.Current, enumerator, ref steps, out typeDef, out _, out _))
    {
        enumerator.Move(steps);
        return true;
    }
    return false;
}

bool TryParseType(Token name, TokenEnumerator* enumerator, ref int steps, out TypeDefinition typeDefinition, out bool endsWithShift, out bool endsWithRotate, int depth = 0)
{
    typeDefinition = CreateAst<TypeDefinition>(name, enumerator.file_index);
    typeDefinition.Name = name.Value;
    endsWithShift = false;
    endsWithRotate = false;

    // Alias int to s32
    if (typeDefinition.Name == "int")
    {
        typeDefinition.Name = "s32";
    }

    // Determine whether to parse a generic type, otherwise return
    if (peek(enumerator, &var token, steps) && token.Type == TokenType.LessThan)
    {
        // Clear the '<' before entering loop
        steps++;
        var commaRequiredBeforeNextType = false;
        while (peek(enumerator, &token, steps))
        {
            steps++;
            if (token.Type == TokenType.GreaterThan)
            {
                if (!commaRequiredBeforeNextType && typeDefinition.Generics.Any())
                {
                    return false;
                }
                break;
            }
            else if (token.Type == TokenType.ShiftRight)
            {
                if ((depth % 3 != 1 && !endsWithShift) || (!commaRequiredBeforeNextType && typeDefinition.Generics.Any()))
                {
                    return false;
                }
                endsWithShift = true;
                break;
            }
            else if (token.Type == TokenType.RotateRight)
            {
                if ((depth % 3 != 2 && !endsWithRotate) || (!commaRequiredBeforeNextType && typeDefinition.Generics.Any()))
                {
                    return false;
                }
                endsWithRotate = true;
                break;
            }

            if (!commaRequiredBeforeNextType)
            {
                switch (token.Type)
                {
                    case TokenType.Identifier:
                        if (!TryParseType(token, enumerator, ref steps, out var genericType, out endsWithShift, out endsWithRotate, depth + 1))
                        {
                            return false;
                        }
                        if (endsWithShift || endsWithRotate)
                        {
                            steps--;
                        }
                        typeDefinition.Generics.Add(genericType);
                        commaRequiredBeforeNextType = true;
                        break;
                    default:
                        return false;
                }
            }
            else
            {
                switch (token.Type)
                {
                    case TokenType.Comma:
                        commaRequiredBeforeNextType = false;
                        break;
                    default:
                        return false;
                }
            }
        }

        if (!typeDefinition.Generics.Any())
        {
            return false;
        }
    }

    while (peek(enumerator, &token, steps) && token.Type == TokenType.Asterisk)
    {
        var pointerType = CreateAst<TypeDefinition>(token, enumerator.file_index);
        pointerType.Name = "*";
        pointerType.Generics.Add(typeDefinition);
        typeDefinition = pointerType;
        steps++;
        endsWithShift = false;
        endsWithRotate = false;
    }

    return true;
}

ConstantAst ParseConstant(Token token, int fileIndex)
{
    var constant = CreateAst<ConstantAst>(token, fileIndex);
    switch (token.Type)
    {
        case TokenType.Literal:
            constant.TypeName = "string";
            constant.String = token.Value;
            return constant;
        case TokenType.Character:
            constant.TypeName = "u8";
            constant.Value = new Constant {UnsignedInteger = (byte)token.Value[0]};
            return constant;
        case TokenType.Number:
            if (token.Flags == TokenFlags.None)
            {
                if (int.TryParse(token.Value, out var s32))
                {
                    constant.TypeName = "s32";
                    constant.Value = new Constant {Integer = s32};
                    return constant;
                }

                if (long.TryParse(token.Value, out var s64))
                {
                    constant.TypeName = "s64";
                    constant.Value = new Constant {Integer = s64};
                    return constant;
                }

                if (ulong.TryParse(token.Value, out var u64))
                {
                    constant.TypeName = "u64";
                    constant.Value = new Constant {UnsignedInteger = u64};
                    return constant;
                }

                report_error("Invalid integer '{token.Value}', must be 64 bits or less", fileIndex, token);
                return null;
            }

            if (token.Flags.HasFlag(TokenFlags.Float))
            {
                if (float.TryParse(token.Value, out var f32))
                {
                    constant.TypeName = "float";
                    constant.Value = new Constant {Double = (double)f32};
                    return constant;
                }

                if (double.TryParse(token.Value, out var f64))
                {
                    constant.TypeName = "float64";
                    constant.Value = new Constant {Double = f64};
                    return constant;
                }

                report_error("Invalid floating point number '{token.Value}', must be single or double precision", fileIndex, token);
                return null;
            }

            if (token.Flags.HasFlag(TokenFlags.HexNumber))
            {
                if (token.Value.Length == 2)
                {
                    report_error("Invalid number '{token.Value}'", fileIndex, token);
                    return null;
                }

                var value = token.Value.Substring(2);
                if (value.Length <= 8)
                {
                    if (uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u32))
                    {
                        constant.TypeName = "u32";
                        constant.Value = new Constant {UnsignedInteger = u32};
                        return constant;
                    }
                }
                else if (value.Length <= 16)
                {
                    if (ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u64))
                    {
                        constant.TypeName = "u64";
                        constant.Value = new Constant {UnsignedInteger = u64};
                        return constant;
                    }
                }
                report_error("Invalid integer '{token.Value}'", fileIndex, token);
                return null;
            }
            report_error("Unable to determine type of token '{token.Value}'", fileIndex, token);
            return null;
        case TokenType.Boolean:
            constant.TypeName = "bool";
            constant.Value = new Constant {Boolean = token.Value == "true"};
            return constant;
        default:
            report_error("Unable to determine type of token '{token.Value}'", fileIndex, token);
            return null;
    }
}

CastAst ParseCast(TokenEnumerator* enumerator, IFunction currentFunction)
{
    var castAst = CreateAst<CastAst>(enumerator);

    // 1. Try to get the open paren to begin the cast
    if (!move_next(enumerator) || enumerator.Current.Type != TokenType.OpenParen)
    {
        report_error("Expected '(' after 'cast'", enumerator.file_index, enumerator.Current);
        return null;
    }

    // 2. Get the target type
    if (!move_next(enumerator))
    {
        report_error("Expected to get the target type for the cast", enumerator.file_index, enumerator.Last);
        return null;
    }
    castAst.TargetTypeDefinition = ParseType(enumerator, currentFunction);
    if (currentFunction != null)
    {
        for (var i = 0; i < currentFunction.Generics.Count; i++)
        {
            var generic = currentFunction.Generics[i];
            if (SearchForGeneric(generic, i, castAst.TargetTypeDefinition))
            {
                castAst.HasGenerics = true;
            }
        }
    }

    // 3. Expect to get a comma
    if (!move_next(enumerator) || enumerator.Current.Type != TokenType.Comma)
    {
        report_error("Expected ',' after type in cast", enumerator.file_index, enumerator.Current);
        return null;
    }

    // 4. Get the value expression
    if (!move_next(enumerator))
    {
        report_error("Expected to get the value for the cast", enumerator.file_index, enumerator.Last);
        return null;
    }
    castAst.Value = ParseExpression(enumerator, currentFunction, null, TokenType.CloseParen);

    return castAst;
}

T CreateAst<T>(IAst source) where T : IAst, new()
{
    if (source == null) return new();

    return new()
    {
        file_index = source.FileIndex,
        Line = source.Line,
        Column = source.Column
    };
}

T CreateAst<T>(TokenEnumerator* enumerator) where T : IAst, new()
{
    var token = enumerator.Current;
    return new()
    {
        file_index = enumerator.FileIndex,
        Line = token.Line,
        Column = token.Column
    };
}

T CreateAst<T>(Token token, int fileIndex) where T : IAst, new()
{
    return new()
    {
        file_index = fileIndex,
        Line = token.Line,
        Column = token.Column
    };
}

Operator ConvertOperator(Token token)
{
    switch (token.Type)
    {
        // Multi-character operators
        case TokenType.And:
            return Operator.And;
        case TokenType.Or:
            return Operator.Or;
        case TokenType.Equality:
            return Operator.Equality;
        case TokenType.NotEqual:
            return Operator.NotEqual;
        case TokenType.GreaterThanEqual:
            return Operator.GreaterThanEqual;
        case TokenType.LessThanEqual:
            return Operator.LessThanEqual;
        case TokenType.ShiftLeft:
            return Operator.ShiftLeft;
        case TokenType.ShiftRight:
            return Operator.ShiftRight;
        case TokenType.RotateLeft:
            return Operator.RotateLeft;
        case TokenType.RotateRight:
            return Operator.RotateRight;
        // Handle single character operators
        default:
            var op = (Operator)token.Value[0];
            return Enum.IsDefined(typeof(Operator), op) ? op : Operator.None;
    }
}
