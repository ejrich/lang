// This module contains functions and types only available when compiling and running programs in Windows

#assert os == OS.Windows;

// Windows kernel functions

int GetLastError() #extern "kernel32"
void* VirtualAlloc(void* lpAddress, u64 dwSize, AllocationType flAllocationType, ProtectionType flProtect) #extern "kernel32"
bool VirtualFree(void* lpAddress, u64 dwSize, FreeType dwFreeType) #extern "kernel32"
s64 VirtualQuery(void* lpAddress, MEMORY_BASIC_INFORMATION* lpBuffer, int dwLength) #extern "kernel32"
ExitProcess(int uExitCode) #extern "kernel32"

Sleep(int dwMilliseconds) #extern "kernel32"
YieldProcessor() #extern "kernel32"
bool QueryPerformanceCounter(u64* lpPerformanceCount) #extern "kernel32"
bool QueryPerformanceFrequency(u64* lpFrequency) #extern "kernel32"

u8* GetCommandLineA() #extern "kernel32"
Handle* GetStdHandle(int nStdHandle) #extern "kernel32"
bool WriteConsoleA(Handle* hConsoleOutput, void* lpBuffer, int nNumberOfCharsToWrite, int* lpNumberOfCharsWritten, void* lpReserved) #extern "kernel32"
OutputDebugStringA(string lpOutputString) #extern "kernel32"
bool AttachConsole(int dwProcessId) #extern "kernel32"

bool PathFileExistsA(string pszPath) #extern "shlwapi"
int GetFullPathNameA(string lpFileName, int nBufferLength, u8* lpBuffer, u8** lpFilePart) #extern "kernel32"
Handle* OpenFile(string lpFileName, OFSTRUCT* lpReOpenBuff, OpenFileType uStyle) #extern "kernel32"
bool DeleteFileA(string lpFileName) #extern "kernel32"
bool CloseHandle(Handle* hObject) #extern "kernel32"
bool CreateDirectoryA(string lpPathName, SECURITY_ATTRIBUTES* lpSecurityAttributes) #extern "kernel32"

int SetFilePointer(Handle* hFile, u64 lDistanceToMove, u64* lpDistanceToMoveHigh, MoveMethod dwMoveMethod) #extern "kernel32"
bool ReadFile(Handle* hFile, void* lpBuffer, int nNumberOfBytesToRead, int* nNumberOfBytesRead, void* lpOverlapped) #extern "kernel32"
bool WriteFile(Handle* hFile, void* lpBuffer, int nNumberOfBytesToWrite, int* nNumberOfBytesWritten, void* lpOverlapped) #extern "kernel32"
bool GetFileTime(Handle* hFile, FILETIME* lpCreationTime,  FILETIME* lpLastAccessTime,  FILETIME* lpLastWriteTime) #extern "kernel32"

bool CreatePipe(Handle** hReadPipe, Handle** hWritePipe, SECURITY_ATTRIBUTES* lpPipeAttributes, int nSize) #extern "kernel32"
bool SetHandleInformation(Handle* hObject, HandleFlags dwMask, HandleFlags dwFlags) #extern "kernel32"
bool CreateProcessA(string lpApplicationName, string lpCommandLine, SECURITY_ATTRIBUTES* lpProcessAttributes, SECURITY_ATTRIBUTES* lpThreadAttributes, bool bInheritHandles, int dwCreationFlags, void* lpEnvironment, string lpCurrentDirectory, STARTUPINFOA* lpStartupInfo, PROCESS_INFORMATION* lpProcessInformation) #extern "kernel32"
bool GetExitCodeProcess(Handle* hProcess, int* lpExitCode) #extern "kernel32"

Handle* FindFirstFileA(string lpFileName, WIN32_FIND_DATAA* lpFindFileData) #extern "kernel32"
bool FindNextFileA(Handle* hFindHandle, WIN32_FIND_DATAA* lpFindFileData) #extern "kernel32"
bool FindClose(Handle* hFindFile) #extern "kernel32"

int GetModuleFileNameA(Handle* hModule, u8* lpFileName, int nSize) #extern "kernel32"
bool GetCurrentDirectoryA(int nBufferLength, u8* lpBuffer) #extern "kernel32"
bool SetCurrentDirectoryA(string lpPathName) #extern "kernel32"

Handle* GetModuleHandleA(string lpModuleName) #extern "kernel32"
bool RegisterClassExA(WNDCLASSEXA* wndClass) #extern "user32"
Handle* LoadCursorA(Handle* hInstance, u64 lpCursorName) #extern "user32"
Handle* CreateSolidBrush(u32 color) #extern "Gdi32"
int GetSystemMetrics(int nIndex) #extern "user32"
s64 DefWindowProcA(Handle* hWnd, MessageType uMsg, u64 wParam, s64 lParam) #extern "user32"
Handle* CreateWindowExA(ExtendedWindowStyle dwExStyle, string lpClassName, string lpWindowName, WindowStyle dwStyle, u32 x, u32 y, u32 nWidth, u32 nWeight, Handle* hWndParent, Handle* hMenu, Handle* hInstance, void* lpParam) #extern "user32"
s32 SetWindowLongA(Handle* hWnd, s32 nIndex, s64 dwNewLong) #extern "user32"
bool SetLayeredWindowAttributes(Handle* hWnd, u32 crKey, u8 bAlpha, u32 dwFlags) #extern "user32"
bool ShowWindow(Handle* hWnd, WindowShow nCmdShow) #extern "user32"
bool UpdateWindow(Handle* hWnd) #extern "user32"
bool CloseWindow(Handle* hWnd) #extern "user32"
bool GetWindowRect(Handle* hWnd, RECT* lpRect) #extern "user32"
bool SetWindowPos(Handle* hWnd, Handle* hWndInsertAfter, int X, int Y, int cx, int cy, SWPFlags uFlags) #extern "user32"
bool SetProcessDPIAware() #extern "user32"
s32 DwmExtendFrameIntoClientArea(Handle* hWnd, MARGINS* pMarInset) #extern "dwmapi"

bool GetMessage(MSG* lpMsg, Handle* hWnd, u32 wMsgFilterMin, u32 wMsgFilterMax) #extern "user32"
bool PeekMessageA(MSG* lpMsg, Handle* hWnd, u32 wMsgFilterMin, u32 wMsgFilterMax, RemoveMsg wRemoveMsg) #extern "user32"
bool TranslateMessage(MSG* lpMsg) #extern "user32"
s64 DispatchMessageA(MSG* lpMsg) #extern "user32"
s16 GetKeyState(int nVirtKey) #extern "user32"
bool GetKeyboardState(u8* lpKeyState) #extern "user32"
int ToAscii(u32 uVirtKey, u32 uScanCode, u8* lpKeyState, u8* lpChar, u32 uFlags) #extern "user32"
bool GetCursorPos(POINT* lpPoint) #extern "user32"

Handle* BeginPaint(Handle* hWnd, PAINTSTRUCT* lpPaint) #extern "user32"
bool EndPaint(Handle* hWnd, PAINTSTRUCT* lpPaint) #extern "user32"
bool InvalidateRect(Handle* hWnd, RECT* lpRect, bool bErase) #extern "user32"

NtStatus BCryptOpenAlgorithmProvider(Handle** phAlgorithm, u16* pszAlgId, u16* pszImplementation, u64 dwFlags) #extern "bcrypt"
NtStatus BCryptCloseAlgorithmProvider(Handle* phAlgorithm, u64 dwFlags) #extern "bcrypt"
NtStatus BCryptGenRandom(Handle* hProv, void* pbBuffer, u64 cbBuffer, u64 dwFlags) #extern "bcrypt"

GetSystemInfo(SYSTEM_INFO* lpSystemInfo) #extern "kernel32"
Handle* CreateThread(SECURITY_ATTRIBUTES* lpThreadAttributes, u64 dwStackSize, ThreadProcedure lpStartAddress, void* lpParameter, int dwCreationFlags, int* lpThreadId) #extern "kernel32"
Handle* CreateSemaphoreA(SECURITY_ATTRIBUTES* lpSemaphoreAttributes, u64 lInitialCount, u64 lMaximumCount, string lpName) #extern "kernel32"
bool ReleaseSemaphore(Handle* hSemaphore, u64 lReleaseCount, u64* lpPreviousCount) #extern "kernel32"
int WaitForSingleObject(Handle* hHandle, int dwMilliseconds) #extern "kernel32"
int GetEnvironmentVariableA(string lpName, u8* lpBuffer, int nSize) #extern "kernel32"

GetSystemTime(SYSTEMTIME* lpSystemTime) #extern "kernel32"
GetLocalTime(SYSTEMTIME* lpSystemTime) #extern "kernel32"

s32 CoInitializeEx(void* pvReserved, u32 dwCoInit) #extern "ole32"
s32 CoCreateInstance(GUID* rclsid, void** pUnkOuter, u32 dwClsContext, void* riid, void** ppv) #extern "ole32"
CoTaskMemFree(void* pv) #extern "ole32"
Handle* CreateEventA(SECURITY_ATTRIBUTES* lpSecurityAttributes, bool bManualReset, bool bInitialState, string lpName) #extern "kernel32"

bool AddClipboardFormatListener(Handle* hwnd) #extern "user32"
bool OpenClipboard(Handle* hWndNewOwner) #extern "user32"
bool CloseClipboard() #extern "user32"
Handle* GetClipboardData(ClipboardFormat uFormat) #extern "user32"
Handle* SetClipboardData(ClipboardFormat uFormat, Handle* hMem) #extern "user32"
bool EmptyClipboard() #extern "user32"

Handle* GlobalAlloc(u32 uFlags, u32 dwBytes) #extern "kernel32"
void* GlobalLock(Handle* hMem) #extern "kernel32"
bool GlobalUnlock(Handle* hMem) #extern "kernel32"

STD_INPUT_HANDLE  := -10; #const
STD_OUTPUT_HANDLE := -11; #const
STD_ERROR_HANDLE  := -12; #const

struct GUID {
    data1: u32;
    data2: u16;
    data3: u16;
    data4: CArray<u8>[8];
}

home_environment_variable := "UserProfile"; #const

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

struct MEMORY_BASIC_INFORMATION {
    BaseAddress: void*;
    AllocationBase: void*;
    AllocationProtect: ProtectionType;
    PartitionId: s16;
    RegionSize: s64;
    State: int;
    Protect: ProtectionType;
    Type: int;
}

struct OFSTRUCT {
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

struct FILETIME {
    dwLowDateTime: u32;
    dwHighDateTime: u32;
}

struct SECURITY_ATTRIBUTES {
    nLength: int;
    lpSecurityDescriptor: void*;
    bInheritHandle: bool;
}

enum HandleFlags {
    None;
    HANDLE_FLAG_INHERIT;
    HANDLE_FLAG_PROTECT_FROM_CLOSE;
}

struct STARTUPINFOA {
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

struct PROCESS_INFORMATION {
    hProcess: Handle*;
    hThread: Handle*;
    dwProcessId: int;
    dwThreadId: int;
}

struct WIN32_FIND_DATAA {
    dwFileAttributes: FileAttribute;
    ftCreationTime: FILETIME;
    ftLastAccessTime: FILETIME;
    ftLastWriteTime: FILETIME;
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

[flags]
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

interface s64 WNDPROC(Handle* hWnd, MessageType uMsg, u64 wParam, s64 lParam)

struct WNDCLASSEXA {
    cbSize: u32;
    style: WindowClassStyle;
    lpfnWndProc: WNDPROC;
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

enum ExtendedWindowStyle : u32 {
    WS_EX_LEFT                = 0x0;
    WS_EX_LTRREADING          = 0x0;
    WS_EX_RIGHTSCROLLBAR      = 0x0;
    WS_EX_DLGMODALFRAME       = 0x1;
    WS_EX_NOPARENTNOTIFY      = 0x4;
    WS_EX_TOPMOST             = 0x8;
    WS_EX_ACCEPTFILES         = 0x10;
    WS_EX_TRANSPARENT         = 0x20;
    WS_EX_MDICHILD            = 0x40;
    WS_EX_TOOLWINDOW          = 0x80;
    WS_EX_WINDOWEDGE          = 0x100;
    WS_EX_CLIENTEDGE          = 0x200;
    WS_EX_CONTEXTHELP         = 0x400;
    WS_EX_RIGHT               = 0x1000;
    WS_EX_RTLREADING          = 0x2000;
    WS_EX_LEFTSCROLLBAR       = 0x4000;
    WS_EX_CONTROLPARENT       = 0x10000;
    WS_EX_STATICEDGE          = 0x20000;
    WS_EX_APPWINDOW           = 0x40000;
    WS_EX_LAYERED             = 0x80000;
    WS_EX_NOINHERITLAYOUT     = 0x100000;
    WS_EX_NOREDIRECTIONBITMAP = 0x200000;
    WS_EX_LAYOUTRTL           = 0x400000;
    WS_EX_COMPOSITED          = 0x2000000;
    WS_EX_NOACTIVATE          = 0x8000000;
    WS_EX_OVERLAPPEDWINDOW    = 0x300;
    WS_EX_PALETTEWINDOW       = 0x188;
}

enum WindowStyle : u32 {
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

struct MSG {
    hwnd: Handle*;
    message: MessageType;
    wParam: u64;
    lParam: s64;
    time: int;
    pt: POINT;
    lPrivate: int;
}

struct POINT {
    x: s32;
    y: s32;
}

struct PAINTSTRUCT {
    hdc: Handle*;
    fErase: s32;
    rcPaint: RECT;
    fRestore: s32;
    fIncUpdate: s32;
    rgbReserved: CArray<u8>[32];
}

enum MessageType {
    WM_NULL = 0;
    WM_CREATE = 1;
    WM_DESTROY = 2;
    WM_MOVE = 3;
    WM_SIZE = 5;
    WM_ACTIVATE = 6;
    WM_SETFOCUS = 7;
    WM_KILLFOCUS = 8;
    WM_ENABLE = 10;
    WM_SETREDRAW = 11;
    WM_SETTEXT = 12;
    WM_GETTEXT = 13;
    WM_GETTEXTLENGTH = 14;
    WM_PAINT = 15;
    WM_CLOSE = 16;
    WM_QUERYENDSESSION = 17;
    WM_QUIT = 18;
    WM_QUERYOPEN = 19;
    WM_ERASEBKGND = 20;
    WM_SYSCOLORCHANGE = 21;
    WM_ENDSESSION = 22;
    WM_SHOWWINDOW = 24;
    WM_CTLCOLOR = 25;
    WM_WININICHANGE = 26;
    WM_DEVMODECHANGE = 27;
    WM_ACTIVATEAPP = 28;
    WM_FONTCHANGE = 29;
    WM_TIMECHANGE = 30;
    WM_CANCELMODE = 31;
    WM_SETCURSOR = 32;
    WM_MOUSEACTIVATE = 33;
    WM_CHILDACTIVATE = 34;
    WM_QUEUESYNC = 35;
    WM_GETMINMAXINFO = 36;
    WM_PAINTICON = 38;
    WM_ICONERASEBKGND = 39;
    WM_NEXTDLGCTL = 40;
    WM_SPOOLERSTATUS = 42;
    WM_DRAWITEM = 43;
    WM_MEASUREITEM = 44;
    WM_DELETEITEM = 45;
    WM_VKEYTOITEM = 46;
    WM_CHARTOITEM = 47;
    WM_SETFONT = 48;
    WM_GETFONT = 49;
    WM_SETHOTKEY = 50;
    WM_GETHOTKEY = 51;
    WM_QUERYDRAGICON = 55;
    WM_COMPAREITEM = 57;
    WM_GETOBJECT = 61;
    WM_COMPACTING = 65;
    WM_COMMNOTIFY = 68;
    WM_WINDOWPOSCHANGING = 70;
    WM_WINDOWPOSCHANGED = 71;
    WM_POWER = 72;
    WM_COPYGLOBALDATA = 73;
    WM_COPYDATA = 74;
    WM_CANCELJOURNAL = 75;
    WM_NOTIFY = 78;
    WM_INPUTLANGCHANGEREQUEST = 80;
    WM_INPUTLANGCHANGE = 81;
    WM_TCARD = 82;
    WM_HELP = 83;
    WM_USERCHANGED = 84;
    WM_NOTIFYFORMAT = 85;
    WM_CONTEXTMENU = 123;
    WM_STYLECHANGING = 124;
    WM_STYLECHANGED = 125;
    WM_DISPLAYCHANGE = 126;
    WM_GETICON = 127;
    WM_SETICON = 128;
    WM_NCCREATE = 129;
    WM_NCDESTROY = 130;
    WM_NCCALCSIZE = 131;
    WM_NCHITTEST = 132;
    WM_NCPAINT = 133;
    WM_NCACTIVATE = 134;
    WM_GETDLGCODE = 135;
    WM_SYNCPAINT = 136;
    WM_NCMOUSEMOVE = 160;
    WM_NCLBUTTONDOWN = 161;
    WM_NCLBUTTONUP = 162;
    WM_NCLBUTTONDBLCLK = 163;
    WM_NCRBUTTONDOWN = 164;
    WM_NCRBUTTONUP = 165;
    WM_NCRBUTTONDBLCLK = 166;
    WM_NCMBUTTONDOWN = 167;
    WM_NCMBUTTONUP = 168;
    WM_NCMBUTTONDBLCLK = 169;
    WM_NCXBUTTONDOWN = 171;
    WM_NCXBUTTONUP = 172;
    WM_NCXBUTTONDBLCLK = 173;
    EM_GETSEL = 176;
    EM_SETSEL = 177;
    EM_GETRECT = 178;
    EM_SETRECT = 179;
    EM_SETRECTNP = 180;
    EM_SCROLL = 181;
    EM_LINESCROLL = 182;
    EM_SCROLLCARET = 183;
    EM_GETMODIFY = 185;
    EM_SETMODIFY = 187;
    EM_GETLINECOUNT = 188;
    EM_LINEINDEX = 189;
    EM_SETHANDLE = 190;
    EM_GETHANDLE = 191;
    EM_GETTHUMB = 192;
    EM_LINELENGTH = 193;
    EM_REPLACESEL = 194;
    EM_SETFONT = 195;
    EM_GETLINE = 196;
    EM_LIMITTEXT = 197;
    EM_SETLIMITTEXT = 197;
    EM_CANUNDO = 198;
    EM_UNDO = 199;
    EM_FMTLINES = 200;
    EM_LINEFROMCHAR = 201;
    EM_SETWORDBREAK = 202;
    EM_SETTABSTOPS = 203;
    EM_SETPASSWORDCHAR = 204;
    EM_EMPTYUNDOBUFFER = 205;
    EM_GETFIRSTVISIBLELINE = 206;
    EM_SETREADONLY = 207;
    EM_SETWORDBREAKPROC = 209;
    EM_GETWORDBREAKPROC = 209;
    EM_GETPASSWORDCHAR = 210;
    EM_SETMARGINS = 211;
    EM_GETMARGINS = 212;
    EM_GETLIMITTEXT = 213;
    EM_POSFROMCHAR = 214;
    EM_CHARFROMPOS = 215;
    EM_SETIMESTATUS = 216;
    EM_GETIMESTATUS = 217;
    SBM_SETPOS = 224;
    SBM_GETPOS = 225;
    SBM_SETRANGE = 226;
    SBM_GETRANGE = 227;
    SBM_ENABLE_ARROWS = 228;
    SBM_SETRANGEREDRAW = 230;
    SBM_SETSCROLLINFO = 233;
    SBM_GETSCROLLINFO = 234;
    SBM_GETSCROLLBARINFO = 235;
    BM_GETCHECK = 240;
    BM_SETCHECK = 241;
    BM_GETSTATE = 242;
    BM_SETSTATE = 243;
    BM_SETSTYLE = 244;
    BM_CLICK = 245;
    BM_GETIMAGE = 246;
    BM_SETIMAGE = 247;
    BM_SETDONTCLICK = 248;
    WM_INPUT = 255;
    WM_KEYDOWN = 256;
    WM_KEYFIRST = 256;
    WM_KEYUP = 257;
    WM_CHAR = 258;
    WM_DEADCHAR = 259;
    WM_SYSKEYDOWN = 260;
    WM_SYSKEYUP = 261;
    WM_SYSCHAR = 262;
    WM_SYSDEADCHAR = 263;
    WM_UNICHAR = 265;
    WM_KEYLAST = 265;
    WM_WNT_CONVERTREQUESTEX = 265;
    WM_CONVERTREQUEST = 266;
    WM_CONVERTRESULT = 267;
    WM_INTERIM = 268;
    WM_IME_STARTCOMPOSITION = 269;
    WM_IME_ENDCOMPOSITION = 270;
    WM_IME_COMPOSITION = 271;
    WM_IME_KEYLAST = 271;
    WM_INITDIALOG = 272;
    WM_COMMAND = 273;
    WM_SYSCOMMAND = 274;
    WM_TIMER = 275;
    WM_HSCROLL = 276;
    WM_VSCROLL = 277;
    WM_INITMENU = 278;
    WM_INITMENUPOPUP = 279;
    WM_SYSTIMER = 280;
    WM_MENUSELECT = 287;
    WM_MENUCHAR = 288;
    WM_ENTERIDLE = 289;
    WM_MENURBUTTONUP = 290;
    WM_MENUDRAG = 291;
    WM_MENUGETOBJECT = 292;
    WM_UNINITMENUPOPUP = 293;
    WM_MENUCOMMAND = 294;
    WM_CHANGEUISTATE = 295;
    WM_UPDATEUISTATE = 296;
    WM_QUERYUISTATE = 297;
    WM_LBTRACKPOINT = 305;
    WM_CTLCOLORMSGBOX = 306;
    WM_CTLCOLOREDIT = 307;
    WM_CTLCOLORLISTBOX = 308;
    WM_CTLCOLORBTN = 309;
    WM_CTLCOLORDLG = 310;
    WM_CTLCOLORSCROLLBAR = 311;
    WM_CTLCOLORSTATIC = 312;
    CB_GETEDITSEL = 320;
    CB_LIMITTEXT = 321;
    CB_SETEDITSEL = 322;
    CB_ADDSTRING = 323;
    CB_DELETESTRING = 324;
    CB_DIR = 325;
    CB_GETCOUNT = 326;
    CB_GETCURSEL = 327;
    CB_GETLBTEXT = 328;
    CB_GETLBTEXTLEN = 329;
    CB_INSERTSTRING = 330;
    CB_RESETCONTENT = 331;
    CB_FINDSTRING = 332;
    CB_SELECTSTRING = 333;
    CB_SETCURSEL = 334;
    CB_SHOWDROPDOWN = 335;
    CB_GETITEMDATA = 336;
    CB_SETITEMDATA = 337;
    CB_GETDROPPEDCONTROLRECT = 338;
    CB_SETITEMHEIGHT = 339;
    CB_GETITEMHEIGHT = 340;
    CB_SETEXTENDEDUI = 341;
    CB_GETEXTENDEDUI = 342;
    CB_GETDROPPEDSTATE = 343;
    CB_FINDSTRINGEXACT = 344;
    CB_SETLOCALE = 345;
    CB_GETLOCALE = 346;
    CB_GETTOPINDEX = 347;
    CB_SETTOPINDEX = 348;
    CB_GETHORIZONTALEXTENT = 349;
    CB_SETHORIZONTALEXTENT = 350;
    CB_GETDROPPEDWIDTH = 351;
    CB_SETDROPPEDWIDTH = 352;
    CB_INITSTORAGE = 353;
    CB_MULTIPLEADDSTRING = 355;
    CB_GETCOMBOBOXINFO = 356;
    CB_MSGMAX = 357;
    WM_MOUSEFIRST = 512;
    WM_MOUSEMOVE = 512;
    WM_LBUTTONDOWN = 513;
    WM_LBUTTONUP = 514;
    WM_LBUTTONDBLCLK = 515;
    WM_RBUTTONDOWN = 516;
    WM_RBUTTONUP = 517;
    WM_RBUTTONDBLCLK = 518;
    WM_MBUTTONDOWN = 519;
    WM_MBUTTONUP = 520;
    WM_MBUTTONDBLCLK = 521;
    WM_MOUSELAST = 521;
    WM_MOUSEWHEEL = 522;
    WM_XBUTTONDOWN = 523;
    WM_XBUTTONUP = 524;
    WM_XBUTTONDBLCLK = 525;
    WM_MOUSEHWHEEL = 526;
    WM_PARENTNOTIFY = 528;
    WM_ENTERMENULOOP = 529;
    WM_EXITMENULOOP = 530;
    WM_NEXTMENU = 531;
    WM_SIZING = 532;
    WM_CAPTURECHANGED = 533;
    WM_MOVING = 534;
    WM_POWERBROADCAST = 536;
    WM_DEVICECHANGE = 537;
    WM_MDICREATE = 544;
    WM_MDIDESTROY = 545;
    WM_MDIACTIVATE = 546;
    WM_MDIRESTORE = 547;
    WM_MDINEXT = 548;
    WM_MDIMAXIMIZE = 549;
    WM_MDITILE = 550;
    WM_MDICASCADE = 551;
    WM_MDIICONARRANGE = 552;
    WM_MDIGETACTIVE = 553;
    WM_MDISETMENU = 560;
    WM_ENTERSIZEMOVE = 561;
    WM_EXITSIZEMOVE = 562;
    WM_DROPFILES = 563;
    WM_MDIREFRESHMENU = 564;
    WM_IME_REPORT = 640;
    WM_IME_SETCONTEXT = 641;
    WM_IME_NOTIFY = 642;
    WM_IME_CONTROL = 643;
    WM_IME_COMPOSITIONFULL = 644;
    WM_IME_SELECT = 645;
    WM_IME_CHAR = 646;
    WM_IME_REQUEST = 648;
    WM_IMEKEYDOWN = 656;
    WM_IME_KEYDOWN = 656;
    WM_IMEKEYUP = 657;
    WM_IME_KEYUP = 657;
    WM_NCMOUSEHOVER = 672;
    WM_MOUSEHOVER = 673;
    WM_NCMOUSELEAVE = 674;
    WM_MOUSELEAVE = 675;
    WM_CUT = 768;
    WM_COPY = 769;
    WM_PASTE = 770;
    WM_CLEAR = 771;
    WM_UNDO = 772;
    WM_RENDERFORMAT = 773;
    WM_RENDERALLFORMATS = 774;
    WM_DESTROYCLIPBOARD = 775;
    WM_DRAWCLIPBOARD = 776;
    WM_PAINTCLIPBOARD = 777;
    WM_VSCROLLCLIPBOARD = 778;
    WM_SIZECLIPBOARD = 779;
    WM_ASKCBFORMATNAME = 780;
    WM_CHANGECBCHAIN = 781;
    WM_HSCROLLCLIPBOARD = 782;
    WM_QUERYNEWPALETTE = 783;
    WM_PALETTEISCHANGING = 784;
    WM_PALETTECHANGED = 785;
    WM_HOTKEY = 786;
    WM_PRINT = 791;
    WM_PRINTCLIENT = 792;
    WM_APPCOMMAND = 793;
    WM_CLIPBOARDUPDATE = 797;
    WM_HANDHELDFIRST = 856;
    WM_HANDHELDLAST = 863;
    WM_AFXFIRST = 864;
    WM_AFXLAST = 895;
    WM_PENWINFIRST = 896;
    WM_RCRESULT = 897;
    WM_HOOKRCRESULT = 898;
    WM_GLOBALRCCHANGE = 899;
    WM_PENMISCINFO = 899;
    WM_SKB = 900;
    WM_HEDITCTL = 901;
    WM_PENCTL = 901;
    WM_PENMISC = 902;
    WM_CTLINIT = 903;
    WM_PENEVENT = 904;
    WM_PENWINLAST = 911;
    DDM_SETFMT = 1024;
    DM_GETDEFID = 1024;
    NIN_SELECT = 1024;
    TBM_GETPOS = 1024;
    WM_PSD_PAGESETUPDLG = 1024;
    WM_USER = 1024;
    CBEM_INSERTITEMA = 1025;
    DDM_DRAW = 1025;
    DM_SETDEFID = 1025;
    HKM_SETHOTKEY = 1025;
    PBM_SETRANGE = 1025;
    RB_INSERTBANDA = 1025;
    SB_SETTEXTA = 1025;
    TB_ENABLEBUTTON = 1025;
    TBM_GETRANGEMIN = 1025;
    TTM_ACTIVATE = 1025;
    WM_CHOOSEFONT_GETLOGFONT = 1025;
    WM_PSD_FULLPAGERECT = 1025;
    CBEM_SETIMAGELIST = 1026;
    DDM_CLOSE = 1026;
    DM_REPOSITION = 1026;
    HKM_GETHOTKEY = 1026;
    PBM_SETPOS = 1026;
    RB_DELETEBAND = 1026;
    SB_GETTEXTA = 1026;
    TB_CHECKBUTTON = 1026;
    TBM_GETRANGEMAX = 1026;
    WM_PSD_MINMARGINRECT = 1026;
    CBEM_GETIMAGELIST = 1027;
    DDM_BEGIN = 1027;
    HKM_SETRULES = 1027;
    PBM_DELTAPOS = 1027;
    RB_GETBARINFO = 1027;
    SB_GETTEXTLENGTHA = 1027;
    TBM_GETTIC = 1027;
    TB_PRESSBUTTON = 1027;
    TTM_SETDELAYTIME = 1027;
    WM_PSD_MARGINRECT = 1027;
    CBEM_GETITEMA = 1028;
    DDM_END = 1028;
    PBM_SETSTEP = 1028;
    RB_SETBARINFO = 1028;
    SB_SETPARTS = 1028;
    TB_HIDEBUTTON = 1028;
    TBM_SETTIC = 1028;
    TTM_ADDTOOLA = 1028;
    WM_PSD_GREEKTEXTRECT = 1028;
    CBEM_SETITEMA = 1029;
    PBM_STEPIT = 1029;
    TB_INDETERMINATE = 1029;
    TBM_SETPOS = 1029;
    TTM_DELTOOLA = 1029;
    WM_PSD_ENVSTAMPRECT = 1029;
    CBEM_GETCOMBOCONTROL = 1030;
    PBM_SETRANGE32 = 1030;
    RB_SETBANDINFOA = 1030;
    SB_GETPARTS = 1030;
    TB_MARKBUTTON = 1030;
    TBM_SETRANGE = 1030;
    TTM_NEWTOOLRECTA = 1030;
    WM_PSD_YAFULLPAGERECT = 1030;
    CBEM_GETEDITCONTROL = 1031;
    PBM_GETRANGE = 1031;
    RB_SETPARENT = 1031;
    SB_GETBORDERS = 1031;
    TBM_SETRANGEMIN = 1031;
    TTM_RELAYEVENT = 1031;
    CBEM_SETEXSTYLE = 1032;
    PBM_GETPOS = 1032;
    RB_HITTEST = 1032;
    SB_SETMINHEIGHT = 1032;
    TBM_SETRANGEMAX = 1032;
    TTM_GETTOOLINFOA = 1032;
    CBEM_GETEXSTYLE = 1033;
    CBEM_GETEXTENDEDSTYLE = 1033;
    PBM_SETBARCOLOR = 1033;
    RB_GETRECT = 1033;
    SB_SIMPLE = 1033;
    TB_ISBUTTONENABLED = 1033;
    TBM_CLEARTICS = 1033;
    TTM_SETTOOLINFOA = 1033;
    CBEM_HASEDITCHANGED = 1034;
    RB_INSERTBANDW = 1034;
    SB_GETRECT = 1034;
    TB_ISBUTTONCHECKED = 1034;
    TBM_SETSEL = 1034;
    TTM_HITTESTA = 1034;
    WIZ_QUERYNUMPAGES = 1034;
    CBEM_INSERTITEMW = 1035;
    RB_SETBANDINFOW = 1035;
    SB_SETTEXTW = 1035;
    TB_ISBUTTONPRESSED = 1035;
    TBM_SETSELSTART = 1035;
    TTM_GETTEXTA = 1035;
    WIZ_NEXT = 1035;
    CBEM_SETITEMW = 1036;
    RB_GETBANDCOUNT = 1036;
    SB_GETTEXTLENGTHW = 1036;
    TB_ISBUTTONHIDDEN = 1036;
    TBM_SETSELEND = 1036;
    TTM_UPDATETIPTEXTA = 1036;
    WIZ_PREV = 1036;
    CBEM_GETITEMW = 1037;
    RB_GETROWCOUNT = 1037;
    SB_GETTEXTW = 1037;
    TB_ISBUTTONINDETERMINATE = 1037;
    TTM_GETTOOLCOUNT = 1037;
    CBEM_SETEXTENDEDSTYLE = 1038;
    RB_GETROWHEIGHT = 1038;
    SB_ISSIMPLE = 1038;
    TB_ISBUTTONHIGHLIGHTED = 1038;
    TBM_GETPTICS = 1038;
    TTM_ENUMTOOLSA = 1038;
    SB_SETICON = 1039;
    TBM_GETTICPOS = 1039;
    TTM_GETCURRENTTOOLA = 1039;
    RB_IDTOINDEX = 1040;
    SB_SETTIPTEXTA = 1040;
    TBM_GETNUMTICS = 1040;
    TTM_WINDOWFROMPOINT = 1040;
    RB_GETTOOLTIPS = 1041;
    SB_SETTIPTEXTW = 1041;
    TBM_GETSELSTART = 1041;
    TB_SETSTATE = 1041;
    TTM_TRACKACTIVATE = 1041;
    RB_SETTOOLTIPS = 1042;
    SB_GETTIPTEXTA = 1042;
    TB_GETSTATE = 1042;
    TBM_GETSELEND = 1042;
    TTM_TRACKPOSITION = 1042;
    RB_SETBKCOLOR = 1043;
    SB_GETTIPTEXTW = 1043;
    TB_ADDBITMAP = 1043;
    TBM_CLEARSEL = 1043;
    TTM_SETTIPBKCOLOR = 1043;
    RB_GETBKCOLOR = 1044;
    SB_GETICON = 1044;
    TB_ADDBUTTONSA = 1044;
    TBM_SETTICFREQ = 1044;
    TTM_SETTIPTEXTCOLOR = 1044;
    RB_SETTEXTCOLOR = 1045;
    TB_INSERTBUTTONA = 1045;
    TBM_SETPAGESIZE = 1045;
    TTM_GETDELAYTIME = 1045;
    RB_GETTEXTCOLOR = 1046;
    TB_DELETEBUTTON = 1046;
    TBM_GETPAGESIZE = 1046;
    TTM_GETTIPBKCOLOR = 1046;
    RB_SIZETORECT = 1047;
    TB_GETBUTTON = 1047;
    TBM_SETLINESIZE = 1047;
    TTM_GETTIPTEXTCOLOR = 1047;
    RB_BEGINDRAG = 1048;
    TB_BUTTONCOUNT = 1048;
    TBM_GETLINESIZE = 1048;
    TTM_SETMAXTIPWIDTH = 1048;
    RB_ENDDRAG = 1049;
    TB_COMMANDTOINDEX = 1049;
    TBM_GETTHUMBRECT = 1049;
    TTM_GETMAXTIPWIDTH = 1049;
    RB_DRAGMOVE = 1050;
    TBM_GETCHANNELRECT = 1050;
    TB_SAVERESTOREA = 1050;
    TTM_SETMARGIN = 1050;
    RB_GETBARHEIGHT = 1051;
    TB_CUSTOMIZE = 1051;
    TBM_SETTHUMBLENGTH = 1051;
    TTM_GETMARGIN = 1051;
    RB_GETBANDINFOW = 1052;
    TB_ADDSTRINGA = 1052;
    TBM_GETTHUMBLENGTH = 1052;
    TTM_POP = 1052;
    RB_GETBANDINFOA = 1053;
    TB_GETITEMRECT = 1053;
    TBM_SETTOOLTIPS = 1053;
    TTM_UPDATE = 1053;
    RB_MINIMIZEBAND = 1054;
    TB_BUTTONSTRUCTSIZE = 1054;
    TBM_GETTOOLTIPS = 1054;
    TTM_GETBUBBLESIZE = 1054;
    RB_MAXIMIZEBAND = 1055;
    TBM_SETTIPSIDE = 1055;
    TB_SETBUTTONSIZE = 1055;
    TTM_ADJUSTRECT = 1055;
    TBM_SETBUDDY = 1056;
    TB_SETBITMAPSIZE = 1056;
    TTM_SETTITLEA = 1056;
    MSG_FTS_JUMP_VA = 1057;
    TB_AUTOSIZE = 1057;
    TBM_GETBUDDY = 1057;
    TTM_SETTITLEW = 1057;
    RB_GETBANDBORDERS = 1058;
    MSG_FTS_JUMP_QWORD = 1059;
    RB_SHOWBAND = 1059;
    TB_GETTOOLTIPS = 1059;
    MSG_REINDEX_REQUEST = 1060;
    TB_SETTOOLTIPS = 1060;
    MSG_FTS_WHERE_IS_IT = 1061;
    RB_SETPALETTE = 1061;
    TB_SETPARENT = 1061;
    RB_GETPALETTE = 1062;
    RB_MOVEBAND = 1063;
    TB_SETROWS = 1063;
    TB_GETROWS = 1064;
    TB_GETBITMAPFLAGS = 1065;
    TB_SETCMDID = 1066;
    RB_PUSHCHEVRON = 1067;
    TB_CHANGEBITMAP = 1067;
    TB_GETBITMAP = 1068;
    MSG_GET_DEFFONT = 1069;
    TB_GETBUTTONTEXTA = 1069;
    TB_REPLACEBITMAP = 1070;
    TB_SETINDENT = 1071;
    TB_SETIMAGELIST = 1072;
    TB_GETIMAGELIST = 1073;
    TB_LOADIMAGES = 1074;
    EM_CANPASTE = 1074;
    TTM_ADDTOOLW = 1074;
    EM_DISPLAYBAND = 1075;
    TB_GETRECT = 1075;
    TTM_DELTOOLW = 1075;
    EM_EXGETSEL = 1076;
    TB_SETHOTIMAGELIST = 1076;
    TTM_NEWTOOLRECTW = 1076;
    EM_EXLIMITTEXT = 1077;
    TB_GETHOTIMAGELIST = 1077;
    TTM_GETTOOLINFOW = 1077;
    EM_EXLINEFROMCHAR = 1078;
    TB_SETDISABLEDIMAGELIST = 1078;
    TTM_SETTOOLINFOW = 1078;
    EM_EXSETSEL = 1079;
    TB_GETDISABLEDIMAGELIST = 1079;
    TTM_HITTESTW = 1079;
    EM_FINDTEXT = 1080;
    TB_SETSTYLE = 1080;
    TTM_GETTEXTW = 1080;
    EM_FORMATRANGE = 1081;
    TB_GETSTYLE = 1081;
    TTM_UPDATETIPTEXTW = 1081;
    EM_GETCHARFORMAT = 1082;
    TB_GETBUTTONSIZE = 1082;
    TTM_ENUMTOOLSW = 1082;
    EM_GETEVENTMASK = 1083;
    TB_SETBUTTONWIDTH = 1083;
    TTM_GETCURRENTTOOLW = 1083;
    EM_GETOLEINTERFACE = 1084;
    TB_SETMAXTEXTROWS = 1084;
    EM_GETPARAFORMAT = 1085;
    TB_GETTEXTROWS = 1085;
    EM_GETSELTEXT = 1086;
    TB_GETOBJECT = 1086;
    EM_HIDESELECTION = 1087;
    TB_GETBUTTONINFOW = 1087;
    EM_PASTESPECIAL = 1088;
    TB_SETBUTTONINFOW = 1088;
    EM_REQUESTRESIZE = 1089;
    TB_GETBUTTONINFOA = 1089;
    EM_SELECTIONTYPE = 1090;
    TB_SETBUTTONINFOA = 1090;
    EM_SETBKGNDCOLOR = 1091;
    TB_INSERTBUTTONW = 1091;
    EM_SETCHARFORMAT = 1092;
    TB_ADDBUTTONSW = 1092;
    EM_SETEVENTMASK = 1093;
    TB_HITTEST = 1093;
    EM_SETOLECALLBACK = 1094;
    TB_SETDRAWTEXTFLAGS = 1094;
    EM_SETPARAFORMAT = 1095;
    TB_GETHOTITEM = 1095;
    EM_SETTARGETDEVICE = 1096;
    TB_SETHOTITEM = 1096;
    EM_STREAMIN = 1097;
    TB_SETANCHORHIGHLIGHT = 1097;
    EM_STREAMOUT = 1098;
    TB_GETANCHORHIGHLIGHT = 1098;
    EM_GETTEXTRANGE = 1099;
    TB_GETBUTTONTEXTW = 1099;
    EM_FINDWORDBREAK = 1100;
    TB_SAVERESTOREW = 1100;
    EM_SETOPTIONS = 1101;
    TB_ADDSTRINGW = 1101;
    EM_GETOPTIONS = 1102;
    TB_MAPACCELERATORA = 1102;
    EM_FINDTEXTEX = 1103;
    TB_GETINSERTMARK = 1103;
    EM_GETWORDBREAKPROCEX = 1104;
    TB_SETINSERTMARK = 1104;
    EM_SETWORDBREAKPROCEX = 1105;
    TB_INSERTMARKHITTEST = 1105;
    EM_SETUNDOLIMIT = 1106;
    TB_MOVEBUTTON = 1106;
    TB_GETMAXSIZE = 1107;
    EM_REDO = 1108;
    TB_SETEXTENDEDSTYLE = 1108;
    EM_CANREDO = 1109;
    TB_GETEXTENDEDSTYLE = 1109;
    EM_GETUNDONAME = 1110;
    TB_GETPADDING = 1110;
    EM_GETREDONAME = 1111;
    TB_SETPADDING = 1111;
    EM_STOPGROUPTYPING = 1112;
    TB_SETINSERTMARKCOLOR = 1112;
    EM_SETTEXTMODE = 1113;
    TB_GETINSERTMARKCOLOR = 1113;
    EM_GETTEXTMODE = 1114;
    TB_MAPACCELERATORW = 1114;
    EM_AUTOURLDETECT = 1115;
    TB_GETSTRINGW = 1115;
    EM_GETAUTOURLDETECT = 1116;
    TB_GETSTRINGA = 1116;
    EM_SETPALETTE = 1117;
    EM_GETTEXTEX = 1118;
    EM_GETTEXTLENGTHEX = 1119;
    EM_SHOWSCROLLBAR = 1120;
    EM_SETTEXTEX = 1121;
    TAPI_REPLY = 1123;
    ACM_OPENA = 1124;
    BFFM_SETSTATUSTEXTA = 1124;
    CDM_FIRST = 1124;
    CDM_GETSPEC = 1124;
    EM_SETPUNCTUATION = 1124;
    IPM_CLEARADDRESS = 1124;
    WM_CAP_UNICODE_START = 1124;
    ACM_PLAY = 1125;
    BFFM_ENABLEOK = 1125;
    CDM_GETFILEPATH = 1125;
    EM_GETPUNCTUATION = 1125;
    IPM_SETADDRESS = 1125;
    PSM_SETCURSEL = 1125;
    UDM_SETRANGE = 1125;
    WM_CHOOSEFONT_SETLOGFONT = 1125;
    ACM_STOP = 1126;
    BFFM_SETSELECTIONA = 1126;
    CDM_GETFOLDERPATH = 1126;
    EM_SETWORDWRAPMODE = 1126;
    IPM_GETADDRESS = 1126;
    PSM_REMOVEPAGE = 1126;
    UDM_GETRANGE = 1126;
    WM_CAP_SET_CALLBACK_ERRORW = 1126;
    WM_CHOOSEFONT_SETFLAGS = 1126;
    ACM_OPENW = 1127;
    BFFM_SETSELECTIONW = 1127;
    CDM_GETFOLDERIDLIST = 1127;
    EM_GETWORDWRAPMODE = 1127;
    IPM_SETRANGE = 1127;
    PSM_ADDPAGE = 1127;
    UDM_SETPOS = 1127;
    WM_CAP_SET_CALLBACK_STATUSW = 1127;
    BFFM_SETSTATUSTEXTW = 1128;
    CDM_SETCONTROLTEXT = 1128;
    EM_SETIMECOLOR = 1128;
    IPM_SETFOCUS = 1128;
    PSM_CHANGED = 1128;
    UDM_GETPOS = 1128;
    CDM_HIDECONTROL = 1129;
    EM_GETIMECOLOR = 1129;
    IPM_ISBLANK = 1129;
    PSM_RESTARTWINDOWS = 1129;
    UDM_SETBUDDY = 1129;
    CDM_SETDEFEXT = 1130;
    EM_SETIMEOPTIONS = 1130;
    PSM_REBOOTSYSTEM = 1130;
    UDM_GETBUDDY = 1130;
    EM_GETIMEOPTIONS = 1131;
    PSM_CANCELTOCLOSE = 1131;
    UDM_SETACCEL = 1131;
    EM_CONVPOSITION = 1132;
    PSM_QUERYSIBLINGS = 1132;
    UDM_GETACCEL = 1132;
    MCIWNDM_GETZOOM = 1133;
    PSM_UNCHANGED = 1133;
    UDM_SETBASE = 1133;
    PSM_APPLY = 1134;
    UDM_GETBASE = 1134;
    PSM_SETTITLEA = 1135;
    UDM_SETRANGE32 = 1135;
    PSM_SETWIZBUTTONS = 1136;
    UDM_GETRANGE32 = 1136;
    WM_CAP_DRIVER_GET_NAMEW = 1136;
    PSM_PRESSBUTTON = 1137;
    UDM_SETPOS32 = 1137;
    WM_CAP_DRIVER_GET_VERSIONW = 1137;
    PSM_SETCURSELID = 1138;
    UDM_GETPOS32 = 1138;
    PSM_SETFINISHTEXTA = 1139;
    PSM_GETTABCONTROL = 1140;
    PSM_ISDIALOGMESSAGE = 1141;
    MCIWNDM_REALIZE = 1142;
    PSM_GETCURRENTPAGEHWND = 1142;
    MCIWNDM_SETTIMEFORMATA = 1143;
    PSM_INSERTPAGE = 1143;
    EM_SETLANGOPTIONS = 1144;
    MCIWNDM_GETTIMEFORMATA = 1144;
    PSM_SETTITLEW = 1144;
    WM_CAP_FILE_SET_CAPTURE_FILEW = 1144;
    EM_GETLANGOPTIONS = 1145;
    MCIWNDM_VALIDATEMEDIA = 1145;
    PSM_SETFINISHTEXTW = 1145;
    WM_CAP_FILE_GET_CAPTURE_FILEW = 1145;
    EM_GETIMECOMPMODE = 1146;
    EM_FINDTEXTW = 1147;
    MCIWNDM_PLAYTO = 1147;
    WM_CAP_FILE_SAVEASW = 1147;
    EM_FINDTEXTEXW = 1148;
    MCIWNDM_GETFILENAMEA = 1148;
    EM_RECONVERSION = 1149;
    MCIWNDM_GETDEVICEA = 1149;
    PSM_SETHEADERTITLEA = 1149;
    WM_CAP_FILE_SAVEDIBW = 1149;
    EM_SETIMEMODEBIAS = 1150;
    MCIWNDM_GETPALETTE = 1150;
    PSM_SETHEADERTITLEW = 1150;
    EM_GETIMEMODEBIAS = 1151;
    MCIWNDM_SETPALETTE = 1151;
    PSM_SETHEADERSUBTITLEA = 1151;
    MCIWNDM_GETERRORA = 1152;
    PSM_SETHEADERSUBTITLEW = 1152;
    PSM_HWNDTOINDEX = 1153;
    PSM_INDEXTOHWND = 1154;
    MCIWNDM_SETINACTIVETIMER = 1155;
    PSM_PAGETOINDEX = 1155;
    PSM_INDEXTOPAGE = 1156;
    DL_BEGINDRAG = 1157;
    MCIWNDM_GETINACTIVETIMER = 1157;
    PSM_IDTOINDEX = 1157;
    DL_DRAGGING = 1158;
    PSM_INDEXTOID = 1158;
    DL_DROPPED = 1159;
    PSM_GETRESULT = 1159;
    DL_CANCELDRAG = 1160;
    PSM_RECALCPAGESIZES = 1160;
    MCIWNDM_GET_SOURCE = 1164;
    MCIWNDM_PUT_SOURCE = 1165;
    MCIWNDM_GET_DEST = 1166;
    MCIWNDM_PUT_DEST = 1167;
    MCIWNDM_CAN_PLAY = 1168;
    MCIWNDM_CAN_WINDOW = 1169;
    MCIWNDM_CAN_RECORD = 1170;
    MCIWNDM_CAN_SAVE = 1171;
    MCIWNDM_CAN_EJECT = 1172;
    MCIWNDM_CAN_CONFIG = 1173;
    IE_GETINK = 1174;
    IE_MSGFIRST = 1174;
    MCIWNDM_PALETTEKICK = 1174;
    IE_SETINK = 1175;
    IE_GETPENTIP = 1176;
    IE_SETPENTIP = 1177;
    IE_GETERASERTIP = 1178;
    IE_SETERASERTIP = 1179;
    IE_GETBKGND = 1180;
    IE_SETBKGND = 1181;
    IE_GETGRIDORIGIN = 1182;
    IE_SETGRIDORIGIN = 1183;
    IE_GETGRIDPEN = 1184;
    IE_SETGRIDPEN = 1185;
    IE_GETGRIDSIZE = 1186;
    IE_SETGRIDSIZE = 1187;
    IE_GETMODE = 1188;
    IE_SETMODE = 1189;
    IE_GETINKRECT = 1190;
    WM_CAP_SET_MCI_DEVICEW = 1190;
    WM_CAP_GET_MCI_DEVICEW = 1191;
    WM_CAP_PAL_OPENW = 1204;
    WM_CAP_PAL_SAVEW = 1205;
    IE_GETAPPDATA = 1208;
    IE_SETAPPDATA = 1209;
    IE_GETDRAWOPTS = 1210;
    IE_SETDRAWOPTS = 1211;
    IE_GETFORMAT = 1212;
    IE_SETFORMAT = 1213;
    IE_GETINKINPUT = 1214;
    IE_SETINKINPUT = 1215;
    IE_GETNOTIFY = 1216;
    IE_SETNOTIFY = 1217;
    IE_GETRECOG = 1218;
    IE_SETRECOG = 1219;
    IE_GETSECURITY = 1220;
    IE_SETSECURITY = 1221;
    IE_GETSEL = 1222;
    IE_SETSEL = 1223;
    CDM_LAST = 1224;
    EM_SETBIDIOPTIONS = 1224;
    IE_DOCOMMAND = 1224;
    MCIWNDM_NOTIFYMODE = 1224;
    EM_GETBIDIOPTIONS = 1225;
    IE_GETCOMMAND = 1225;
    EM_SETTYPOGRAPHYOPTIONS = 1226;
    IE_GETCOUNT = 1226;
    EM_GETTYPOGRAPHYOPTIONS = 1227;
    IE_GETGESTURE = 1227;
    MCIWNDM_NOTIFYMEDIA = 1227;
    EM_SETEDITSTYLE = 1228;
    IE_GETMENU = 1228;
    EM_GETEDITSTYLE = 1229;
    IE_GETPAINTDC = 1229;
    MCIWNDM_NOTIFYERROR = 1229;
    IE_GETPDEVENT = 1230;
    IE_GETSELCOUNT = 1231;
    IE_GETSELITEMS = 1232;
    IE_GETSTYLE = 1233;
    MCIWNDM_SETTIMEFORMATW = 1243;
    EM_OUTLINE = 1244;
    MCIWNDM_GETTIMEFORMATW = 1244;
    EM_GETSCROLLPOS = 1245;
    EM_SETSCROLLPOS = 1246;
    EM_SETFONTSIZE = 1247;
    EM_GETZOOM = 1248;
    MCIWNDM_GETFILENAMEW = 1248;
    EM_SETZOOM = 1249;
    MCIWNDM_GETDEVICEW = 1249;
    EM_GETVIEWKIND = 1250;
    EM_SETVIEWKIND = 1251;
    EM_GETPAGE = 1252;
    MCIWNDM_GETERRORW = 1252;
    EM_SETPAGE = 1253;
    EM_GETHYPHENATEINFO = 1254;
    EM_SETHYPHENATEINFO = 1255;
    EM_GETPAGEROTATE = 1259;
    EM_SETPAGEROTATE = 1260;
    EM_GETCTFMODEBIAS = 1261;
    EM_SETCTFMODEBIAS = 1262;
    EM_GETCTFOPENSTATUS = 1264;
    EM_SETCTFOPENSTATUS = 1265;
    EM_GETIMECOMPTEXT = 1266;
    EM_ISIME = 1267;
    EM_GETIMEPROPERTY = 1268;
    EM_GETQUERYRTFOBJ = 1293;
    EM_SETQUERYRTFOBJ = 1294;
    FM_GETFOCUS = 1536;
    FM_GETDRIVEINFOA = 1537;
    FM_GETSELCOUNT = 1538;
    FM_GETSELCOUNTLFN = 1539;
    FM_GETFILESELA = 1540;
    FM_GETFILESELLFNA = 1541;
    FM_REFRESH_WINDOWS = 1542;
    FM_RELOAD_EXTENSIONS = 1543;
    FM_GETDRIVEINFOW = 1553;
    FM_GETFILESELW = 1556;
    FM_GETFILESELLFNW = 1557;
    WLX_WM_SAS = 1625;
    SM_GETSELCOUNT = 2024;
    UM_GETSELCOUNT = 2024;
    WM_CPL_LAUNCH = 2024;
    SM_GETSERVERSELA = 2025;
    UM_GETUSERSELA = 2025;
    WM_CPL_LAUNCHED = 2025;
    SM_GETSERVERSELW = 2026;
    UM_GETUSERSELW = 2026;
    SM_GETCURFOCUSA = 2027;
    UM_GETGROUPSELA = 2027;
    SM_GETCURFOCUSW = 2028;
    UM_GETGROUPSELW = 2028;
    SM_GETOPTIONS = 2029;
    UM_GETCURFOCUSA = 2029;
    UM_GETCURFOCUSW = 2030;
    UM_GETOPTIONS = 2031;
    UM_GETOPTIONS2 = 2032;
    LVM_FIRST = 4096;
    LVM_GETBKCOLOR = 4096;
    LVM_SETBKCOLOR = 4097;
    LVM_GETIMAGELIST = 4098;
    LVM_SETIMAGELIST = 4099;
    LVM_GETITEMCOUNT = 4100;
    LVM_GETITEMA = 4101;
    LVM_SETITEMA = 4102;
    LVM_INSERTITEMA = 4103;
    LVM_DELETEITEM = 4104;
    LVM_DELETEALLITEMS = 4105;
    LVM_GETCALLBACKMASK = 4106;
    LVM_SETCALLBACKMASK = 4107;
    LVM_GETNEXTITEM = 4108;
    LVM_FINDITEMA = 4109;
    LVM_GETITEMRECT = 4110;
    LVM_SETITEMPOSITION = 4111;
    LVM_GETITEMPOSITION = 4112;
    LVM_GETSTRINGWIDTHA = 4113;
    LVM_HITTEST = 4114;
    LVM_ENSUREVISIBLE = 4115;
    LVM_SCROLL = 4116;
    LVM_REDRAWITEMS = 4117;
    LVM_ARRANGE = 4118;
    LVM_EDITLABELA = 4119;
    LVM_GETEDITCONTROL = 4120;
    LVM_GETCOLUMNA = 4121;
    LVM_SETCOLUMNA = 4122;
    LVM_INSERTCOLUMNA = 4123;
    LVM_DELETECOLUMN = 4124;
    LVM_GETCOLUMNWIDTH = 4125;
    LVM_SETCOLUMNWIDTH = 4126;
    LVM_GETHEADER = 4127;
    LVM_CREATEDRAGIMAGE = 4129;
    LVM_GETVIEWRECT = 4130;
    LVM_GETTEXTCOLOR = 4131;
    LVM_SETTEXTCOLOR = 4132;
    LVM_GETTEXTBKCOLOR = 4133;
    LVM_SETTEXTBKCOLOR = 4134;
    LVM_GETTOPINDEX = 4135;
    LVM_GETCOUNTPERPAGE = 4136;
    LVM_GETORIGIN = 4137;
    LVM_UPDATE = 4138;
    LVM_SETITEMSTATE = 4139;
    LVM_GETITEMSTATE = 4140;
    LVM_GETITEMTEXTA = 4141;
    LVM_SETITEMTEXTA = 4142;
    LVM_SETITEMCOUNT = 4143;
    LVM_SORTITEMS = 4144;
    LVM_SETITEMPOSITION32 = 4145;
    LVM_GETSELECTEDCOUNT = 4146;
    LVM_GETITEMSPACING = 4147;
    LVM_GETISEARCHSTRINGA = 4148;
    LVM_SETICONSPACING = 4149;
    LVM_SETEXTENDEDLISTVIEWSTYLE = 4150;
    LVM_GETEXTENDEDLISTVIEWSTYLE = 4151;
    LVM_GETSUBITEMRECT = 4152;
    LVM_SUBITEMHITTEST = 4153;
    LVM_SETCOLUMNORDERARRAY = 4154;
    LVM_GETCOLUMNORDERARRAY = 4155;
    LVM_SETHOTITEM = 4156;
    LVM_GETHOTITEM = 4157;
    LVM_SETHOTCURSOR = 4158;
    LVM_GETHOTCURSOR = 4159;
    LVM_APPROXIMATEVIEWRECT = 4160;
    LVM_SETWORKAREAS = 4161;
    LVM_GETSELECTIONMARK = 4162;
    LVM_SETSELECTIONMARK = 4163;
    LVM_SETBKIMAGEA = 4164;
    LVM_GETBKIMAGEA = 4165;
    LVM_GETWORKAREAS = 4166;
    LVM_SETHOVERTIME = 4167;
    LVM_GETHOVERTIME = 4168;
    LVM_GETNUMBEROFWORKAREAS = 4169;
    LVM_SETTOOLTIPS = 4170;
    LVM_GETITEMW = 4171;
    LVM_SETITEMW = 4172;
    LVM_INSERTITEMW = 4173;
    LVM_GETTOOLTIPS = 4174;
    LVM_FINDITEMW = 4179;
    LVM_GETSTRINGWIDTHW = 4183;
    LVM_GETCOLUMNW = 4191;
    LVM_SETCOLUMNW = 4192;
    LVM_INSERTCOLUMNW = 4193;
    LVM_GETITEMTEXTW = 4211;
    LVM_SETITEMTEXTW = 4212;
    LVM_GETISEARCHSTRINGW = 4213;
    LVM_EDITLABELW = 4214;
    LVM_GETBKIMAGEW = 4235;
    LVM_SETSELECTEDCOLUMN = 4236;
    LVM_SETTILEWIDTH = 4237;
    LVM_SETVIEW = 4238;
    LVM_GETVIEW = 4239;
    LVM_INSERTGROUP = 4241;
    LVM_SETGROUPINFO = 4243;
    LVM_GETGROUPINFO = 4245;
    LVM_REMOVEGROUP = 4246;
    LVM_MOVEGROUP = 4247;
    LVM_MOVEITEMTOGROUP = 4250;
    LVM_SETGROUPMETRICS = 4251;
    LVM_GETGROUPMETRICS = 4252;
    LVM_ENABLEGROUPVIEW = 4253;
    LVM_SORTGROUPS = 4254;
    LVM_INSERTGROUPSORTED = 4255;
    LVM_REMOVEALLGROUPS = 4256;
    LVM_HASGROUP = 4257;
    LVM_SETTILEVIEWINFO = 4258;
    LVM_GETTILEVIEWINFO = 4259;
    LVM_SETTILEINFO = 4260;
    LVM_GETTILEINFO = 4261;
    LVM_SETINSERTMARK = 4262;
    LVM_GETINSERTMARK = 4263;
    LVM_INSERTMARKHITTEST = 4264;
    LVM_GETINSERTMARKRECT = 4265;
    LVM_SETINSERTMARKCOLOR = 4266;
    LVM_GETINSERTMARKCOLOR = 4267;
    LVM_SETINFOTIP = 4269;
    LVM_GETSELECTEDCOLUMN = 4270;
    LVM_ISGROUPVIEWENABLED = 4271;
    LVM_GETOUTLINECOLOR = 4272;
    LVM_SETOUTLINECOLOR = 4273;
    LVM_CANCELEDITLABEL = 4275;
    LVM_MAPINDEXTOID = 4276;
    LVM_MAPIDTOINDEX = 4277;
    LVM_ISITEMVISIBLE = 4278;
    OCM__BASE = 8192;
    LVM_SETUNICODEFORMAT = 8197;
    LVM_GETUNICODEFORMAT = 8198;
    OCM_CTLCOLOR = 8217;
    OCM_DRAWITEM = 8235;
    OCM_MEASUREITEM = 8236;
    OCM_DELETEITEM = 8237;
    OCM_VKEYTOITEM = 8238;
    OCM_CHARTOITEM = 8239;
    OCM_COMPAREITEM = 8249;
    OCM_NOTIFY = 8270;
    OCM_COMMAND = 8465;
    OCM_HSCROLL = 8468;
    OCM_VSCROLL = 8469;
    OCM_CTLCOLORMSGBOX = 8498;
    OCM_CTLCOLOREDIT = 8499;
    OCM_CTLCOLORLISTBOX = 8500;
    OCM_CTLCOLORBTN = 8501;
    OCM_CTLCOLORDLG = 8502;
    OCM_CTLCOLORSCROLLBAR = 8503;
    OCM_CTLCOLORSTATIC = 8504;
    OCM_PARENTNOTIFY = 8720;
    WM_APP = 32768;
    WM_RASDIALEVENT = 52429;
}

enum RemoveMsg {
    PM_NOREMOVE;
    PM_REMOVE;
    PM_NOYIELD;
}

struct RECT {
    left: s32;
    top: s32;
    right: s32;
    bottom: s32;
}

enum SWPFlags {
    SWP_NOSIZE         = 0x1;
    SWP_NOMOVE         = 0x2;
    SWP_NOZORDER       = 0x4;
    SWP_NOREDRAW       = 0x8;
    SWP_NOACTIVATE     = 0x10;
    SWP_DRAWFRAME      = 0x20;
    SWP_FRAMECHANGED   = 0x20;
    SWP_SHOWWINDOW     = 0x40;
    SWP_HIDEWINDOW     = 0x80;
    SWP_NOCOPYBITS     = 0x100;
    SWP_NOOWNERZORDER  = 0x200;
    SWP_NOREPOSITION   = 0x200;
    SWP_NOSENDCHANGING = 0x400;
    SWP_DEFERASE       = 0x2000;
    SWP_ASYNCWINDOWPOS = 0x4000;
}

struct MARGINS {
    cxLeftWidth: s32;
    cxRightWidth: s32;
    cyTopHeight: s32;
    cyBottomHeight: s32;
}

struct NCCALCSIZE_PARAMS {
    rgrc: CArray<RECT>[3];
    lppos: WINDOWPOS;
}

struct WINDOWPOS {
    hwnd: Handle*;
    hwndInsertAfter: Handle*;
    x: s32;
    y: s32;
    cx: s32;
    cy: s32;
    flags: SWPFlags;
}

enum NtStatus : u32 {
    STATUS_SUCCESS           = 0x0;
    STATUS_INVALID_HANDLE    = 0xC0000008;
    STATUS_INVALID_PARAMETER = 0xC000000D;
    STATUS_NO_MEMORY         = 0xC0000017;
    STATUS_NOT_FOUND         = 0xC0000225;
}

[flags]
enum MouseButtonMod {
    MK_LBUTTON  = 0x1;
    MK_RBUTTON  = 0x2;
    MK_SHIFT    = 0x4;
    MK_CONTROL  = 0x8;
    MK_MBUTTON  = 0x10;
    MK_XBUTTON1 = 0x20;
    MK_XBUTTON2 = 0x40;
}

struct SYSTEM_INFO {
    DUMMYUNIONNAME: int;
    dwPageSize: int;
    lpMinimumApplicationAddress: void*;
    lpMaximumApplicationAddress: void*;
    dwActiveProcessorMask: int*;
    dwNumberOfProcessors: int;
    dwProcessorType: int;
    dwAllocationGranularity: int;
    wProcessorLevel: s16;
    wProcessorRevision: s16;
}

struct SYSTEMTIME {
    wYear: u16;
    wMonth: u16;
    wDayOfWeek: u16;
    wDay: u16;
    wHour: u16;
    wMinute: u16;
    wSecond: u16;
    wMilliseconds: u16;
}

INFINITE := -1; #const

enum ClipboardFormat : u32 {
    CF_TEXT         = 1;
    CF_BITMAP       = 2;
    CF_METAFILEPICT = 3;
    CF_SYLK         = 4;
    CF_DIF          = 5;
    CF_TIFF         = 6;
    CF_OEMTEXT      = 7;
    CF_DIB          = 8;
    CF_PALETTE      = 9;
    CF_PENDATA      = 10;
    CF_RIFF         = 11;
    CF_WAVE         = 12;
    CF_UNICODETEXT  = 13;
    CF_ENHMETAFILE  = 14;
}


// Virtual key codes
VK_LBUTTON: u8 = 0x01; #const
VK_RBUTTON: u8 = 0x02; #const
VK_CANCEL: u8 = 0x03; #const
VK_MBUTTON: u8 = 0x04; #const
VK_XBUTTON1: u8 = 0x05; #const
VK_XBUTTON2: u8 = 0x06; #const
VK_BACK: u8 = 0x08; #const
VK_TAB: u8 = 0x09; #const
VK_CLEAR: u8 = 0x0C; #const
VK_RETURN: u8 = 0x0D; #const
VK_SHIFT: u8 = 0x10; #const
VK_CONTROL: u8 = 0x11; #const
VK_MENU: u8 = 0x12; #const // ALT key
VK_PAUSE: u8 = 0x13; #const
VK_CAPITAL: u8 = 0x14; #const
VK_KANA: u8 = 0x15; #const
VK_HANGUEL: u8 = 0x15; #const
VK_HANGUL: u8 = 0x15; #const
VK_IME_ON: u8 = 0x16; #const
VK_JUNJA: u8 = 0x17; #const
VK_FINAL: u8 = 0x18; #const
VK_HANJA: u8 = 0x19; #const
VK_KANJI: u8 = 0x19; #const
VK_IME_OFF: u8 = 0x1A; #const
VK_ESCAPE: u8 = 0x1B; #const
VK_CONVERT: u8 = 0x1C; #const
VK_NONCONVERT: u8 = 0x1D; #const
VK_ACCEPT: u8 = 0x1E; #const
VK_MODECHANGE: u8 = 0x1F; #const
VK_SPACE: u8 = 0x20; #const
VK_PRIOR: u8 = 0x21; #const
VK_NEXT: u8 = 0x22; #const
VK_END: u8 =  0x23; #const
VK_HOME: u8 = 0x24; #const
VK_LEFT: u8 = 0x25; #const
VK_UP: u8 = 0x26; #const
VK_RIGHT: u8 = 0x27; #const
VK_DOWN: u8 = 0x28; #const
VK_SELECT: u8 = 0x29; #const
VK_PRINT: u8 = 0x2A; #const
VK_EXECUTE: u8 = 0x2B; #const
VK_SNAPSHOT: u8 = 0x2C; #const
VK_INSERT: u8 = 0x2D; #const
VK_DELETE: u8 = 0x2E; #const
VK_HELP: u8 = 0x2F; #const
VK_LWIN: u8 = 0x5B; #const
VK_RWIN: u8 = 0x5C; #const
VK_APPS: u8 = 0x5D; #const
VK_SLEEP: u8 = 0x5F; #const
VK_NUMPAD0: u8 = 0x60; #const
VK_NUMPAD1: u8 = 0x61; #const
VK_NUMPAD2: u8 = 0x62; #const
VK_NUMPAD3: u8 = 0x63; #const
VK_NUMPAD4: u8 = 0x64; #const
VK_NUMPAD5: u8 = 0x65; #const
VK_NUMPAD6: u8 = 0x66; #const
VK_NUMPAD7: u8 = 0x67; #const
VK_NUMPAD8: u8 = 0x68; #const
VK_NUMPAD9: u8 = 0x69; #const
VK_MULTIPLY: u8 = 0x6A; #const
VK_ADD: u8 = 0x6B; #const
VK_SEPARATOR: u8 = 0x6C; #const
VK_SUBTRACT: u8 = 0x6D; #const
VK_DECIMAL: u8 = 0x6E; #const
VK_DIVIDE: u8 = 0x6F; #const
VK_F1: u8 = 0x70; #const
VK_F2: u8 = 0x71; #const
VK_F3: u8 = 0x72; #const
VK_F4: u8 = 0x73; #const
VK_F5: u8 = 0x74; #const
VK_F6: u8 = 0x75; #const
VK_F7: u8 = 0x76; #const
VK_F8: u8 = 0x77; #const
VK_F9: u8 = 0x78; #const
VK_F10: u8 = 0x79; #const
VK_F11: u8 = 0x7A; #const
VK_F12: u8 = 0x7B; #const
VK_F13: u8 = 0x7C; #const
VK_F14: u8 = 0x7D; #const
VK_F15: u8 = 0x7E; #const
VK_F16: u8 = 0x7F; #const
VK_F17: u8 = 0x80; #const
VK_F18: u8 = 0x81; #const
VK_F19: u8 = 0x82; #const
VK_F20: u8 = 0x83; #const
VK_F21: u8 = 0x84; #const
VK_F22: u8 = 0x85; #const
VK_F23: u8 = 0x86; #const
VK_F24: u8 = 0x87; #const
VK_NUMLOCK: u8 = 0x90; #const
VK_SCROLL: u8 = 0x91; #const
VK_LSHIFT: u8 = 0xA0; #const
VK_RSHIFT: u8 = 0xA1; #const
VK_LCONTROL: u8 = 0xA2; #const
VK_RCONTROL: u8 = 0xA3; #const
VK_LMENU: u8 = 0xA4; #const
VK_RMENU: u8 = 0xA5; #const
VK_BROWSER_BACK: u8 = 0xA6; #const
VK_BROWSER_FORWARD: u8 = 0xA7; #const
VK_BROWSER_REFRESH: u8 = 0xA8; #const
VK_BROWSER_STOP: u8 = 0xA9; #const
VK_BROWSER_SEARCH: u8 = 0xAA; #const
VK_BROWSER_FAVORITES: u8 = 0xAB; #const
VK_BROWSER_HOME: u8 = 0xAC; #const
VK_VOLUME_MUTE: u8 = 0xAD; #const
VK_VOLUME_DOWN: u8 = 0xAE; #const
VK_VOLUME_UP: u8 = 0xAF; #const
VK_MEDIA_NEXT_TRACK: u8 = 0xB0; #const
VK_MEDIA_PREV_TRACK: u8 = 0xB1; #const
VK_MEDIA_STOP: u8 = 0xB2; #const
VK_MEDIA_PLAY_PAUSE: u8 = 0xB3; #const
VK_LAUNCH_MAIL: u8 = 0xB4; #const
VK_LAUNCH_MEDIA_SELECT: u8 = 0xB5; #const
VK_LAUNCH_APP1: u8 = 0xB6; #const
VK_LAUNCH_APP2: u8 = 0xB7; #const
VK_OEM_1: u8 = 0xBA; #const // ';:' key
VK_OEM_PLUS: u8 = 0xBB; #const
VK_OEM_COMMA: u8 = 0xBC; #const
VK_OEM_MINUS: u8 = 0xBD; #const
VK_OEM_PERIOD: u8 = 0xBE; #const
VK_OEM_2: u8 = 0xBF; #const // '/?' key
VK_OEM_3: u8 = 0xC0; #const // '`~' key
VK_OEM_4: u8 = 0xDB; #const // '[{' key
VK_OEM_5: u8 = 0xDC; #const // '\|' key
VK_OEM_6: u8 = 0xDD; #const // ']}' key
VK_OEM_7: u8 = 0xDE; #const // 'single-quote/double-quote' key
VK_OEM_8: u8 = 0xDF; #const
VK_OEM_102: u8 = 0xE2; #const
VK_PROCESSKEY: u8 = 0xE5; #const
VK_PACKET: u8 = 0xE7; #const
VK_ATTN: u8 = 0xF6; #const
VK_CRSEL: u8 = 0xF7; #const
VK_EXSEL: u8 = 0xF8; #const
VK_EREOF: u8 = 0xF9; #const
VK_PLAY: u8 = 0xFA; #const
VK_ZOOM: u8 = 0xFB; #const
VK_NONAME: u8 = 0xFC; #const
VK_PA1: u8 = 0xFD; #const
VK_OEM_CLEAR: u8 = 0xFE; #const
