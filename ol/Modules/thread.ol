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
