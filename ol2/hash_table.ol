// Hash table
struct HashTable<T, U> {
    length: int;
    load_factor := 0.75;
    initialized := false;
    entries: Array<HashTableEntry<T, U>>;
}

struct HashTableEntry<T, U> {
    filled: bool;
    hash: u32;
    key: T;
    value: U;
}

HashTable<T, U>* table_create<T, U>(int capacity = 10) {
    table := new<HashTable<T, U>>();
    table_init(table, capacity);

    return table;
}

table_init<T, U>(HashTable<T, U>* table, int capacity, float load_factor = 0.75) {
    if table.initialized return;

    table.load_factor = load_factor;
    array_resize(&table.entries, capacity, allocate, reallocate);
    table.initialized = true;
}

bool, U table_get<T, U>(HashTable<T, U> table, T key) {
    hash := hash(key);
    capacity := table.entries.length;
    index := hash % capacity;

    entry := table.entries[index];
    while entry.filled {
        if entry.key == key return true, entry.value;

        index++;
        if index >= capacity index = 0;

        entry = table.entries[index];
    }

    value: U;
    return false, value;
}

bool table_add<T, U>(HashTable<T, U>* table, T key, U value) {
    if !table.initialized
        table_init(table, 10);
    else if cast(float, table.length) / table.entries.length > table.load_factor
        table_expand(table);

    entry: HashTableEntry<T, U> = { filled = true; hash = hash(key); key = key; value = value; }
    added := table_set(table.entries, entry);

    if added table.length++;
    return added;
}

bool table_contains<T, U>(HashTable<T, U> table, T key) {
    found, _ := table_get(table, key);
    return found;
}

#if build_env == BuildEnv.Debug {
    print_table<T, U>(HashTable<T, U> table, string name) {
        print("Hash table % contains % items\n", name, table.length);
        i := 0;
        each entry in table.entries {
            if entry.filled {
                print("Entry % = %\n", i++, entry);
            }
        }
        print("\n");
    }
}


// Hash set
// These are assumed to be intialized with the full size and do not expand
struct HashSet<T> {
    length: int;
    entries: Array<HashSetEntry<T>>;
}

struct HashSetEntry<T> {
    filled: bool;
    hash: u32;
    value: T;
}

HashSet<T> create_temp_set<T>(int capacity) #inline {
    entries: Array<HashSetEntry<T>>[capacity * 2];
    set: HashSet<T> = { entries = entries; }
    return set;
}

bool set_add<T>(HashSet<T>* set, T value) {
    hash := hash(value);
    entry: HashSetEntry<T> = { filled = true; hash = hash; value = value; }

    entries := set.entries.length;
    index := cast(u64, entry.hash) % entries;
    assert(index >= 0);

    candidate_entry := set.entries[index];
    while candidate_entry.filled {
        if value == candidate_entry.value {
            return false;
        }

        // @Cleanup Make sure this doesn't infinitely loop
        index++;
        if index >= entries index = 0;

        candidate_entry = set.entries[index];
    }

    set.length++;
    set.entries[index] = entry;
    return true;
}

#private

table_expand<T, U>(HashTable<T, U>* table) {
    new_entries: Array<HashTableEntry<T, U>>;
    length := table.entries.length * 2;
    if length <= 0 length = 10;
    array_resize(&new_entries, length, allocate, reallocate);

    each entry in table.entries {
        if entry.filled {
            table_set(new_entries, entry);
        }
    }

    table.entries = new_entries;
}

bool table_set<T, U>(Array<HashTableEntry<T, U>> entries, HashTableEntry<T, U> entry) {
    index := cast(u64, entry.hash) % entries.length;
    assert(index >= 0);

    candidate_entry := entries[index];
    while candidate_entry.filled {
        if entry.key == candidate_entry.key {
            entries[index].value = entry.value;
            return false;
        }

        index++;
        if index >= entries.length index = 0;

        candidate_entry = entries[index];
    }

    entries[index] = entry;
    return true;
}

fnv_offset: u32 = 0x811C9DC5; #const
fnv_prime : u32 = 0x01000193; #const

u32 hash<T>(T value) {
    #if T == string {
        hash := fnv_offset;

        each i in 0..value.length - 1 {
            hash ^= value[i];
            hash *= fnv_prime;
        }

        return hash;
    }
    else #if type_of(T).type_kind == TypeKind.Integer {
        return value;
    }
    else {
        return 0;
    }
}
