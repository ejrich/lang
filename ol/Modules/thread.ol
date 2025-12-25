// Module for sync objects and handling threads

u64 create_thread(ThreadProcedure proc, void* arg, Allocate stack_allocator) {
    thread_id: u64;
    #if os == OS.Linux {
        stack_size := 2 * 1024 * 1024; #const
        stack := stack_allocator(stack_size);
        tid: int;

        args: clone_args = {
            flags = CloneFlags.CLONE_VM | CloneFlags.CLONE_FS | CloneFlags.CLONE_FILES | CloneFlags.CLONE_SYSVSEM | CloneFlags.CLONE_SIGHAND | CloneFlags.CLONE_THREAD | CloneFlags.CLONE_PARENT_SETTID;
            parent_tid = &tid;
            stack = stack;
            stack_size = stack_size;
        }

        handler_args: CloneArguments = {
            procedure = proc;
            arg = arg;
        }

        asm {
            in rdi, &args;
            in rsi, size_of(args);
            in rax, 435; // clone3
            in r8, &handler_args;
            syscall;

            // Set arguments for __clone_handler
            mov rdi, rax;
            mov rsi, r8;
        }

        __clone_handler();
        thread_id = tid;
    }
    #if os == OS.Windows {
        handle := CreateThread(null, 0, proc, arg, 0, null);
        thread_id = cast(u64, handle);
    }

    return thread_id;
}

#if os == OS.Linux {
    struct Semaphore {
        sem: sem_t;
    }
}
#if os == OS.Windows {
    struct Semaphore {
        handle: Handle*;
    }
}

bool create_semaphore(Semaphore* semaphore, int allowed = 1, int initial_value = 0) {
    success: bool;
    #if os == OS.Linux {
        success = sem_init(&semaphore.sem, 0, initial_value) == 0;
    }
    #if os == OS.Windows {
        semaphore.handle = CreateSemaphoreA(null, initial_value, allowed, null);
        success = semaphore.handle != null;
    }

    return success;
}

semaphore_wait(Semaphore* semaphore) {
    #if os == OS.Linux {
        sem_wait(&semaphore.sem);
    }
    #if os == OS.Windows {
        WaitForSingleObject(semaphore.handle, INFINITE);
    }
}

semaphore_release(Semaphore* semaphore) {
    #if os == OS.Linux {
        sem_post(&semaphore.sem);
    }
    #if os == OS.Windows {
        ReleaseSemaphore(semaphore.handle, 1, null);
    }
}
