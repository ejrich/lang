// This module contains functions and types only available when compiling and running programs in Windows

#assert os == OS.Windows;

// Windows kernel functions

void* VirtualAlloc(void* lpAddress, u64 dwSize, AllocationType flAllocationType, ProtectionType flProtect) #extern "kernel32"
bool VirtualFree(void* lpAddress, u64 dwSize, FreeType dwFreeType) #extern "kernel32"
ExitProcess(int uExitCode) #extern "kernel32"

Sleep(int dwMilliseconds) #extern "kernel32"
YieldProcessor() #extern "kernel32"
bool QueryPerformanceCounter(u64* lpPerformanceCount) #extern "kernel32"

Handle* GetStdHandle(int nStdHandle) #extern "kernel32"
bool WriteConsole(Handle* hConsoleOutput, void* lpBuffer, int nNumberOfCharsToWrite, int* lpNumberOfCharsWritten, void* lpReserved) #extern "kernel32"

Handle* OpenFile(string lpFileName, OfStruct* lpReOpenBuff, OpenFileType uStyle) #extern "kernel32"
bool CloseHandle(Handle* hObject) #extern "kernel32"
int SetFilePointer(Handle* hFile, u64 lDistanceToMove, u64* lpDistanceToMoveHigh, MoveMethod dwMoveMethod) #extern "kernel32"

bool CryptAcquireContextA(Handle** phProv, string szContainer, string szProvider, int dwProvType, int dwFlags) #extern "advapi32"
bool CryptGenRandom(Handle* hProv, int dwLen, void* pbBuffer) #extern "advapi32"

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
