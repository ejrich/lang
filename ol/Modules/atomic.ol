// This module contains atomic operations like compare exchange and increment

T compare_exchange<T>(T* pointer, T value, T comparand) {
    original := *pointer;

    // TODO Use asm
    if original == comparand {
        *pointer = value;
    }

    return original;
}

T atomic_increment<T>(T* pointer) {
    #assert type_of(T).type == TypeKind.Integer;

    // TODO Use asm
    *pointer = *pointer + 1;

    return *pointer;
}
