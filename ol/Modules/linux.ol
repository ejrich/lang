// This module contains functions and types only available when compiling and running programs in Linux

#assert os == OS.Linux;

// Linux system calls

s64 read(int fd, u8* buf, u64 count) #syscall 0
s64 write(int fd, u8* buf, u64 count) #syscall 1
int open(u8* pathname, OpenFlags flags, OpenMode mode) #syscall 2
int close(int fd) #syscall 3
int stat(u8* path, Stat* buf) #syscall 4
int fstat(int fd, Stat* buf) #syscall 5
int lstat(int fd, Stat* buf) #syscall 6
int poll(PollFd* ufds, u32 nfds, int timeout) #syscall 7
s64 lseek(int fd, u64 offset, Whence whence) #syscall 8
void* mmap(void* addr, u64 length, Prot prot, MmapFlags flags, int fd, u64 offset) #syscall 9
int mprotect(void* addr, u64 length, Prot prot) #syscall 10
int munmap(void* addr, u64 length) #syscall 11
int brk(void* addr) #syscall 12
int rt_sigaction(int sig, Sigaction* act, Sigaction* oact, u64 sigsetsize) #syscall 13
int rt_sigprocmask(Sighow how, Sigset_T* set, Sigset_T* oset, u64 sigsetsize) #syscall 14
int rt_sigreturn() #syscall 15
int ioctl(int fd, u32 cmd, u64 arg) #syscall 16
int pread64(int fd, u8* buf, u64 count, u64 pos) #syscall 17
int pwrite64(int fd, u8* buf, u64 count, u64 pos) #syscall 18
int readv(int fd, Iovec* vec, u64 vlen) #syscall 19
int writev(int fd, Iovec* vec, u64 vlen) #syscall 20
int access(u8* filename, int mode) #syscall 21
int pipe(int* fildes) #syscall 22
int select(int nfds, Fd_Set* inp, Fd_Set outp, Fd_Set* exp, Timeval* tvp) #syscall 23
sched_yield() #syscall 24
void* mremap(void* old_address, u64 old_size, u64 new_size, MremapFlags flags) #syscall 25
int dup(int oldfd) #syscall 32
int dup2(int oldfd, int newfd) #syscall 32
int pause() #syscall 34
int nanosleep(Timespec* req, Timespec* rem) #syscall 35
int getpid() #syscall 39
int clone(CloneFlags clone_flags, void* newsp, int* parent_tidptr, int* child_tidptr, u64 tls) #syscall 56
int fork() #syscall 57
int vfork() #syscall 58
int execve(u8* pathname, u8** argv, u8** envp) #syscall 59
exit(int status) #syscall 60
int wait4(int pid, int* status, int options, void* rusage) #syscall 61
int kill(int pid, int sig) #syscall 62
u8* getcwd(u8* buf, u64 size) #syscall 79
int chdir(u8* path) #syscall 80
int fchdir(int fd) #syscall 81
int rename(u8* oldname, u8* newname) #syscall 82
int mkdir(u8* pathname, int mode) #syscall 83
int unlink(u8* path) #syscall 87
int readlink(u8* path, u8* buf, int bufsize) #syscall 89
int futex(u32* uaddr, FutexOperation op, u32 val, Timespec* timeout, u32* uaddr2, u32 val3) #syscall 202
int getdents64(int fd, Dirent* dirp, u32 count) #syscall 217
int clock_gettime(ClockId clk_id, Timespec* tp) #syscall 228
exit_group(int status) #syscall 231
int pipe2(int* pipefd, int flags) #syscall 293
int getcpu(u32* cpu, u32* node) #syscall 309
s64 getrandom(void* buf, u64 buflen, RandomFlags flags) #syscall 318
int clone3(clone_args* cl_args, u64 size) #syscall 435
// @Future Add additional syscalls when necessary

stdin  := 0; #const
stdout := 1; #const
stderr := 2; #const

enum OpenFlags {
    O_RDONLY    = 0x000000;
    O_WRONLY    = 0x000001;
    O_RDWR      = 0x000002;
    O_ACCMODE   = 0x000003;
    O_CREAT     = 0x000040;
    O_EXCL      = 0x000080;
    O_NOCTTY    = 0x000100;
    O_TRUNC     = 0x000200;
    O_APPEND    = 0x000400;
    O_NONBLOCK  = 0x000800;
    O_DSYNC     = 0x001000;
    O_ASYNC     = 0x002000;
    O_DIRECT    = 0x004000;
    O_LARGEFILE = 0x008000;
    O_DIRECTORY = 0x010000;
    O_NOFOLLOW  = 0x020000;
    O_NOATIME   = 0x040000;
    O_CLOEXEC   = 0x080000;
    O_SYNC      = 0x101000;
    O_PATH      = 0x200000;
    O_TMPFILE   = 0x410000;
}

enum OpenMode {
    S_IXOTH = 0x1;
    S_IWOTH = 0x2;
    S_IROTH = 0x4;
    S_IRWXO = 0x7;
    S_IXGRP = 0x8;
    S_IWGRP = 0x10;
    S_IRGRP = 0x20;
    S_IRWXG = 0x38;
    S_IXUSR = 0x40;
    S_IWUSR = 0x80;
    S_IRUSR = 0x100;
    S_IRWXU = 0x1C0;
    S_RWALL = 0x1B6;
}

struct Stat {
    st_dev: u64;
    st_ino: u64;
    st_nlink: u64;
    st_mode: int;
    st_uid: int;
    st_gid: int;
    __pad0: int;
    st_rdev: u64;
    st_size: u64;
    st_blksize: u64;
    st_blocks: u64;
    st_atim: Timespec;
    st_mtim: Timespec;
    st_ctim: Timespec;
    __reserved: CArray<u64>[3];
}

struct PollFd {
    // @Incomplete Add fields before using
}

enum Whence {
    SEEK_SET;
    SEEK_CUR;
    SEEK_END;
}

enum Prot {
    PROT_NONE = 0;
    PROT_READ = 1;
    PROT_WRITE = 2;
    PROT_EXEC = 4;
}

enum MmapFlags {
    MAP_SHARED          = 0x1;
    MAP_PRIVATE         = 0x2;
    MAP_FIXED           = 0x10;
    MAP_ANONYMOUS       = 0x20;
    MAP_HUGE_SHIFT      = 0x1A;
    MAP_HUGE_MASK       = 0x3f;
    MAP_GROWSDOWN       = 0x100;
    MAP_DENYWRITE       = 0x800;
    MAP_EXECUTABLE      = 0x1000;
    MAP_LOCKED          = 0x2000;
    MAP_NORESERVE       = 0x4000;
    MAP_POPULATE        = 0x8000;
    MAP_NONBLOCK        = 0x10000;
    MAP_STACK           = 0x20000;
    MAP_HUGETLB         = 0x40000;
    MAP_SYNC            = 0x80000;
    MAP_FIXED_NOREPLACE = 0x100000;
}

struct Sigaction {
    // @Incomplete Add fields before using
}

struct Sigset_T {
    // @Incomplete Add fields before using
}

enum Sighow {
    SIG_BLOCK;
    SIG_UNBLOCK;
    SIG_SETMASK;
}

struct Iovec {
    // @Incomplete Add fields before using
}

struct Fd_Set {
    // @Incomplete Add fields before using
}

struct Timeval {
    tv_sec: u64;
    tv_usec: u64;
}

enum MremapFlags {
    MREMAP_MAYMOVE   = 1;
    MREMAP_FIXED     = 2;
    MREMAP_DONTUNMAP = 4;
}

enum FutexOperation {
    FUTEX_WAIT            = 0;
    FUTEX_WAKE            = 1;
    FUTEX_REQUEUE         = 3;
    FUTEX_CMP_REQUEUE     = 4;
    FUTEX_WAKE_OP         = 5;
    FUTEX_LOCK_PI         = 6;
    FUTEX_UNLOCK_PI       = 7;
    FUTEX_TRYLOCK_PI      = 8;
    FUTEX_WAIT_BITSET     = 9;
    FUTEX_WAKE_BITSET     = 10;
    FUTEX_WAIT_REQUEUE_PI = 11;
    FUTEX_CMP_REQUEUE_PI  = 12;
    FUTEX_LOCK_PI2        = 13;
    FUTEX_PRIVATE_FLAG    = 128;
    FUTEX_CLOCK_REALTIME  = 256;
}

struct Dirent {
    d_ino: u64;
    d_off: u64;
    d_reclen: u16;
    d_type: DirentType;
    d_name: CArray<u8>[256];
}

enum DirentType : u8 {
    DT_UNKNOWN = 0;
    DT_FIFO = 1;
    DT_CHR = 2;
    DT_DIR = 4;
    DT_BLK = 6;
    DT_REG = 8;
    DT_LNK = 10;
    DT_SOCK = 12;
    DT_WHT = 14;
}

struct Timespec {
    tv_sec: u64;
    tv_nsec: u64;
}

enum ClockId {
    CLOCK_REALTIME;
    CLOCK_MONOTONIC;
    CLOCK_PROCESS_CPUTIME_ID;
    CLOCK_THREAD_CPUTIME_ID;
    CLOCK_MONOTONIC_RAW;
    CLOCK_REALTIME_COARSE;
    CLOCK_MONOTONIC_COARSE;
    CLOCK_BOOTTIME;
    CLOCK_REALTIME_ALARM;
    CLOCK_BOOTTIME_ALARM;
}

enum RandomFlags {
    GRND_NONBLOCK = 1;
    GRND_RANDOM   = 2;
    GRND_INSECURE = 4;
}

struct tm {
    tm_sec: int;
    tm_min: int;
    tm_hour: int;
    tm_mday: int;
    tm_mon: int;
    tm_year: int;
    tm_wday: int;
    tm_yday: int;
    tm_isdst: int;
}

tm* localtime(u64* timer) #extern "c"

[flags]
enum CloneFlags : u64 {
    CLONE_VM             = 0x000000100;
    CLONE_FS             = 0x000000200;
    CLONE_FILES          = 0x000000400;
    CLONE_SIGHAND        = 0x000000800;
    CLONE_PIDFD          = 0x000001000;
    CLONE_PTRACE         = 0x000002000;
    CLONE_VFORK          = 0x000004000;
    CLONE_PARENT         = 0x000008000;
    CLONE_THREAD         = 0x000010000;
    CLONE_NEWNS          = 0x000020000;
    CLONE_SYSVSEM        = 0x000040000;
    CLONE_SETTLS         = 0x000080000;
    CLONE_PARENT_SETTID  = 0x000100000;
    CLONE_CHILD_CLEARTID = 0x000200000;
    CLONE_DETACHED       = 0x000400000;
    CLONE_UNTRACED       = 0x000800000;
    CLONE_CHILD_SETTID   = 0x001000000;
    CLONE_NEWCGROUP      = 0x002000000;
    CLONE_NEWUTS         = 0x004000000;
    CLONE_NEWIPC         = 0x008000000;
    CLONE_NEWUSER        = 0x010000000;
    CLONE_NEWPID         = 0x020000000;
    CLONE_NEWNET         = 0x040000000;
    CLONE_IO             = 0x008000000;
    CLONE_CLEAR_SIGHAND  = 0x100000000;
}

struct clone_args {
    flags: CloneFlags;
    pidfd: int*;
    child_tid: int*;
    parent_tid: int*;
    exit_signal: u64;
    stack: void*;
    stack_size: u64;
    tls: u64;
    set_tid: int*;
    set_tid_size: u64;
    cgroup: int*;
}

home_environment_variable := "HOME"; #const

struct CloneArguments {
    command: string;
    pipes: int*;
    procedure: ThreadProcedure;
    arg: void*;
}

int __clone_handler() {
    read_pipe := 0; #const
    write_pipe := 1; #const

    pid: int;
    arguments_pointer: CloneArguments*;

    asm {
        out pid, edi;
        out arguments_pointer, rsi;
    }

    arguments := *arguments_pointer;

    // Return the pid to the child process
    if pid != 0 {
        if pid > 0 && arguments.pipes != null {
            close(arguments.pipes[write_pipe]);
        }
        return pid;
    }

    if arguments.procedure != null {
        arguments.procedure(arguments.arg);
    }
    else if !string_is_empty(arguments.command) {
        if arguments.pipes {
            close(arguments.pipes[read_pipe]);
            dup2(arguments.pipes[write_pipe], stdout);
        }

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
