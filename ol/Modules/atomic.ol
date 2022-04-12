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

    #if size_of(T) == 1 {
        asm {
            in rax, pointer;
            mov rcx, 1;
            xadd [rax], cl;
        }
    }
    else #if size_of(T) == 2 {
        asm {
            in rax, pointer;
            mov rcx, 1;
            xadd [rax], cx;
        }
    }
    else #if size_of(T) == 4 {
        asm {
            in rax, pointer;
            mov rcx, 1;
            xadd [rax], ecx;
        }
    }
    else {
        asm {
            in rax, pointer;
            mov rcx, 1;
            xadd [rax], rcx;
        }
    }

    return *pointer;
}
