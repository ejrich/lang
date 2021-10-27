#import "lexer.ol"

parse(string file_contents, string library) {
    tokens := get_file_tokens(file_contents);

    lib_file := fopen(library, "w+");

    if lib_file {
        node := tokens.head;

        new_line := "\n"; #const
        while node {
            value := node.data.value;
            fwrite(value.data, 1, value.length, lib_file);
            fwrite(new_line.data, 1, 1, lib_file);
            node = node.next;
        }

        fclose(lib_file);
    }
    else {
        printf("Unable to create file '%s'\n", library);
    }
}

fwrite(void* ptr, int size, int count, FILE* file) #extern "c"
