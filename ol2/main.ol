#import atomic
#import standard
#import thread
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
                report_error_message("Unrecognized compiler flag '%'", arg);
            }
            else if !string_is_empty(entrypoint) {
                report_error_message("Multiple program entrypoints defined '%'", arg);
            }
            else {
                if !file_exists(arg) || !ends_with(arg, ".ol") {
                    report_error_message("Entrypoint file does not exist or is not an .ol file '%'", arg);
                }
                else {
                    // name = Path.GetFileNameWithoutExtension(arg);
                    entrypoint = arg; // Path.GetFullPath(arg);
                    // path = Path.GetDirectoryName(entrypoint);
                }
            }
        }
    }

    if string_is_empty(entrypoint) report_error_message("Program entrypoint not defined");
    list_errors_and_exit(ArgumentsError);

    // Parse source files to asts
    if !noThreads init_thread_pool();
    init_types();
    parse(entrypoint);
    list_errors_and_exit(ParsingError);

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


// Multithreading
init_thread_pool() {
    create_semaphore(&semaphore, 65536);
    thread_count := get_processors();
    each i in 0..thread_count - 2 {
        create_thread(thread_worker, null);
    }
}

void* thread_worker(void* arg) {
    while true {
        if execute_queued_item() {
            semaphore_wait(semaphore);
        }
    }
    return null;
}

semaphore: Semaphore*;
thread_queue: LinkedList<QueueItem>;
completed := 0;
count := 0;

queue_work(Callback callback, void* data) {
    item: QueueItem = { callback = callback; data = data; }
    add_to_head(&thread_queue, item);
    atomic_increment(&count);
    semaphore_release(semaphore);
}

bool execute_queued_item() {
    head := thread_queue.head;
    if head == null return true;

    value := compare_exchange(&thread_queue.head, head.next, head);

    if value == head {
        queue_item := head.data;
        queue_item.callback(queue_item.data);
        atomic_increment(&completed);
    }

    return false;
}

complete_work() {
    while completed < count
        execute_queued_item();

    completed = 0;
    count = 0;
}

struct QueueItem {
    callback: Callback;
    data: void*;
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


// Memory allocation
T* new<T>() {
    size := size_of(T);
    return allocate(size);
}

arena_size := 80000; #const
arenas: Array<Arena>;

struct Arena {
    pointer: void*;
    cursor: int;
    size: int;
}

void* allocate(int size) {
    if size > arena_size
        return allocate_arena(size, size);

    each arena in arenas {
        if size <= arena.size - arena.cursor {
            pointer := arena.pointer + arena.cursor;
            arena.cursor += size;
            return pointer;
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

void* allocate_arena(int cursor, int size = arena_size) {
    arena: Arena = { pointer = allocate_memory(size); cursor = cursor; size = size; }
    array_insert(&arenas, arena);
    return arena.pointer;
}

deallocate_arenas() {
    each arena in arenas {
        free_memory(arena.pointer, arena.size);
    }
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

    if os != OS.Windows {
        set_linker(LinkerType.Dynamic);
    }
}
