main() {
    if command_line_arguments.length == 0 {
        printf("Please provide an input file\n");
        exit_code = 1;
        return;
    }

    file := fopen(command_line_arguments[0], "r");
    if file {
        printf("Parsing file '%s'\n", command_line_arguments[0]);

        fclose(file);
    }
    else {
        printf("Input file '%s' not found\n", command_line_arguments[0]);
        exit_code = 1;
    }
}

struct FILE {}

FILE* fopen(string file, string type) #extern "c"
int fseek(FILE* file, s64 offset, int origin) #extern "c"
s64 ftell(FILE* file) #extern "c"
int fclose(FILE* file) #extern "c"
