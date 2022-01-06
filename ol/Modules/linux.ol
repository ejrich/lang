// Linux system calls

s64 read(u64 fd, u8* buf, u64 count) #syscall 0
s64 write(u64 fd, u8* buf, u64 count) #syscall 1
int open(u8* pathname, int flags, Mode_T mode) #syscall 2
int close(u64 fd) #syscall 3
int stat(u8* path, Stat* buf) #syscall 4
int fstat(u64 fd, Stat* buf) #syscall 5
int lstat(u64 fd, Stat* buf) #syscall 6
int poll(PollFd* ufds, u32 nfds, int timeout) #syscall 7
int lseek(u64 fd, u64 offset, Whence whence) #syscall 8
void* mmap(void* addr, u64 length, Prot prot, MmapFlags flags, int fd, u64 offset) #syscall 9
int mprotect(void* addr, u64 length, Prot prot) #syscall 10
int munmap(void* addr, u64 length) #syscall 11
int brk(void* addr) #syscall 12
// TODO Add rest of the syscalls
void* mremap(void* old_address, u64 old_size, u64 new_size, MremapFlags flags) #syscall 25
exit(int status) #syscall 60

enum Mode_T {
    S_IXOTH = 0x1;
    S_IWOTH = 0x2;
    S_IROTH = 0x4;
    S_IRWXO = 0x7;
    S_IXGRP = 0x10;
    S_IWGRP = 0x20;
    S_IRGRP = 0x40;
    S_IRWXG = 0x70;
    S_IXUSR = 0x100;
    S_IWUSR = 0x200;
    S_IRUSR = 0x400;
    S_IRWXU = 0x700;
}

struct Stat {
    // TODO Add fields
}

struct PollFd {
    // TODO Add fields
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

enum MremapFlags {
    MREMAP_MAYMOVE   = 1;
    MREMAP_FIXED     = 2;
    MREMAP_DONTUNMAP = 4;
}
