int main() {
    tests_dir := "Tests/tests"; #const
    dir := opendir(tests_dir);
    failed_test_count := 0;
    if dir {
        file := readdir(dir);
        test_dir: string;
        while file {
            if file.d_type == FileType.DT_DIR && file.d_reclen == 32 {
                dir_name := get_file_name(file);

                test_dir = format_string("%/%", tests_dir, dir_name);

                if !run_test(test_dir) then failed_test_count++;
            }

            file = readdir(dir);
        }
        free(test_dir.data);

        closedir(dir);
    }

    if failed_test_count {
        printf("\n%d Test(s) Failed\n", failed_test_count);
        return -1;
    }
    else {
        printf("\nAll Tests Passed\n");
        return 0;
    }
}

string get_file_name(dirent* file) {
    name: string = { data = file.d_name.data; }

    each i in 0..file.d_name.length - 1 {
        if file.d_name[i] != 0 then name.length++;
        else then return name;
    }
    return name;
}

string format_string(string format, Params<string> args) {
    if format.length == 0 then return "";

    // @Cleanup This is not good, figure out a better way to do this
    buffer := malloc(100);

    str: string = {data = buffer;}
    arg_index := 0;
    format_index := 0;

    while format_index < format.length {
        char := format[format_index];
        if char == 37 { // TODO Have a character syntax
            arg := args[arg_index++];
            each i in 0..arg.length - 1 {
                pointer := buffer + str.length++;
                *pointer = arg[i]; // TODO Add indexing for pointers
            }
        }
        else {
            pointer := buffer + str.length++;
            *pointer = char;
        }

        format_index++;
    }

    return str;
}

bool run_test(string test_dir) {
    executable := "./Lang/bin/Debug/net5.0/Lang"; #const

    command := format_string("% %", executable, test_dir);
    printf("Compiling: %s", command);
    exit_code := run_command(command);
    // return true;
    // free(command.data);

    if exit_code {
        printf(" -- Test Failed\n");
        return false;
    }

    bin_dir := format_string("%/bin", test_dir);
    dir := opendir(bin_dir.data);
    if dir == null {
        printf(" -- Test Failed: Unable to open directory '%s'\n", bin_dir.data);
        // free(bin_dir.data);
        return false;
    }

    file := readdir(dir);
    found_executable := false;
    while !found_executable && file != null {
        if file.d_type == FileType.DT_REG {
            file_name := get_file_name(file);
            command = format_string("./%s/%s", bin_dir, file_name);
            found_executable = true;
        }

        file = readdir(dir);
    }

    closedir(dir);

    if !found_executable {
        printf(" -- Test Failed: Executable not found in directory '%s'\n", bin_dir.data);
        // free(bin_dir.data);
        return false;
    }

    printf("\nRunning: %s", command);
    exit_code = run_command(command);
    // free(command.data);

    if exit_code {
        printf(" -- Test Failed\n");
        return false;
    }
    printf("\n");
    return true;
}

int run_command(string command) {
    handle := popen(command, "r");
    if handle == null {
        printf(" -- Test Failed: Unable to run '%s'\n", command);
        return -1;
    }

    buffer := malloc(1000);
    while fgets(buffer, 1000, handle) != null {}
    free(buffer);

    status := pclose(handle);
    return (status & 0xFF00) >> 8;
}

// #run main();
