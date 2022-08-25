#import atomic
#import standard
#import thread
#import "ast.ol"
#import "link.ol"
#import "llvm_backend.ol"
#import "parser.ol"
#import "type_checker.ol"

release := false;
output_assembly := false;
emit_debug := true;
path: string;
executable_name: string;
output_directory: string;
input_variables: HashTable<string, InputVariable*>;

struct InputVariable {
    token: Token;
    used: bool;
}

linker: LinkerType;
output_type_table: OutputTypeTableConfiguration;

file_paths: Array<string>;
file_names: Array<string>;

main() {
    freq := get_performance_frequency();
    start := get_performance_counter();

    // Load cli args into build settings
    entrypoint: string;
    no_threads, parsing_variables := false;
    each arg in command_line_arguments {
        if arg == "-R" || arg == "--release" release = true;
        else if arg == "-S" output_assembly = true;
        else if arg == "-V" parsing_variables = true;
        else if arg == "-noThreads" no_threads = true;
        else if arg == "-noDebug" emit_debug = false;
        else {
            if arg[0] == '-'
                report_error_message("Unrecognized compiler flag '%'", arg);
            else if parsing_variables {
                initial_allocated_length := 5; #const
                token_list: TokenList = { allocated_length = initial_allocated_length; }
                token_list.tokens.data = allocate(initial_allocated_length * size_of(Token));
                parse_tokens(arg, -1, &token_list);

                if token_list.tokens.length != 3 || token_list.tokens[0].type != TokenType.Identifier || token_list.tokens[1].type != TokenType.Equals || (token_list.tokens[2].type != TokenType.Number && token_list.tokens[2].type != TokenType.Boolean)
                    report_error_message("Malformed input variable '%', should be formatted like 'foo=true'", arg);
                else if !add_input_variable(token_list.tokens[0].value, token_list.tokens[2])
                    report_error_message("Multiple definitions for input variable '%'", arg);
            }
            else if !string_is_empty(entrypoint)
                report_error_message("Multiple program entrypoints defined '%'", arg);
            else {
                if !file_exists(arg) || !ends_with(arg, ".ol") {
                    report_error_message("Entrypoint file does not exist or is not an .ol file '%'", arg);
                }
                else {
                    executable_name = name_without_extension(arg);
                    entrypoint = get_full_path(arg);
                    path = get_directory(entrypoint);
                }
            }
        }
    }

    if string_is_empty(entrypoint) report_error_message("Program entrypoint not defined");
    list_errors_and_exit(ArgumentsError);

    // Initialize subsystems
    if !no_threads init_thread_pool();
    init_types();

    // Parse what is available
    parse(entrypoint);
    list_errors_and_exit(ParsingError);

    // Verify necessary asts for compiler directives
    init_necessary_types();
    verify_compiler_directives();

    // Handle messages
    // Check types and build the program ir
    check_types();
    list_errors_and_exit(CompilationError);
    complete_work();
    front_end_time := get_performance_counter();

    // Build program
    object_file := llvm_build();
    build_time := get_performance_counter();

    // Link binaries
    link(object_file);
    link_time := get_performance_counter();

    // Log statistics
    print("Front-end time: % seconds\nLLVM build time: % seconds\nLinking time: % seconds\n", get_time(start, front_end_time, freq), get_time(front_end_time, build_time, freq), get_time(build_time, link_time, freq));
    deallocate_arenas();
}

bool add_input_variable(string name, Token value) {
    input_variable := new<InputVariable>();
    input_variable.token = value;
    return table_add(&input_variables, name, input_variable);
}

bool ends_with(string value, string ending) {
    if ending.length > value.length return false;

    start_index := value.length - ending.length;
    each i in 0..ending.length - 1 {
        if value[start_index + i] != ending[i] return false;
    }

    return true;
}

string name_with_extension(string file) {
    length := 0;
    each i in 0..file.length - 1 {
        if file[i] == '/' length = i;
    }

    if length return substring(file, length + 1, file.length - length - 1);
    return file;
}

string name_without_extension(string file) {
    length := 0;
    extension_index := 0;
    each i in 0..file.length - 1 {
        switch file[i] {
            case '/'; length = i;
            case '.'; extension_index = i;
        }
    }

    if length >= extension_index return substring(file, length, file.length - length);
    return substring(file, length + 1, file.length - extension_index + 1);
}

PATH_MAX := 4096; #const
string get_full_path(string path) {
    null_terminated_path: Array<u8>[path.length + 1];
    memory_copy(null_terminated_path.data, path.data, path.length);
    null_terminated_path[path.length] = 0;

    full_path: CArray<u8>[PATH_MAX];
    result: string;
    #if os == OS.Linux {
        path_pointer := realpath(null_terminated_path.data, &full_path);
        if path_pointer {
            each char, i in full_path {
                if char == 0 {
                    result.length = i;
                    break;
                }
            }
        }
        else return path;
    }
    #if os == OS.Windows {
        result.length = GetFullPathNameA(path, PATH_MAX, &full_path, null);
        if result.length == 0 return path;
    }

    result.data = allocate(result.length);
    memory_copy(result.data, &full_path, result.length);

    return result;
}

string get_directory(string path) {
    length := 0;
    each i in 0..path.length - 1 {
        if path[i] == '/' length = i;
    }

    return substring(path, 0, length);
}

float get_time(u64 start, u64 end, u64 freq) {
    return cast(float, end - start) / freq;
}


// Multithreading
init_thread_pool() {
    create_semaphore(&queue_semaphore, 65536);
    thread_count := get_processors();
    each i in 0..thread_count - 2 {
        create_thread(thread_worker, null);
    }
}

void* thread_worker(void* arg) {
    while true {
        if execute_queued_item() {
            semaphore_wait(&queue_semaphore);
        }
    }
    return null;
}

queue_semaphore: Semaphore;
thread_queue: LinkedList<QueueItem>;
completed := 0;
job_count := 0;

queue_work(Callback callback, void* data, bool clear = false) {
    item: QueueItem = { callback = callback; data = data; clear = clear; }
    add_to_head(&thread_queue, item);
    atomic_increment(&job_count);
    semaphore_release(&queue_semaphore);
}

bool execute_queued_item() {
    head := thread_queue.head;
    if head == null return true;

    value := compare_exchange(&thread_queue.head, head.next, head);

    if value == head {
        queue_item := head.data;

        if queue_item.clear {
            atomic_increment(&completed);
            queue_item.callback(queue_item.data);
        }
        else {
            queue_item.callback(queue_item.data);
            atomic_increment(&completed);
        }
    }

    return false;
}

complete_work() {
    while completed < job_count
        execute_queued_item();

    completed = 0;
    job_count = 0;
}

struct QueueItem {
    callback: Callback;
    data: void*;
    clear: bool;
}

interface Callback(void* data)

struct LinkedList<T> {
    head: Node<T>*;
    end: Node<T>*;
}

struct Node<T> {
    data: T;
    next: Node<T>*;
}

add<T>(LinkedList<T>* list, T data) {
    node := new<Node<T>>();
    node.data = data;

    if list.head {
        originalEnd := replace_end(list, node);
        originalEnd.next = node;
    }
    else {
        node.next = null;
        list.head = node;
        list.end = node;
    }
}

add_to_head<T>(LinkedList<T>* list, T data) {
    node := new<Node<T>>();
    node.data = data;
    node.next = list.head;

    while compare_exchange(&list.head, node, node.next) != node.next {
        node.next = list.head;
    }
}

Node<T>* replace_end<T>(LinkedList<T>* list, Node<T>* node) {
    originalEnd := list.end;

    while compare_exchange(&list.end, node, originalEnd) != originalEnd {
        originalEnd = list.end;
    }

    return originalEnd;
}

remove_node<T>(LinkedList<T>* list, Node<T>* node, Node<T>* previous) {
    if previous == null {
        list.head = node.next;
        if node.next == null replace_end(list, null);
    }
    else if node.next {
        previous.next = node.next;
    }
    else {
        replace_end(list, previous);
        previous.next = null;
    }
}


// Messages
enum CompilerMessageType {
    ReadyToBeTypeChecked = 1;
    TypeCheckFailed;
    TypeCheckSucceeded;
    IRGenerated;
    ReadyForCodeGeneration;
    CodeGenerated;
    ExecutableLinked;
}

struct CompilerMessage {
    type: CompilerMessageType;
    ast: Ast*;
}

message_queue: LinkedList<CompilerMessage>;


// String and Array helpers
string allocate_string(string input) {
    result: string = { length = input.length; data = allocate(input.length); }
    memory_copy(result.data, input.data, input.length);

    return result;
}

Array<T> allocate_array<T>(Array<T> source) {
    array: Array<T>;
    array.length = source.length;
    bytes := source.length * size_of(T);
    array.data = allocate(bytes);
    memory_copy(array.data, source.data, bytes);

    return array;
}


// Memory allocation
T* new<T>() #inline {
    value: T;
    size := size_of(T);
    pointer: T* = allocate(size);
    *pointer = value;

    return pointer;
}

default_arena_size := 80000; #const
arenas: Array<Arena>;

struct Arena {
    pointer: void*;
    cursor: int;
    size: int;
}

void* allocate(int size) {
    if size > default_arena_size
        return allocate_arena(size, size);

    each arena in arenas {
        arena_size := arena.size;
        while true {
            cursor := arena.cursor;
            if size <= arena_size - cursor {
                if compare_exchange(&arena.cursor, cursor + size, cursor) == cursor {
                    pointer := arena.pointer + cursor;
                    return pointer;
                }
            }
            else break;
        }
    }

    return allocate_arena(size);
}

void* reallocate(void* pointer, int old_size, int size) {
    // @Cleanup Write general purpose allocator to replace this
    new_pointer := allocate(size);
    memory_copy(new_pointer, pointer, old_size);
    return new_pointer;
}

void* allocate_arena(int cursor, int size = default_arena_size) {
    arena: Arena = { pointer = allocate_memory(size); cursor = cursor; size = size; }
    array_insert(&arenas, arena);
    return arena.pointer;
}

deallocate_arenas() {
    total_allocated := 0;
    each arena in arenas {
        free_memory(arena.pointer, arena.size);
        total_allocated += arena.size;
    }
    // print("% arenas allocated, % kb memory\n", arenas.length, total_allocated / 1000.0);
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

report_error_message(string message, Params args) {
    report_error(message, -1, 0, 0, args);
}

report_error(string message, int file_index, Token token, Params args) {
    report_error(message, file_index, token.line, token.column, args);
}

report_error(string message, Ast* ast, Params args) {
    if ast == null report_error(message, -1, 0, 0, args);
    else report_error(message, ast.file_index, ast.line, ast.column, args);
}

report_error(string message, int file_index, u32 line, u32 column, Params args) {
    if args.length message = format_string(message, allocate, args);

    error: Error = { message = message; file_index = file_index; line = line; column = column; }
    array_insert(&errors, error, allocate, reallocate);
}

list_errors_and_exit(int errorCode) {
    if errors.length == 0 return;

    print("% compilation error(s):\n\n", errors.length);

    each error in errors {
        if error.file_index >= 0 print("%: % at line %:%\n", file_names[error.file_index], error.message, error.line, error.column);
        else print("%\n", error.message);
    }

    deallocate_arenas();
    exit_program(errorCode);
}

#run {
    set_executable_name("ol");
    set_output_type_table(OutputTypeTableConfiguration.Used);

    if os != OS.Windows {
        set_linker(LinkerType.Dynamic);
    }
}
