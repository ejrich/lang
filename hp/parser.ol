#import "lexer.ol"

parse(string file_contents, string library) {
    tokens := get_file_tokens(file_contents);

    lib_file := fopen(library, "w+");

    if lib_file {
        node := tokens.head;

        while node {
            type := node.data.type;
            if type == TokenType.Typedef {
                node = parse_typedef(node, lib_file, library);
            }
            else if type == TokenType.Extern {
                node = parse_extern(node, lib_file, library);
            }
            else {
                node = node.next;
            }
        }

        fclose(lib_file);
    }
    else {
        printf("Unable to create file '%s'\n", library);
    }
}

struct TypeDefinition {
    name: string;
    pointer_count: int;
}

TypeDefinition, Node<Token>* parse_type(Node<Token>* node) {
    if node.data.type == TokenType.Const {
        node = node.next;
    }

    type := node.data.type;

    if type == TokenType.Signed {
        if node.next.data.type == TokenType.Long {
            if node.next.data.type == TokenType.Long {
                node = node.next;
            }
            if node.next.data.type == TokenType.Int {
                node = node.next;
            }

            return check_for_pointers("s64", node.next);
        }
        if node.next.data.type == TokenType.Int {
            return check_for_pointers("s32", node.next);
        }
        if node.next.data.type == TokenType.Short {
            if node.next.data.type == TokenType.Int {
                node = node.next;
            }

            return check_for_pointers("s16", node.next);
        }
        if node.next.data.type == TokenType.Char {
            return check_for_pointers("s8", node.next);
        }
    }
    else if type == TokenType.Unsigned {
        if node.next.data.type == TokenType.Long {
            if node.next.data.type == TokenType.Long {
                node = node.next;
            }
            if node.next.data.type == TokenType.Int {
                node = node.next;
            }

            return check_for_pointers("u64", node.next);
        }
        if node.next.data.type == TokenType.Int {
            return check_for_pointers("u32", node.next);
        }
        if node.next.data.type == TokenType.Short {
            if node.next.data.type == TokenType.Int {
                node = node.next;
            }

            return check_for_pointers("u16", node.next);
        }
        if node.next.data.type == TokenType.Char {
            return check_for_pointers("u8", node.next);
        }
    }
    else if type == TokenType.Long {
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

struct Argument {
    type: TypeDefinition;
    array_length: string;
    name: string;
}

struct Function {
    return_type: TypeDefinition;
    name: string;
    arguments: Array<Argument>;
}

Node<Token>* parse_extern(Node<Token>* node, FILE* file, string library) {
    node = node.next;

    if node {
        function: Function;
        function.return_type, node = parse_type(node);
        function.name = node.data.value;

        // Move over '('
        node = node.next;
        node = node.next;

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
                // TODO Free
                argument.name.data = null;
                argument.array_length.length = 0;
                argument.array_length.data = null;

                node = node.next;
            }
            else if type == TokenType.CloseParen {
                if !new_arg
                    array_insert(&function.arguments, argument);
                // Move over ')' and ';'
                node = node.next;
                node = node.next;
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

        // Print function definition to file
        if function.return_type.name != "void" || function.return_type.pointer_count > 0 {
            print_type(function.return_type, file);
            fputc(' ', file);
        }

        print_string(function.name, file);
        fputc('(', file);

        each arg, i in function.arguments {
            if !string_is_empty(arg.array_length) {
                print_string("CArray<", file);
                print_type(arg.type, file);
                print_string(">[", file);
                print_string(arg.array_length, file);
                print_string("]", file);
            }
            else print_type(arg.type, file);
            fputc(' ', file);

            if string_is_empty(arg.name) {
                fputc('a' + i, file);
            }
            else {
                print_string(arg.name, file);
            }

            if i < function.arguments.length - 1 {
                print_string(", ", file);
            }
        }

        fprintf(file, ") #extern \"%s\"\n\n", library);
    }

    return node;
}

Node<Token>* parse_typedef(Node<Token>* node, FILE* file, string library) {
    node = node.next;

    if node {
        type := node.data.type;

        if type == TokenType.Struct {
            return parse_struct(node, file, library);
        }
        else if type == TokenType.Union {
            return parse_struct(node, file, library, "union");
        }
        else if type == TokenType.Enum {
            return parse_enum(node, file, library);
        }
        else {
            // TODO Handle type aliasing
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

Node<Token>* parse_struct(Node<Token>* node, FILE* file, string library, string type_name = "struct") {
    node = node.next;

    if node {
        struct_def: Struct;

        if node.data.type == TokenType.Identifier {
            struct_def.alias = node.data.value;
            node = node.next;
        }

        // Move over '{'
        node = node.next;

        new_field := true;
        struct_field: StructField;

        while node {
            type := node.data.type;

            if type == TokenType.Struct node = node.next;
            else if type == TokenType.Identifier {
                if new_field {
                    struct_field.type, node = parse_type(node);
                    new_field = false;
                }
                else {
                    array_insert(&struct_field.names, node.data.value);
                    node = node.next;
                }
            }
            else if type == TokenType.OpenBracket {
                struct_field.array_length, node = get_array_length(node.next);
            }
            else if type == TokenType.SemiColon {
                array_insert(&struct_def.fields, struct_field);

                // Reset StructField struct_fields
                new_field = true;
                struct_field.names.length = 0;
                // TODO Free
                struct_field.names.data = null;
                struct_field.array_length.length = 0;
                struct_field.array_length.data = null;

                node = node.next;
            }
            else if type == TokenType.CloseBrace {
                node = node.next;
                break;
            }
            else if new_field {
                struct_field.type, node = parse_type(node);
                new_field = false;
            }
            else {
                node = node.next;
            }
        }

        if node.data.type == TokenType.Star {
            struct_def.pointer = true;
            node = node.next;
        }

        struct_def.name = node.data.value;

        // Move over ';'
        node = node.next;
        node = node.next;

        // Print struct definition to file
        print_string(type_name, file);
        fputc(' ', file);
        print_string(struct_def.name, file);
        print_string(" {", file);

        if struct_def.fields.length {
            fputc('\n', file);
        }

        each field in struct_def.fields {
            each name in field.names {
                print_string("    ", file);
                print_string(name, file);
                print_string(": ", file);
                if !string_is_empty(field.array_length) {
                    print_string("CArray<", file);
                    print_type(field.type, file);
                    print_string(">[", file);
                    print_string(field.array_length, file);
                    print_string("]", file);
                }
                else print_type(field.type, file);

                print_string(";\n", file);
            }
        }
        print_string("}\n", file);

        if struct_def.fields.length {
            fputc('\n', file);
        }
    }

    return node;
}

string, Node<Token>* get_array_length(Node<Token>* node) {
    array_length := node.data.value;

    node = node.next;
    while node.data.type != TokenType.CloseBracket {
        node = node.next;
    }

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

Node<Token>* parse_enum(Node<Token>* node, FILE* file, string library) {
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
                if string_is_empty(enum_value.name) {
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
        print_string("enum ", file);
        print_string(enum_def.name, file);
        print_string(" {\n", file);

        each value in enum_def.values {
            print_string("    ", file);
            print_string(value.name, file);

            if !string_is_empty(value.value) {
                print_string(" = ", file);
                print_string(value.value, file);
            }

            print_string(";\n", file);
        }
        print_string("}\n\n", file);
    }

    return node;
}

print_type(TypeDefinition type, FILE* file) {
    print_string(type.name, file);
    each i in 1..type.pointer_count {
        fputc('*', file);
    }
}

print_string(string value, FILE* file) {
    fwrite(value.data, 1, value.length, file);
}


fputc(u8 char, FILE* file) #extern "c"
fwrite(void* ptr, int size, int count, FILE* file) #extern "c"
fprintf(FILE* file, string format, ... args) #extern "c"
