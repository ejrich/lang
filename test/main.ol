#import standard
#import compiler

main() {
    tests_dir := "test/tests"; #const
    command_buffer = default_allocator(1000);
    failed_test_count := 0;

    success, files := get_files_in_directory(tests_dir);
    if success {
        each file in files {
            if file.type == FileType.Directory && file.name != "." && file.name != ".." {
                test_dir := format_string("%/%", tests_dir, file.name);

                if !run_test(test_dir, file.name) failed_test_count++;

                default_free(test_dir.data);
            }
        }
    }
    default_free(command_buffer);

    if failed_test_count {
        print("\n% Test(s) Failed\n", failed_test_count);
        exit_code = -1;
    }
    else {
        print("\nAll Tests Passed\n");
    }
}

bool run_test(string test_dir, string test) {
    executable := "./ol/bin/Debug/net6.0/ol"; #const

    command := format_string("% %/%.ol", executable, test_dir, test);
    print("Compiling: %", command);
    compiler_exit_code := run_command(command);
    default_free(command.data);

    if compiler_exit_code {
        print(" -- Test Failed\n");
        return false;
    }

    command = format_string("./%/bin/%", test_dir, test);

    print("\nRunning: %", command);
    run_exit_code := run_command(command);
    default_free(command.data);

    if run_exit_code {
        print(" -- Test Failed\n");
        return false;
    }
    print("\n");
    return true;
}

command_buffer: void*;

int run_command(string command) {
    #if os == OS.Windows {
        sa: SecurityAttributes = { nLength = size_of(SecurityAttributes); bInheritHandle = true; }
        stdOutRd, stdOutWr: Handle*;

        if !CreatePipe(&stdOutRd, &stdOutWr, &sa, 0) {
            return -1;
        }
        SetHandleInformation(stdOutRd, HandleFlags.HANDLE_FLAG_INHERIT, HandleFlags.None);

        si: StartupInfo = { cbSize = size_of(StartupInfo); dwFlags = 0x100; hStdInput = GetStdHandle(STD_INPUT_HANDLE); hStdOutput = stdOutWr; }
        pi: ProcessInformation;

        if !CreateProcessA(null, command, null, null, true, 0, null, null, &si, &pi) {
            CloseHandle(hStdOutRd);
            CloseHandle(hStdOutWr);
            return -1;
        }

        buf: CArray<u8>[1000];
        while true {
            read: int;
            success := ReadFile(stdOutRd, &buf, 1000, &read, null);

            if !success || read == 0 break;
        }

        status: int;
        GetExitCodeProcess(pi.hProcess, &status);

        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        CloseHandle(hStdOutRd);
        CloseHandle(hStdOutWr);
    }
    else {
        command = format_string("% > /dev/null", command);

        status := system(command);
        default_free(command.data);

        return status;
    }
}

#if os != OS.Windows {
    int system(string command) #extern "c"
}

#run {
    set_executable_name("tests");

    if os != OS.Windows {
        set_linker(LinkerType.Dynamic);
    }
}
