struct DIR {}

struct dirent {
    u64 d_ino;
    u64 d_off;
    u16 d_reclen;
    FileType d_type;
    List<u8>[256] #c_array d_name;
}

enum FileType : u8 {
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

DIR* opendir(string dirname) #extern "libc"
int closedir(DIR* dir) #extern "libc"
dirent* readdir(DIR* dir) #extern "libc"

u8* malloc(int size) #extern "libc"
free(u8* data) #extern "libc"

struct FILE {}

FILE* popen(string command, string type) #extern "libc"
int pclose(FILE* stream) #extern "libc"
u8* fgets(u8* buffer, int n, FILE* stream) #extern "libc"
