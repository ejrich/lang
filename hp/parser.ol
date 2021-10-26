#import "lexer.ol"

parse(string file_contents, string library) {
    tokens := get_file_tokens(file_contents);

    lib_file := fopen(library, "w+");

    if lib_file {
        node := tokens.head;
        i := 0;

        new_line := "\n"; #const
        while node {
            if node.data {
                a := node.data;
                value := node.data.value;
                fwrite(value.data, 1, value.length, lib_file);
                fwrite(new_line.data, 1, 1, lib_file);
            }
            else {
                printf("%p %p\n", node, tokens.end);
            }
            node = node.next;
            i++;
            if node == tokens.end printf("Hello world\n");
        }

        fclose(lib_file);
    }
    else {
        printf("Unable to create file '%s'\n", library);
    }
}

fwrite(void* ptr, int size, int count, FILE* file) #extern "c"
