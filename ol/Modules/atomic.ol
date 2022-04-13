// This module contains atomic operations like compare exchange and increment

T compare_exchange<T>(T* pointer, T value, T comparand) {
    #assert type_of(T).type == TypeKind.Integer || type_of(T).type == TypeKind.Pointer;

    result: T;
    #if size_of(T) == 1 {
        asm {
            in rax, comparand;
            in rcx, pointer;
            in rdx, value;
            lock;
            cmpxchg [rcx], dl;
            out result, rax;
        }
    }
    else #if size_of(T) == 2 {
        asm {
            in rax, comparand;
            in rcx, pointer;
            in rdx, value;
            lock;
            cmpxchg [rcx], dx;
            out result, rax;
        }
    }
    else #if size_of(T) == 4 {
        asm {
            in rax, comparand;
            in rcx, pointer;
            in rdx, value;
            lock;
            cmpxchg [rcx], edx;
            out result, rax;
        }
    }
    else {
        asm {
            in rax, comparand;
            in rcx, pointer;
            in rdx, value;
            lock;
            cmpxchg [rcx], rdx;
            out result, rax;
        }
    }

    return result;
}

T atomic_increment<T>(T* pointer) {
    #assert type_of(T).type == TypeKind.Integer;

    #if size_of(T) == 1 {
        asm {
            in rax, pointer;
            mov rcx, 1;
            lock;
            xadd [rax], cl;
        }
    }
    else #if size_of(T) == 2 {
        asm {
            in rax, pointer;
            mov rcx, 1;
            lock;
            xadd [rax], cx;
        }
    }
    else #if size_of(T) == 4 {
        asm {
            in rax, pointer;
            mov rcx, 1;
            lock;
            xadd [rax], ecx;
        }
    }
    else {
        asm {
            in rax, pointer;
            mov rcx, 1;
            lock;
            xadd [rax], rcx;
        }
    }

    return *pointer;
}
