// This module contains functions and types only available when compiling and running programs in Windows

#assert os == OS.Windows;

// Windows kernel functions

void* VirtualAlloc(void* lpAddress, u64 dwSize, AllocationType flAllocationType, ProtectionType flProtect) #extern "kernel32"
bool VirtualFree(void* lpAddress, u64 dwSize, FreeType dwFreeType) #extern "kernel32"
s64 VirtualQuery(void* lpAddress, MemoryBasicInformation* lpBuffer, int dwLength) #extern "kernel32"
ExitProcess(int uExitCode) #extern "kernel32"

Sleep(int dwMilliseconds) #extern "kernel32"
YieldProcessor() #extern "kernel32"
bool QueryPerformanceCounter(u64* lpPerformanceCount) #extern "kernel32"

Handle* GetStdHandle(int nStdHandle) #extern "kernel32"
bool WriteConsole(Handle* hConsoleOutput, void* lpBuffer, int nNumberOfCharsToWrite, int* lpNumberOfCharsWritten, void* lpReserved) #extern "kernel32"

Handle* OpenFile(string lpFileName, OfStruct* lpReOpenBuff, OpenFileType uStyle) #extern "kernel32"
bool CloseHandle(Handle* hObject) #extern "kernel32"
int SetFilePointer(Handle* hFile, u64 lDistanceToMove, u64* lpDistanceToMoveHigh, MoveMethod dwMoveMethod) #extern "kernel32"
bool ReadFile(Handle* hFile, void* lpBuffer, int nNumberOfBytesToRead, int* nNumberOfBytesRead, void* lpOverlapped) #extern "kernel32"
bool WriteFile(Handle* hFile, void* lpBuffer, int nNumberOfBytesToWrite, int* nNumberOfBytesWritten, void* lpOverlapped) #extern "kernel32"

Handle* FindFirstFileA(string lpFileName, Win32FindData* lpFindFileData) #extern "kernel32"
bool FindNextFileA(Handle* hFindHandle, Win32FindData* lpFindFileData) #extern "kernel32"
bool FindClose(Handle* hFindFile) #extern "kernel32"

int BCryptOpenAlgorithmProvider(Handle** phAlgorithm, string pszAlgId, string pszImplementation, u64 dwFlags) #extern "bcrypt"
int BCryptGenRandom(Handle* hProv, void* pbBuffer, u64 cbBuffer, u64 dwFlags) #extern "bcrypt"

STD_INPUT_HANDLE  := -10; #const
STD_OUTPUT_HANDLE := -11; #const
STD_ERROR_HANDLE  := -12; #const

struct Handle {}

enum AllocationType {
    MEM_COMMIT     = 0x1000;
    MEM_RESERVE    = 0x2000;
    MEM_RESET      = 0x80000;
    MEM_RESET_UNDO = 0x1000000;
}

enum ProtectionType {
    PAGE_NOACCESS          = 0x1;
    PAGE_READONLY          = 0x2;
    PAGE_READWRITE         = 0x4;
    PAGE_WRITECOPY         = 0x8;
    PAGE_EXECUTE           = 0x10;
    PAGE_EXECUTE_READ      = 0x20;
    PAGE_EXECUTE_READWRITE = 0x40;
    PAGE_EXECUTE_WRITECOPY = 0x80;
}

enum FreeType {
    MEM_DECOMMIT = 0x4000;
    MEM_RELEASE  = 0x8000;
}

struct MemoryBasicInformation {
    BaseAddress: void*;
    AllocationBase: void*;
    AllocationProtect: ProtectionType;
    PartitionId: s16;
    RegionSize: s64;
    State: int;
    Protect: ProtectionType;
    Type: int;
}

struct OfStruct {
    cBytes: u8;
    fFixedDisk: u8;
    nErrCode: s16;
    Reserved1: s16;
    Reserved2: s16;
    szPathName: CArray<u8>[128];
}

enum OpenFileType {
    OF_READ;
    OF_WRITE            = 0x1;
    OF_READWRITE        = 0x2;
    OF_SHARE_EXCLUSIVE  = 0x10;
    OF_SHARE_DENY_WRITE = 0x20;
    OF_SHARE_DENY_READ  = 0x30;
    OF_SHARE_DENY_NONE  = 0x40;
    OF_PARSE            = 0x100;
    OF_DELETE           = 0x200;
    OF_CANCEL           = 0x800;
    OF_CREATE           = 0x1000;
    OF_PROMPT           = 0x2000;
    OF_EXIST            = 0x4000;
    OF_REOPEN           = 0x8000;
}

enum MoveMethod {
    FILE_BEGIN;
    FILE_CURRENT;
    FILE_END;
}

struct Win32FindData {
    dwFileAttributes: FileAttribute;
    ftCreationTime: FileTime;
    ftLastAccessTime: FileTime;
    ftLastWriteTime: FileTime;
    nFileSizeHigh: int;
    nFileSizeLow: int;
    dwReserved0: int;
    dwReserved1: int;
    cFileName: CArray<u8>[260];
    cAlternateFileName: CArray<u8>[14];
    // Obsolete fields below
    dwFileType: int;
    dwCreatorType: int;
    wFinderFlags: s16;
}

enum FileAttribute {
    FILE_ATTRIBUTE_READONLY              = 0x1;
    FILE_ATTRIBUTE_HIDDEN                = 0x2;
    FILE_ATTRIBUTE_SYSTEM                = 0x4;
    FILE_ATTRIBUTE_DIRECTORY             = 0x10;
    FILE_ATTRIBUTE_ARCHIVE               = 0x20;
    FILE_ATTRIBUTE_DEVICE                = 0x40;
    FILE_ATTRIBUTE_NORMAL                = 0x80;
    FILE_ATTRIBUTE_TEMPORARY             = 0x100;
    FILE_ATTRIBUTE_SPARSE_FILE           = 0x200;
    FILE_ATTRIBUTE_REPARSE_POINT         = 0x400;
    FILE_ATTRIBUTE_COMPRESSED            = 0x800;
    FILE_ATTRIBUTE_OFFLINE               = 0x1000;
    FILE_ATTRIBUTE_NOT_CONTENT_INDEXED   = 0x2000;
    FILE_ATTRIBUTE_ENCRYPTED             = 0x4000;
    FILE_ATTRIBUTE_INTEGRITY_STREAM      = 0x8000;
    FILE_ATTRIBUTE_VIRTUAL               = 0x10000;
    FILE_ATTRIBUTE_NO_SCRUB_DATA         = 0x20000;
    FILE_ATTRIBUTE_RECALL_ON_OPEN        = 0x40000;
    FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;
}

struct FileTime {
    dwLowDateTime: int;
    dwHighDateTime: int;
}
