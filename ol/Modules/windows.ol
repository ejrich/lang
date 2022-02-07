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
