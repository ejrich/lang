#import compiler
#import "files.ol"

main() {
    tests_dir := "test/tests"; #const
    dir := opendir(tests_dir);
    command_buffer = malloc(1000);
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
        free(test_dir.data);

        closedir(dir);
    }
    free(command_buffer);

    if failed_test_count {
        printf("\n%d Test(s) Failed\n", failed_test_count);
        exit_code = -1;
    }
    else {
        printf("\nAll Tests Passed\n");
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

string format_string(string format, Params<string> args) {
    if format.length == 0 return "";

    // @Cleanup This is not good, figure out a better way to do this
    str: string = {data = cast(u8*, malloc(100));}
    arg_index := 0;
    format_index := 0;

    while format_index < format.length {
        char := format[format_index];
        if char == '%' {
            arg := args[arg_index++];
            each i in 0..arg.length-1 {
                str[str.length++] = arg[i];
            }
        }
        else {
            str[str.length++] = char;
        }

        format_index++;
    }

    str[str.length] = 0;

    return str;
}

bool run_test(string test_dir, string test) {
    executable := "./ol/bin/Debug/net6.0/ol"; #const

    command := format_string("% %/%.ol", executable, test_dir, test);
    printf("Compiling: %s", command);
    compiler_exit_code := run_command(command);
    free(command.data);

    if compiler_exit_code {
        printf(" -- Test Failed\n");
        return false;
    }

    bin_dir := format_string("%/bin", test_dir);
    dir := opendir(bin_dir.data);
    if dir == null {
        printf(" -- Test Failed: Unable to open directory '%s'\n", bin_dir.data);
        free(bin_dir.data);
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
        printf(" -- Test Failed: Executable not found in directory '%s'\n", bin_dir.data);
        free(bin_dir.data);
        return false;
    }

    printf("\nRunning: %s", command);
    run_exit_code := run_command(command);
    free(command.data);

    if run_exit_code {
        printf(" -- Test Failed\n");
        return false;
    }
    printf("\n");
    return true;
}

command_buffer: void*;

int run_command(string command) {
    handle := popen(command, "r");
    if handle == null {
        printf(" -- Test Failed: Unable to run '%s'\n", command);
        return -1;
    }

    while fgets(cast(u8*, command_buffer), 1000, handle) != null {}

    status := pclose(handle);
    return (status & 0xFF00) >> 8;
}

#run {
    set_executable_name("tests");
    // main();
}
