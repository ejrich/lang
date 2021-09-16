struct DIR {}

struct dirent {
    d_ino: u64;
    d_off: u64;
    d_reclen: u16;
    d_type: FileType;
    d_name: Array<u8>[256] #c_array;
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

struct FILE {}

FILE* popen(string command, string type) #extern "libc"
int pclose(FILE* stream) #extern "libc"
u8* fgets(u8* buffer, int n, FILE* stream) #extern "libc"
