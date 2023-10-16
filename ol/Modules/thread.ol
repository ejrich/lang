// Module for sync objects and handling threads

u64 create_thread(ThreadProcedure proc, void* arg) {
    thread_id: u64;
    #if os == OS.Linux {
        pthread_create(&thread_id, null, proc, arg);
    }
    #if os == OS.Windows {
        CreateThread(null, 0, proc, arg, 0, &thread_id);
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
        semaphore.handle = CreateSemaphore(null, initial_value, allowed, null);
        success = semaphore.handle != null;
    }

    return success;
}

semaphore_wait(Semaphore* semaphore) {
    #if os == OS.Linux {
        sem_wait(&semaphore.sem);
    }
    #if os == OS.Windows {
        WaitForSingleObject(&semaphore.handle, INFINITE);
    }
}

semaphore_release(Semaphore* semaphore) {
    #if os == OS.Linux {
        sem_post(&semaphore.sem);
    }
    #if os == OS.Windows {
        ReleaseSemaphore(&semaphore.handle, 1, null);
    }
}
