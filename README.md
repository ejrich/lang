# lang

## This is an experiment for writing a compiler from scratch in .Net

### Language Features

* Strongly typed
* Native code with no runtime/GC
* Combines the strengths of C# with the performance of C
* Full C/C++ interoperability
* Concise standard library
* Modules and packages
* Build tools and project files, not makefiles

### How to build this project

* First install the [.Net 5 SDK](https://dotnet.microsoft.com/download)
* Run `dotnet restore`
* Once the dependencies are installed, run `dotnet build --no-restore`
    * If using vim, this can be run using `<leader><F8>`
* The executable can be found in `Lang/bin/{Debug|Release}/net5.0/Lang`

### Syntax

Examples for syntax can be found in the `Example` and `Tests` folders.

### Tests

The tests in the `Tests/tests` directory can be run by first compiling the test project and then running the executable in `Tests/bin`.

The tests should always be run when making changes to the compiler for regression testing.

### Support

This is a personal project, so no guarantees. Supporting Linux only for now, Windows eventually since it's not my main OS.
