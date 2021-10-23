main() {
    if command_line_arguments.length == 0 {
        printf("Please provide an input file\n");
        exit_code = 1;
        return;
    }

    file := fopen(command_line_arguments[0], "r");
    if file {
        fseek(file, 0, 2);
        size := ftell(file);
        fseek(file, 0, 0);

        printf("Parsing file '%s', size %d\n", command_line_arguments[0], size);

        file_contents: string = {length = size; data = malloc(size + 1);}

        fread(file_contents.data, 1, size, file);
        fclose(file);

        free(file_contents.data);
    }
    else {
        printf("Input file '%s' not found\n", command_line_arguments[0]);
        exit_code = 2;
    }
}

struct FILE {}

FILE* fopen(string file, string type) #extern "c"
int fseek(FILE* file, s64 offset, int origin) #extern "c"
s64 ftell(FILE* file) #extern "c"
int fread(void* buffer, u32 size, u32 length, FILE* file) #extern "c"
int fclose(FILE* file) #extern "c"
