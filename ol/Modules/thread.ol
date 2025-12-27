// Module for sync objects and handling threads

#if os == OS.Linux {
    #import atomic
}

u64 create_thread(ThreadProcedure proc, void* arg) {
    thread_id: u64;
    #if os == OS.Linux {
        pthread_create(&thread_id, null, proc, arg);
    }
    #if os == OS.Windows {
        handle := CreateThread(null, 0, proc, arg, 0, null);
        thread_id = cast(u64, handle);
    }

    return thread_id;
}

#if os == OS.Linux {
    struct Semaphore {
        value: u32;
        futex: u32;
        // sem: sem_t;
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
        semaphore.value = initial_value;
        success = true;
    }
    #if os == OS.Windows {
        semaphore.handle = CreateSemaphoreA(null, initial_value, allowed, null);
        success = semaphore.handle != null;
    }

    return success;
}

semaphore_wait(Semaphore* semaphore) {
    #if os == OS.Linux {
        while true {
            value := semaphore.value;
            if value > 0 && compare_exchange(&semaphore.value, value - 1, value) == value {
                break;
            }

            futex(&semaphore.futex, FutexOperation.FUTEX_WAIT, 0, null, null, 0);
        }
    }
    #if os == OS.Windows {
        WaitForSingleObject(semaphore.handle, INFINITE);
    }
}

semaphore_release(Semaphore* semaphore) {
    #if os == OS.Linux {
        atomic_increment(&semaphore.value);
        futex(&semaphore.futex, FutexOperation.FUTEX_WAKE, 1, null, null, 0);
    }
    #if os == OS.Windows {
        ReleaseSemaphore(semaphore.handle, 1, null);
    }
}
