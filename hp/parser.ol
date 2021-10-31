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

struct Argument {
    type_name: string;
    pointer: bool;
    name: string;
}

struct Function {
    type_name: string;
    pointer: bool; // TODO Better handle this
    arguments: Array<Argument>;
}

Node<Token>* parse_extern(Node<Token>* node, FILE* file, string library) {
    node = node.next;

    if node {
        function: Function = { type_name = node.data.value; }
        node = node.next;

        if node.data.type == TokenType.Star {
            function.pointer = true;
            node = node.next;
        }

        // Move over '('
        node = node.next;

        new_arg := true;
        argument: Argument;

        while node {
            type := node.data.type;

            if type == TokenType.Struct node = node.next;
            else if type == TokenType.Identifier {
                if string_is_empty(argument.type_name) argument.type_name = node.data.value;
                else argument.name = node.data.value;
            }
            else if type == TokenType.Star argument.pointer = true;
            else if type == TokenType.Comma {
                array_insert(&function.arguments, argument);

                // Reset argument fields
                argument.type_name.length = 0;
                argument.type_name.data = null;
                argument.pointer = false;
                argument.name.length = 0;
                argument.name.data = null;
            }
            else if type == TokenType.CloseParen {
                array_insert(&function.arguments, argument);
                // Move over ')' and ';'
                node = node.next;
                node = node.next;
                break;
            }

            node = node.next;
        }

        fprintf(file, " #extern \"%s\"\n\n", library);
    }

    return node;
}

Node<Token>* parse_typedef(Node<Token>* node, FILE* file, string library) {
    return node.next;
}


fputc(u8 char, FILE* file) #extern "c"
fwrite(void* ptr, int size, int count, FILE* file) #extern "c"
fprintf(FILE* file, string format, ... args) #extern "c"
