// This module contains operations for file and path operations

struct FILE {}
struct DIR {}

D_NAME_LENGTH := 256; #const

struct dirent {
    d_ino: u64;
    d_off: u64;
    d_reclen: u16;
    d_type: FileType;
    d_name: CArray<u8>[D_NAME_LENGTH];
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

// Directory operations
DIR* opendir(string dirname) #extern "c"
int closedir(DIR* dir) #extern "c"
dirent* readdir(DIR* dir) #extern "c"

// File operations
FILE* fopen(string file, string type) #extern "c"
int fseek(FILE* file, s64 offset, int origin) #extern "c"
s64 ftell(FILE* file) #extern "c"
int fread(void* buffer, u32 size, u32 length, FILE* file) #extern "c"
int fclose(FILE* file) #extern "c"


bool, string read_file(string file_path, Allocate allocator = null) {
    file_contents: string;
    found: bool;

    file := fopen(file_path, "r");
    if file {
        found = true;
        fseek(file, 0, 2);
        size := ftell(file);
        fseek(file, 0, 0);

        file_contents.length = size;
        if allocator == null allocator = malloc;

        file_contents.data = allocator(size);

        fread(file_contents.data, 1, size, file);
        fclose(file);
    }

    return found, file_contents;
}
