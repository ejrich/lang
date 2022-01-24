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
    handle := popen(command, "r");
    if handle == null {
        print(" -- Test Failed: Unable to run '%'\n", command);
        return -1;
    }

    while fgets(cast(u8*, command_buffer), 1000, handle) != null {}

    return pclose(handle);
}

struct FILE {}

FILE* popen(string command, string type) #extern "c"
int pclose(FILE* stream) #extern "c"
u8* fgets(u8* buffer, int n, FILE* stream) #extern "c"

#run {
    set_executable_name("tests");
    set_linker(LinkerType.Dynamic);
    // main();
}
