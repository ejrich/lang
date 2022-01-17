#import standard
#import compiler

main() {
    tests_dir := "test/tests"; #const
    dir := opendir(tests_dir);
    command_buffer = default_allocator(1000);
    failed_test_count := 0;
    if dir {
        file := readdir(dir);
        test_dir: string;
        while file {
            dir_name := get_file_name(file);
            if file.d_type == FileType.DT_DIR && dir_name != "." && dir_name != ".." {
                test_dir = format_string("%/%", tests_dir, dir_name);

                if !run_test(test_dir, dir_name) failed_test_count++;
            }

            file = readdir(dir);
        }
        default_free(test_dir.data);

        closedir(dir);
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

string get_file_name(dirent* file) {
    name: string = { data = &file.d_name; }

    each i in 0..D_NAME_LENGTH-1 {
        if file.d_name[i] != 0 name.length++;
        else return name;
    }
    return name;
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

    bin_dir := format_string("%/bin", test_dir);
    dir := opendir(bin_dir.data);
    if dir == null {
        print(" -- Test Failed: Unable to open directory '%'\n", bin_dir.data);
        default_free(bin_dir.data);
        return false;
    }

    file := readdir(dir);
    found_executable := false;
    while !found_executable && file != null {
        if file.d_type == FileType.DT_REG {
            file_name := get_file_name(file);
            command = format_string("./%/%", bin_dir, file_name);
            found_executable = true;
        }

        file = readdir(dir);
    }

    closedir(dir);

    if !found_executable {
        print(" -- Test Failed: Executable not found in directory '%'\n", bin_dir.data);
        default_free(bin_dir.data);
        return false;
    }

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

FILE* popen(string command, string type) #extern "c"
int pclose(FILE* stream) #extern "c"
u8* fgets(u8* buffer, int n, FILE* stream) #extern "c"

#run {
    set_executable_name("tests");
    // main();
}
