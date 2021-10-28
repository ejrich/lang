#import "parser.ol"

main() {
    if command_line_arguments.length != 2 {
        printf("Please provide an input file and the library name\n");
        exit_code = 1;
        return;
    }

    file := fopen(command_line_arguments[0], "rb");
    if file {
        fseek(file, 0, 2);
        size := ftell(file);
        fseek(file, 0, 0);

        printf("Parsing file '%s', size %d\n", command_line_arguments[0], size);

        file_contents: string = {length = size; data = allocate(size);}

        fread(file_contents.data, 1, size, file);
        fclose(file);

        parse(file_contents, command_line_arguments[1]);

        each arena in arenas
            free(arena.pointer);
    }
    else {
        printf("Input file '%s' not found\n", command_line_arguments[0]);
        exit_code = 2;
    }
}

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
    node.next = null;

    if list.head == null {
        list.head = node;
        list.end  = node;
    }
    else {
        list.end.next = node;
        list.end = node;
    }
}

T* new<T>() {
    pointer := allocate(size_of(T));
    return cast(T*, pointer);
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

void* allocate_arena(int cursor, int size = arena_size) {
    arena: Arena = {pointer = malloc(size); cursor = cursor; size = size;}
    memset(arena.pointer, 0, size);
    array_insert(&arenas, arena);
    return arena.pointer;
}

struct FILE {}

FILE* fopen(string file, string type) #extern "c"
int fseek(FILE* file, s64 offset, int origin) #extern "c"
s64 ftell(FILE* file) #extern "c"
int fread(void* buffer, u32 size, u32 length, FILE* file) #extern "c"
int fclose(FILE* file) #extern "c"
memset(void* ptr, int value, int num) #extern "c"
