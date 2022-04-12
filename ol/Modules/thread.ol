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

struct Semaphore {}

bool create_semaphore(Semaphore** semaphore, int allowed = 1) {
    success: bool;
    #if os == OS.Linux {
        success = sem_init(semaphore, 0, allowed) == 0;
    }
    #if os == OS.Windows {
        *semaphore = CreateSemaphore(null, 0, allowed, null);
        success = *semaphore != null;
    }

    return success;
}

semaphore_wait(Semaphore* semaphore) {
    #if os == OS.Linux {
        sem_wait(&semaphore);
    }
    #if os == OS.Windows {
        WaitForSingleObject(semaphore, INFINITE);
    }
}

semaphore_release(Semaphore* semaphore) {
    #if os == OS.Linux {
        sem_post(&semaphore);
    }
    #if os == OS.Windows {
        ReleaseSemaphore(semaphore, 1, null);
    }
}
