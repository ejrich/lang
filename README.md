# ol

## This is an experiment for writing a compiler from scratch in .Net

### Language Features

* Strongly typed
* Native code with no runtime/GC
* Full C interoperability
* Ability to execute any code during compilation
* Built in runtime type information
* Simple polymorphism for structs, functions, and operator overloading
* Concise standard library
* API to define the program _with_ the program, not makefiles

### How to build and use this project

* Download [NASM](https://www.nasm.us/) to build the runtime binary
* Build the runtime object file, this contains the program entrypoint `_start`
    * Linux - run `./build_runtime.sh`
    * Windows - run `.\build_runtime.bat`
* __FOR WINDOWS ONLY__ - Download the [Windows SDK](https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/) (can also be installed with Visual Studio)
* Install the [.Net 8 SDK](https://dotnet.microsoft.com/download)
* Run `dotnet restore`
* Once the dependencies are installed, run `dotnet build --no-restore`
    * If using vim, this can be run using `<leader><F8>`
* The executable can be found in `ol/bin/{Debug|Release}/net8.0/ol`

#### GitHub users

* The GitHub repo does not contain all of the binaries necessary to run the project.
    * Linux - Download version 19.1 of [LLVM](https://github.com/llvm/llvm-project/releases/tag/llvmorg-19.1.0) and copy `libLLVM.so` to `ol/dist/linux/libLLVM.so.19.1` and rebuild.
    * Windows - Download version 19.1 of [LLVM](https://github.com/llvm/llvm-project/releases/tag/llvmorg-19.1.0) and copy `LLVM-C.dll` and `lld-link.exe` to `ol/dist/windows` and rebuild.

### Syntax

Examples for syntax can be found in the `Example` and `Tests` folders.

### Tests

The tests in the `test/tests` directory can be run by first compiling the test project and then running the executable in `tests/bin`.

The tests should always be run when making changes to the compiler for regression testing.

### Support

This is a personal project, so no guarantees. Supporting Linux/Windows for now, Mac eventually since it's not my main OS.
