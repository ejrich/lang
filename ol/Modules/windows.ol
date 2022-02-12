// This module contains functions and types only available when compiling and running programs in Windows

#assert os == OS.Windows;

// Windows kernel functions

int GetLastError() #extern "kernel32"
void* VirtualAlloc(void* lpAddress, u64 dwSize, AllocationType flAllocationType, ProtectionType flProtect) #extern "kernel32"
bool VirtualFree(void* lpAddress, u64 dwSize, FreeType dwFreeType) #extern "kernel32"
s64 VirtualQuery(void* lpAddress, MemoryBasicInformation* lpBuffer, int dwLength) #extern "kernel32"
ExitProcess(int uExitCode) #extern "kernel32"

Sleep(int dwMilliseconds) #extern "kernel32"
YieldProcessor() #extern "kernel32"
bool QueryPerformanceCounter(u64* lpPerformanceCount) #extern "kernel32"

Handle* GetStdHandle(int nStdHandle) #extern "kernel32"
bool WriteConsoleA(Handle* hConsoleOutput, void* lpBuffer, int nNumberOfCharsToWrite, int* lpNumberOfCharsWritten, void* lpReserved) #extern "kernel32"

bool PathFileExistsA(string pszPath) #extern "shlwapi"
Handle* OpenFile(string lpFileName, OfStruct* lpReOpenBuff, OpenFileType uStyle) #extern "kernel32"
bool CloseHandle(Handle* hObject) #extern "kernel32"
int SetFilePointer(Handle* hFile, u64 lDistanceToMove, u64* lpDistanceToMoveHigh, MoveMethod dwMoveMethod) #extern "kernel32"
bool ReadFile(Handle* hFile, void* lpBuffer, int nNumberOfBytesToRead, int* nNumberOfBytesRead, void* lpOverlapped) #extern "kernel32"
bool WriteFile(Handle* hFile, void* lpBuffer, int nNumberOfBytesToWrite, int* nNumberOfBytesWritten, void* lpOverlapped) #extern "kernel32"

bool CreatePipe(Handle** hReadPipe, Handle** hWritePipe, SecurityAttributes* lpPipeAttributes, int nSize) #extern "kernel32"
bool SetHandleInformation(Handle* hObject, HandleFlags dwMask, HandleFlags dwFlags) #extern "kernel32"
bool CreateProcessA(string lpApplicationName, string lpCommandLine, SecurityAttributes* lpProcessAttributes, SecurityAttributes* lpThreadAttributes, bool bInheritHandles, int dwCreationFlags, void* lpEnvironment, string lpCurrentDirectory, StartupInfo* lpStartupInfo, ProcessInformation* lpProcessInformation) #extern "kernel32"
bool GetExitCodeProcess(Handle* hProcess, int* lpExitCode) #extern "kernel32"

Handle* FindFirstFileA(string lpFileName, Win32FindData* lpFindFileData) #extern "kernel32"
bool FindNextFileA(Handle* hFindHandle, Win32FindData* lpFindFileData) #extern "kernel32"
bool FindClose(Handle* hFindFile) #extern "kernel32"

Handle* GetModuleHandleA(string lpModuleName) #extern "kernel32"
bool RegisterClassExA(WndClassEx* wndClass) #extern "user32"
s64 DefWindowProcA(Handle* hWnd, u32 uMsg, u32 wParam, s64 lParam) #extern "user32"
Handle* CreateWindowExA(int dwExStyle, string lpClassName, string lpWindowName, WindowStyle dwStyle, u32 x, u32 y, u32 nWidth, u32 nWeight, Handle* hWndParent, Handle* hMenu, Handle* hInstance, void* lpParam) #extern "user32"
bool ShowWindow(Handle* hWnd, WindowShow nCmdShow) #extern "user32"
bool UpdateWindow(Handle* hWnd) #extern "user32"
bool CloseWindow(Handle* hWnd) #extern "user32"

NtStatus BCryptOpenAlgorithmProvider(Handle** phAlgorithm, u16* pszAlgId, u16* pszImplementation, u64 dwFlags) #extern "bcrypt"
NtStatus BCryptCloseAlgorithmProvider(Handle* phAlgorithm, u64 dwFlags) #extern "bcrypt"
NtStatus BCryptGenRandom(Handle* hProv, void* pbBuffer, u64 cbBuffer, u64 dwFlags) #extern "bcrypt"

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

struct SecurityAttributes {
    nLength: int;
    lpSecurityDescriptor: void*;
    bInheritHandle: bool;
}

enum HandleFlags {
    None;
    HANDLE_FLAG_INHERIT;
    HANDLE_FLAG_PROTECT_FROM_CLOSE;
}

struct StartupInfo {
    cb: int;
    lpReserved: u8*;
    lpDesktop: u8*;
    lpTitle: u8*;
    dwX: int;
    dwY: int;
    dwXSize: int;
    dwYSize: int;
    dwXCountChars: int;
    dwYCountChars: int;
    dwFillAttribute: int;
    dwFlags: int;
    wShowWindow: s16;
    cbReserved2: s16;
    lpReserved2: u8*;
    hStdInput: Handle*;
    hStdOutput: Handle*;
    hStdError: Handle*;
}

struct ProcessInformation {
    hProcess: Handle*;
    hThread: Handle*;
    dwProcessId: int;
    dwThreadId: int;
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

interface s64 WndProc(Handle* hWnd, u32 uMsg, u32 wParam, s64 lParam)

struct WndClassEx {
    cbSize: u32;
    style: WindowClassStyle;
    lpfnWndProc: WndProc;
    cbClsExtra: int;
    cbWndExtra: int;
    hInstance: Handle*;
    hIcon: Handle*;
    hCursor: Handle*;
    hbrBackground: Handle*;
    lpszMenuName: u8*;
    lpszClassName: u8*;
    hIconSm: Handle*;
}

enum WindowClassStyle {
    CS_VREDRAW         = 0x1;
    CS_HREDRAW         = 0x2;
    CS_DBLCLKS         = 0x8;
    CS_OWNDC           = 0x20;
    CS_CLASSDC         = 0x40;
    CS_PARENTDC        = 0x80;
    CS_NOCLOSE         = 0x200;
    CS_SAVEBITS        = 0x800;
    CS_BYTEALIGNCLIENT = 0x1000;
    CS_BYTEALIGNWINDOW = 0x2000;
    CS_GLOBALCLASS     = 0x4000;
    CS_DROPSHADOW      = 0x20000;
}

CW_USEDEFAULT: u32 = 0x80000000; #const

enum WindowStyle {
    WS_OVERLAPPED       = 0x0;
    WS_TILED            = 0x0;
    WS_MAXIMIZEBOX      = 0x10000;
    WS_TABSTOP          = 0x10000;
    WS_GROUP            = 0x20000;
    WS_MINIMIZEBOX      = 0x20000;
    WS_SIZEBOX          = 0x40000;
    WS_THICKFRAME       = 0x40000;
    WS_SYSMENU          = 0x80000;
    WS_HSCROLL          = 0x100000;
    WS_VSCROLL          = 0x200000;
    WS_DLGFRAME         = 0x400000;
    WS_BORDER           = 0x800000;
    WS_CAPTION          = 0xC00000;
    WS_MAXIMIZE         = 0x1000000;
    WS_CLIPCHILDREN     = 0x2000000;
    WS_CLIPSIBLINGS     = 0x4000000;
    WS_DISABLED         = 0x8000000;
    WS_VISIBLE          = 0x10000000;
    WS_ICONIC           = 0x20000000;
    WS_MINIMIZE         = 0x20000000;
    WS_CHILD            = 0x40000000;
    WS_CHILDWINDOW      = 0x40000000;
    WS_POPUP            = 0x80000000;
    WS_POPUPWINDOW      = 0x80880000;
    WS_OVERLAPPEDWINDOW = 0xCF0000;
    WS_TILEDWINDOW      = 0x0CF0000;
}

enum WindowShow {
    SW_HIDE;
    SW_NORMAL;
    SW_SHOWMINIMIZED;
    SW_MAXIMIZE;
    SW_SHOWNOACTIVATE;
    SW_SHOW;
    SW_MINIMIZE;
    SW_SHOWMINNOACTIVE;
    SW_SHOWNA;
    SW_RESTORE;
    SW_SHOWDEFAULT;
    SW_FORCEMINIMIZE;
}

enum NtStatus {
    STATUS_SUCCESS           = 0x0;
    STATUS_INVALID_HANDLE    = 0xC0000008;
    STATUS_INVALID_PARAMETER = 0xC000000D;
    STATUS_NO_MEMORY         = 0xC0000017;
    STATUS_NOT_FOUND         = 0xC0000225;
}
