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
int lseek(int fd, u64 offset, Whence whence) #syscall 8
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
int pause() #syscall 34
int nanosleep(Timespec* req, Timespec* rem) #syscall 35
exit(int status) #syscall 60
int clock_gettime(ClockId clk_id, Timespec* tp) #syscall 228
exit_group(int status) #syscall 231
// TODO Add additional syscalls when necessary

stdin  := 0; #const
stdout := 1; #const
stderr := 2; #const

enum OpenFlags {
    O_RDONLY    = 00000000;
    O_WRONLY    = 00000001;
    O_RDWR      = 00000002;
    O_ACCMODE   = 00000003;
    O_CREAT     = 00000100;
    O_EXCL      = 00000200;
    O_NOCTTY    = 00000400;
    O_TRUNC     = 00001000;
    O_APPEND    = 00002000;
    O_NONBLOCK  = 00004000;
    O_DSYNC     = 00010000;
    O_ASYNC     = 00020000;
    O_DIRECT    = 00040000;
    O_LARGEFILE = 00100000;
    O_DIRECTORY = 00200000;
    O_NOFOLLOW  = 00400000;
    O_NOATIME   = 01000000;
    O_CLOEXEC   = 02000000;
    O_SYNC      = 04010000;
    O_PATH      = 10000000;
    O_TMPFILE   = 20200000;
}

enum OpenMode {
    S_IXOTH = 1;
    S_IWOTH = 2;
    S_IROTH = 4;
    S_IRWXO = 7;
    S_IXGRP = 10;
    S_IWGRP = 20;
    S_IRGRP = 40;
    S_IRWXG = 70;
    S_IXUSR = 100;
    S_IWUSR = 200;
    S_IRUSR = 400;
    S_IRWXU = 700;
    S_RWALL = 666;
}

struct Stat {
    // @Incomplete Add fields before using
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
    MAP_HUGE_SHIFT      = 26;
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
