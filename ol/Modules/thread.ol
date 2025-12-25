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
            in rax, 435;
            in r8, &handler_args;
            syscall;

            // Set arguments for clone_handler
            mov rdi, rax;
            mov rsi, r8;
        }

        clone_handler();
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

#private

#if os == OS.Linux {
    struct CloneArguments {
        command: string;
        procedure: ThreadProcedure;
        arg: void*;
    }

    int clone_handler() {
        pid: int;
        arguments_pointer: CloneArguments*;

        asm {
            out pid, edi;
            out arguments_pointer, rsi;
        }

        arguments := *arguments_pointer;

        // Return the pid to the child process
        if pid != 0 return pid;

        if arguments.procedure != null {
            arguments.procedure(arguments.arg);
        }
        else if !string_is_empty(arguments.command) {
            exec_args: Array<u8*>[5];
            exec_args[0] = "sh".data;
            exec_args[1] = "-c".data;
            exec_args[2] = "--".data;
            exec_args[3] = arguments.command.data;
            exec_args[4] = null;
            execve("/bin/sh".data, exec_args.data, __environment_variables_pointer);
        }

        exit(0);
        return 0;
    }
}
