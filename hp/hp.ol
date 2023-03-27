#import standard
#import "parser.ol"

main() {
    args := get_command_line_arguments();
    if args.length != 3 {
        print("Please provide an input file, library name, and output file\n");
        set_exit_code(1);
        return;
    }

    found, file_contents := read_file(args[0], allocate);
    if found {
        print("Parsing file '%', size %\n", args[0], file_contents.length);

        parse(file_contents, args[1], args[2]);

        each arena in arenas
            default_free(arena.pointer);
    }
    else {
        print("Input file '%' not found\n", args[0]);
        set_exit_code(2);
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

hash_table_entries := 1000; #const

struct HashTable<T> {
    entries: Array<HashTableRecord<T>*>[hash_table_entries];
    overflow: Array<LinkedList<HashTableRecord<T>*>>[hash_table_entries];
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

    _: T;
    return false, _;
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

void* allocate(u64 size) {
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
    arena: Arena = {pointer = default_allocator(size); cursor = cursor; size = size;}
    array_insert(&arenas, arena);
    return arena.pointer;
}
