struct HashTable<T, U> {
    data: Array<HashTableEntry<T, U>>;
}

struct HashTableEntry<T, U> {
    key: T;
    value: U;
}

// TODO Add hash table init, get, add, update, etc...
