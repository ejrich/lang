#import file
#import "parser.ol"

main() {
    if command_line_arguments.length != 3 {
        printf("Please provide an input file, library name, and output file\n");
        exit_code = 1;
        return;
    }

    found, file_contents := read_file(command_line_arguments[0], allocate);
    if found {
        printf("Parsing file '%s', size %d\n", command_line_arguments[0], file_contents.length);

        parse(file_contents, command_line_arguments[1], command_line_arguments[2]);

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


struct HashTableRecord<T> {
    key: string;
    value: T;
}

// TODO Get this to work with the HashTable struct array lengths
hash_table_entries := 1000; #const

struct HashTable<T> {
    entries: Array<HashTableRecord<T>*>[1000];
    overflow: Array<LinkedList<HashTableRecord<T>*>>[1000];
    count: int;
}

int hash_key(string key) {
    sum: int;
    each i in 0..key.length-1 {
        sum += key[i];
    }
    return sum % hash_table_entries;
}

insert<T>(HashTable<T>* table, string key, T value) {
    record := new<HashTableRecord<T>>();
    record.key = key;
    record.value = value;

    hash := hash_key(key);
    entry := table.entries[hash];

    if entry {
        if entry.key == key {
            table.entries[hash] = record;
        }
        else {
            node := table.overflow[hash].head;
            while node {
                if node.data.key == key {
                    node.data = record;
                    return;
                }
                node = node.next;
            }
            add(&table.overflow[hash], record);
        }
    }
    else {
        table.entries[hash] = record;
        table.count++;
    }
}

bool, T search<T>(HashTable<T>* table, string key) {
    hash := hash_key(key);
    entry := table.entries[hash];

    if entry {
        if entry.key == key {
            return true, entry.value;
        }
        else {
            node := table.overflow[hash].head;
            while node {
                if node.data.key == key {
                    return true, node.data.value;
                }
                node = node.next;
            }
        }
    }

    default: T;
    return false, default;
}

T* new<T>() {
    size := size_of(T);
    pointer := allocate(size);
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

memset(void* ptr, int value, int num) #extern "c"
