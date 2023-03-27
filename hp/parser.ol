#import "lexer.ol"

parse(string file_contents, string library, string output_file) {
    tokens := get_file_tokens(file_contents);

    success, lib_file := open_file(output_file, FileFlags.Create);

    if success {
        node := tokens.head;

        while node {
            type := node.data.type;
            if type == TokenType.Typedef {
                node = parse_typedef(node, lib_file);
            }
            else if type == TokenType.Struct {
                node = parse_struct(node, lib_file, alias = false);
            }
            else if type == TokenType.Union {
                node = parse_struct(node, lib_file, "union", false);
            }
            else if type == TokenType.Extern {
                node = parse_function(node.next, lib_file, library);
            }
            else if type == TokenType.Static {
                if node.next.data.type == TokenType.Const node = parse_constant(node.next.next, lib_file);
                else node = move_over(node.next, TokenType.CloseBrace);
            }
            else if type == TokenType.Extension node = node.next;
            else if type == TokenType.Attribute {
                node = move_over_paren(node.next);
            }
            else {
                node = parse_function(node, lib_file, library);
            }
        }

        close_file(lib_file);
    }
    else {
        print("Unable to create file '%'\n", output_file);
    }
}

Node<Token>* move_until(Node<Token>* node, TokenType type) {
    while node.data.type != type {
        node = node.next;
    }

    return node;
}

Node<Token>* move_over(Node<Token>* node, TokenType type) {
    node = move_until(node, type);

    return node.next;
}

Node<Token>* move_over_paren(Node<Token>* node) {
    node = node.next;
    while node.data.type != TokenType.CloseParen {
        if node.data.type == TokenType.OpenParen {
            node = move_over_paren(node);
        }
        else {
            node = node.next;
        }
    }

    return node.next;
}

struct TypeDefinition {
    name: string;
    pointer_count: int;
}

types: HashTable<TypeDefinition>;

TypeDefinition, Node<Token>* parse_type(Node<Token>* node) {
    if node.data.type == TokenType.Const {
        node = node.next;
    }

    type := node.data.type;

    if type == TokenType.Signed {
        if node.next.data.type == TokenType.Long {
            node = node.next;
            if node.next.data.type == TokenType.Long {
                node = node.next;
            }
            if node.next.data.type == TokenType.Int {
                node = node.next;
            }

            return check_for_pointers("s64", node.next);
        }
        if node.next.data.type == TokenType.Int {
            return check_for_pointers("s32", node.next.next);
        }
        if node.next.data.type == TokenType.Short {
            node = node.next;
            if node.next.data.type == TokenType.Int {
                node = node.next;
            }

            return check_for_pointers("s16", node.next);
        }
        if node.next.data.type == TokenType.Char {
            return check_for_pointers("s8", node.next.next);
        }

        return check_for_pointers("s32", node.next);
    }
    else if type == TokenType.Unsigned {
        if node.next.data.type == TokenType.Long {
            node = node.next;
            if node.next.data.type == TokenType.Long {
                node = node.next;
            }
            if node.next.data.type == TokenType.Int {
                node = node.next;
            }

            return check_for_pointers("u64", node.next);
        }
        if node.next.data.type == TokenType.Int {
            return check_for_pointers("u32", node.next.next);
        }
        if node.next.data.type == TokenType.Short {
            node = node.next;
            if node.next.data.type == TokenType.Int {
                node = node.next;
            }

            return check_for_pointers("u16", node.next);
        }
        if node.next.data.type == TokenType.Char {
            return check_for_pointers("u8", node.next.next);
        }

        return check_for_pointers("u32", node.next);
    }
    else if type == TokenType.Long {
        if node.next.data.type == TokenType.Double {
            return check_for_pointers("float64", node.next.next);
        }
        if node.next.data.type == TokenType.Unsigned {
            node = node.next;
        }
        if node.next.data.type == TokenType.Long {
            node = node.next;
        }
        if node.next.data.type == TokenType.Int {
            node = node.next;
        }

        return check_for_pointers("s64", node.next);
    }
    else if type == TokenType.Int {
        return check_for_pointers("s32", node.next);
    }
    else if type == TokenType.Short {
        if node.next.data.type == TokenType.Int {
            node = node.next;
        }

        return check_for_pointers("s16", node.next);
    }
    else if type == TokenType.Char {
        return check_for_pointers("u8", node.next);
    }
    else if type == TokenType.Float {
        return check_for_pointers("float", node.next);
    }
    else if type == TokenType.Double {
        return check_for_pointers("float64", node.next);
    }

    found, type_def := search(&types, node.data.value);

    if found {
        node = node.next;
        while node.data.type == TokenType.Star {
            node = node.next;
            type_def.pointer_count++;
        }

        return type_def, node;
    }

    return check_for_pointers(node.data.value, node.next);
}

TypeDefinition, Node<Token>* check_for_pointers(string type, Node<Token>* node) {
    type_def: TypeDefinition = { name = type; }

    while node.data.type == TokenType.Star {
        node = node.next;
        type_def.pointer_count++;
    }

    return type_def, node;
}

Node<Token>* parse_constant(Node<Token>* node, File file) {
    type_def: TypeDefinition;
    type_def, node = parse_type(node);

    name := node.data.value;

    node = node.next.next;

    value := node.data.value;

    write_to_file(file, "%: ", name);
    print_type(type_def, file);
    write_to_file(file, " = %; #const\n", value);

    return move_over(node.next, TokenType.SemiColon);
}

struct Argument {
    type: TypeDefinition;
    array_length: string;
    name: string;
}

struct FunctionDefinition {
    return_type: TypeDefinition;
    name: string;
    arguments: Array<Argument>;
}

Node<Token>* parse_function(Node<Token>* node, File file, string library) {
    function: FunctionDefinition;
    function.return_type, node = parse_type(node);
    function.name = node.data.value;

    // Move over '('
    node = node.next;
    node = node.next;

    node = parse_arguments(node, &function);

    // Print function definition to file
    if function.return_type.name != "void" || function.return_type.pointer_count > 0 {
        print_type(function.return_type, file);
        write_to_file(file, ' ');
    }

    write_to_file(file, function.name);
    write_to_file(file, '(');

    each arg, i in function.arguments {
        if !string_is_empty(arg.array_length) {
            write_to_file(file, "CArray<");
            print_type(arg.type, file);
            write_to_file(file, ">[%]", arg.array_length);
        }
        else print_type(arg.type, file);
        write_to_file(file, ' ');

        if string_is_empty(arg.name) {
            write_to_file(file, 'a' + i);
        }
        else {
            write_to_file(file, arg.name);
        }

        if i < function.arguments.length - 1 {
            write_to_file(file, ", ");
        }
    }

    if function.arguments.length default_free(function.arguments.data);

    write_to_file(file, ") #extern \"%\"\n\n", library);

    return node;
}

Node<Token>* parse_arguments(Node<Token>* node, FunctionDefinition* function, bool internal = false) {
    new_arg := true;
    argument: Argument;

    while node {
        type := node.data.type;

        if type == TokenType.Struct node = node.next;
        else if type == TokenType.Identifier {
            if new_arg {
                argument.type, node = parse_type(node);
                new_arg = false;
            }
            else {
                argument.name = node.data.value;
                node = node.next;
            }
        }
        else if type == TokenType.OpenBracket {
            argument.array_length, node = get_array_length(node.next);
        }
        else if type == TokenType.Comma {
            array_insert(&function.arguments, argument);

            // Reset argument fields
            new_arg = true;
            argument.name.length = 0;
            argument.name.data = null;
            argument.array_length.length = 0;
            argument.array_length.data = null;

            node = node.next;
        }
        else if type == TokenType.CloseParen {
            if !new_arg array_insert(&function.arguments, argument);
            // Move over ')' and ';'
            node = move_until(node.next, TokenType.SemiColon);

            if !internal node = node.next;
            break;
        }
        else if new_arg {
            argument.type, node = parse_type(node);
            new_arg = false;
        }
        else {
            node = node.next;
        }
    }

    return node;
}

Node<Token>* parse_typedef(Node<Token>* node, File file) {
    node = node.next;

    if node {
        type := node.data.type;

        if type == TokenType.Struct {
            return parse_struct(node, file, typedef = true);
        }
        else if type == TokenType.Union {
            if node.next.data.type == TokenType.OpenBrace {
                return parse_struct(node, file, "union");
            }
            else if node.next.next.data.type == TokenType.OpenBrace {
                return parse_struct(node, file, "union");
            }
            return move_over(node, TokenType.SemiColon);
        }
        else if type == TokenType.Enum {
            return parse_enum(node, file);
        }
        else {
            type_def, next_node := parse_type(node);
            node = next_node;

            if node.data.type == TokenType.OpenParen {
                return parse_interface(node, file, type_def);
            }
            name := node.data.value;

            // Move over ';'
            node = move_over(node.next, TokenType.SemiColon);

            insert(&types, name, type_def);
        }
    }

    return node;
}

struct Struct {
    name: string;
    alias: string;
    pointer: bool;
    fields: Array<StructField>;
}

struct StructField {
    type: TypeDefinition;
    array_length: string;
    names: Array<string>;
}

Node<Token>* parse_struct(Node<Token>* node, File file, string type_name = "struct", bool alias = true, string struct_name = "", bool typedef = false, bool internal = false) {
    node = node.next;

    if node {
        struct_def: Struct;

        if string_is_empty(struct_name) {
            if node.data.type == TokenType.Identifier {
                if alias struct_def.alias = node.data.value;
                else struct_def.name = node.data.value;
                node = node.next;
            }

            // Move over '{'
            if node.data.type == TokenType.OpenBrace {
                node = node.next;
            }
            else if node.data.type == TokenType.SemiColon {
                return node.next;
            }
            else if typedef {
                return finish_struct_and_print(node, file, type_name, alias, struct_def, internal);
            }
        }
        else {
            struct_def.name = struct_name;
        }

        new_field := true;
        struct_field: StructField;

        while node {
            type := node.data.type;

            if type == TokenType.Identifier {
                if new_field {
                    struct_field.type, node = parse_type(node);
                    new_field = false;
                }
                else {
                    array_insert(&struct_field.names, node.data.value);
                    node = node.next;
                }
            }
            else if type == TokenType.OpenBrace {
                // Parse internal structs
                node = parse_struct(node, file, alias = string_is_empty(struct_field.type.name), struct_name = struct_field.type.name, internal = true);
            }
            else if type == TokenType.Union {
                // Parse internal union
                node = parse_struct(node, file, "union", node.next.data.type == TokenType.OpenBrace, struct_field.type.name, internal = true);
            }
            else if type == TokenType.OpenParen {
                // Parse internal interfaces
                interface_name := node.next.next.data.value;
                node = parse_interface(node, file, struct_field.type, true);

                struct_field.type.name = interface_name;
                struct_field.type.pointer_count = 0;
                array_insert(&struct_field.names, interface_name);
            }
            else if type == TokenType.OpenBracket {
                struct_field.array_length, node = get_array_length(node.next);
            }
            else if type == TokenType.Attribute {
                node = move_until(node.next, TokenType.SemiColon);
            }
            else if type == TokenType.SemiColon {
                array_insert(&struct_def.fields, struct_field);

                // Reset StructField struct_fields
                new_field = true;
                struct_field.type.name.length = 0;
                struct_field.type.name.data = null;
                struct_field.type.pointer_count = 0;
                struct_field.names.length = 0;
                struct_field.names.data = null;
                struct_field.array_length.length = 0;
                struct_field.array_length.data = null;

                node = node.next;
            }
            else if type == TokenType.CloseBrace {
                node = node.next;
                break;
            }
            else if type == TokenType.Struct || type == TokenType.Const || type == TokenType.Extension {
                node = node.next;
            }
            else if new_field {
                struct_field.type, node = parse_type(node);
                new_field = false;
            }
            else {
                node = node.next;
            }
        }

        node = finish_struct_and_print(node, file, type_name, alias, struct_def, internal);
    }

    return node;
}

Node<Token>* finish_struct_and_print(Node<Token>* node, File file, string type_name, bool alias, Struct struct_def, bool internal) {
    struct_type_def: TypeDefinition;

    if node.data.type == TokenType.Star {
        struct_def.pointer = true;
        node = node.next;
        struct_type_def.pointer_count = 1;
    }

    if alias struct_def.name = node.data.value;

    struct_type_def.name = struct_def.name;
    insert(&types, struct_def.name, struct_type_def);

    // Move over ';'
    if !internal node = move_over(node, TokenType.SemiColon);

    // Print struct definition to file
    write_to_file(file, "% % {", type_name, struct_def.name);

    if struct_def.fields.length {
        write_to_file(file, '\n');
    }

    each field in struct_def.fields {
        each name in field.names {
            write_to_file(file, "    %: ", name);
            if !string_is_empty(field.array_length) {
                write_to_file(file, "CArray<");
                print_type(field.type, file);
                write_to_file(file, ">[%]", field.array_length);
            }
            else print_type(field.type, file);

            write_to_file(file, ";\n");
        }
        if field.names.length default_free(field.names.data);
    }
    write_to_file(file, "}\n");

    if struct_def.fields.length {
        write_to_file(file, '\n');
        default_free(struct_def.fields.data);
    }

    return node;
}

string, Node<Token>* get_array_length(Node<Token>* node) {
    array_length := node.data.value;

    node = move_until(node.next, TokenType.CloseBracket);

    return array_length, node.next;
}

struct Enum {
    alias: string;
    name: string;
    values: Array<Enum_Value>;
}

struct Enum_Value {
    name: string;
    value: string;
}

Node<Token>* parse_enum(Node<Token>* node, File file) {
    node = node.next;

    if node {
        enum_def: Enum;

        if node.data.type == TokenType.Identifier {
            enum_def.alias = node.data.value;
            node = node.next;
        }

        // Move over '{'
        node = node.next;

        enum_value: Enum_Value;

        while node {
            type := node.data.type;

            if type == TokenType.Identifier {
                if string_is_empty(enum_value.name) enum_value.name = node.data.value;
                else enum_value.value = node.data.value;
            }
            else if type == TokenType.Comma {
                array_insert(&enum_def.values, enum_value);

                // Reset argument fields
                enum_value.name.length = 0;
                enum_value.name.data = null;
                enum_value.value.length = 0;
                enum_value.value.data = null;
            }
            if type == TokenType.CloseBrace {
                if !string_is_empty(enum_value.name) {
                    array_insert(&enum_def.values, enum_value);
                }
                node = node.next;
                break;
            }

            node = node.next;
        }

        enum_def.name = node.data.value;

        // Move over ';'
        node = node.next;
        node = node.next;

        // Print struct definition to file
        write_to_file(file, "enum % {\n", enum_def.name);

        each value in enum_def.values {
            write_to_file(file, "    %", value.name);

            if !string_is_empty(value.value) {
                write_to_file(file, " = %", value.value);
            }

            write_to_file(file, ";\n");
        }

        if enum_def.values.length default_free(enum_def.values.data);

        write_to_file(file, "}\n\n");
    }

    return node;
}

Node<Token>* parse_interface(Node<Token>* node, File file, TypeDefinition return_type, bool internal = false) {
    node = node.next.next;

    function: FunctionDefinition = { return_type = return_type; name = node.data.value; }

    node = parse_arguments(node.next.next.next, &function, internal);

    // Print interface definition to file
    write_to_file(file, "interface ");
    if function.return_type.name != "void" || function.return_type.pointer_count > 0 {
        print_type(function.return_type, file);
        write_to_file(file, ' ');
    }

    write_to_file(file, "%(", function.name);

    each arg, i in function.arguments {
        if !string_is_empty(arg.array_length) {
            write_to_file(file, "CArray<");
            print_type(arg.type, file);
            write_to_file(file, ">[%]", arg.array_length);
        }
        else print_type(arg.type, file);
        write_to_file(file, ' ');

        if string_is_empty(arg.name) {
            write_to_file(file, 'a' + i);
        }
        else {
            write_to_file(file, arg.name);
        }

        if i < function.arguments.length - 1 {
            write_to_file(file, ", ");
        }
    }

    if function.arguments.length default_free(function.arguments.data);
    write_to_file(file, ")\n\n");

    return node;
}

print_type(TypeDefinition type, File file) {
    write_to_file(file, type.name);
    each i in 1..type.pointer_count {
        write_to_file(file, '*');
    }
}
