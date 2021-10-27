#import "lexer.ol"

parse(string file_contents, string library) {
    tokens := get_file_tokens(file_contents);

    lib_file := fopen(library, "w+");

    if lib_file {
        node := tokens.head;

        while node {
            value := node.data.value;
            char := value.data;
            fwrite(value.data, 1, value.length, lib_file);
            fputc('\n', lib_file);
            node = node.next;
        }

        fclose(lib_file);
    }
    else {
        printf("Unable to create file '%s'\n", library);
    }
}

fputc(u8 char, FILE* file) #extern "c"
fwrite(void* ptr, int size, int count, FILE* file) #extern "c"
