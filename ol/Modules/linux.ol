// Linux system calls

s64 read(u64 fd, u8* buf, u64 count) #syscall 0
s64 write(u64 fd, u8* buf, u64 count) #syscall 1
int open(u8* pathname, int flags, u32 mode) #syscall 2
int close(u64 fd) #syscall 3
