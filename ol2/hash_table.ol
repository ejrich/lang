struct HashTable<T, U> {
    data: Array<HashTableEntry<T, U>>;
    length: int;
}

struct HashTableEntry<T, U> {
    filled: bool;
    key: T;
    value: U;
}

// TODO Add hash table init, get, add, update, etc...
table_init<T, U>(HashTable<T, U>* table, int capacity) {
    array_resize(table.data, capacity, allocate, reallocate);
}

bool, U table_get<T, U>(HashTable<T, U> table, T key) {
    value: U;
    return true, value;
}

bool table_add<T, U>(HashTable<T, U>* table, T key, U value) {
    return true;
}

bool table_contains<T, U>(HashTable<T, U> table, T key) {
    return true;
}
