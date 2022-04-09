#import standard
#import "parser.ol"
#import "type_checker.ol"
#import "llvm_backend.ol"
#import "link.ol"

release := false;
output_assembly := false;
path: string;
name: string;
output_directory: string;

linker: LinkerType;
output_type_table: OutputTypeTableConfiguration;

file_names: Array<string>;

main() {
    freq := get_performance_frequency();
    start := get_performance_counter();

    // Load cli args into build settings
    entrypoint: string;
    noThreads := false;
    each arg in command_line_arguments {
        if arg == "-R" || arg == "--release" release = true;
        else if arg == "-S" output_assembly = true;
        else if arg == "-noThreads" noThreads = true;
        else {
            if arg[0] == '-' {
                report_error(format_string("Unrecognized compiler flag '%'", arg));
            }
            else if !string_is_empty(entrypoint) {
                report_error(format_string("Multiple program entrypoints defined '%'", arg));
            }
            else {
                if !file_exists(arg) || !ends_with(arg, ".ol") {
                    report_error(format_string("Entrypoint file does not exist or is not an .ol file '%'", arg));
                }
                else {
                    // name = Path.GetFileNameWithoutExtension(arg);
                    entrypoint = arg; // Path.GetFullPath(arg);
                    // path = Path.GetDirectoryName(entrypoint);
                }
            }
        }
    }

    if string_is_empty(entrypoint) report_error("Program entrypoint not defined");
    list_errors_and_exit(ArgumentsError);

    // Parse source files to asts
    // ThreadPool.Init(noThreads);
    // TypeChecker.Init();
    parse(entrypoint);
    list_errors_and_exit(ParsingError);

    // Check types and build the program ir
    check_types();
    list_errors_and_exit(CompilationError);
    // ThreadPool.CompleteWork();
    front_end_time := get_performance_counter();

    // Build program
    object_file := llvm_build();
    build_time := get_performance_counter();

    // Link binaries
    link(object_file);
    link_time := get_performance_counter();

    // Log statistics
    print("Front-end time: % seconds\nLLVM build time: % seconds\nLinking time: % seconds", get_time(start, front_end_time, freq), get_time(front_end_time, build_time, freq), get_time(build_time, link_time, freq));

    // Allocator.Free();
    // Environment.Exit(0);
}

bool ends_with(string value, string ending) {
    if ending.length > value.length return false;

    start_index := value.length - ending.length;
    each i in 0..ending.length - 1 {
        if value[start_index + i] != ending[i] return false;
    }

    return true;
}

string name_without_extension(string file) {
    result: string;

    return result;
}

float get_time(u64 start, u64 end, u64 freq) {
    return cast(float, end - start) / freq;
}

// Error Reporting
ArgumentsError   := 1; #const
ParsingError     := 2; #const
CompilationError := 3; #const
BuildError       := 4; #const
LinkError        := 5; #const

struct Error {
    message: string;
    file_index := -1;
    line: u32;
    column: u32;
}

errors: Array<Error>;

report_error(string message_format) {
    report_error(message_format, -1, 0, 0);
}

report_error(string message, int file_index, Token token) {
    report_error(message, file_index, token.line, token.column);
}

report_error(string message, Ast* ast) {
    if ast == null report_error(message, -1, 0, 0);
    else report_error(message, ast.file_index, ast.line, ast.column);
}

report_error(string message, int file_index, u32 line, u32 column) {
    error: Error = { message = message; file_index = file_index; line = line; column = column; }
    array_insert(&errors, error);
}

list_errors_and_exit(int errorCode) {
    if errors.length == 0 return;

    print("% compilation error(s):\n", errors.length);

    each error in errors {
        if error.file_index >= 0 print("%: % at line %:%", file_names[error.file_index], error.message, error.line, error.column);
        else print(error.message);
    }

    exit_program(errorCode);
}

#run {
    set_executable_name("ol");

    // if os != OS.Windows {
    //     set_linker(LinkerType.Dynamic);
    // }
}
