int main() {
    tests_dir := "Tests/tests"; #const
    dir := opendir(tests_dir);
    failed_test_count := 0;
    if dir {
        file := readdir(dir);
        while file {
            if file.d_type == FileType.DT_DIR && file.d_reclen == 32 {
                test_dir: List<u8>[50];
                sprintf(test_dir.data, "%s/%s", tests_dir, &file.d_name);

                if !run_test(test_dir.data) then failed_test_count++;
            }

            file = readdir(dir);
        }

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

bool run_test(u8* test_dir) {
    executable := "./Lang/bin/Debug/net5.0/Lang"; #const

    command: List<u8>[100];
    sprintf(command.data, "%s %s", executable, test_dir);
    printf("Compiling: %s", command.data);
    exit_code := run_command(command.data);

    if exit_code {
        printf(" -- Test Failed\n");
        return false;
    }

    bin_dir: List<u8>[50];
    sprintf(bin_dir.data, "%s/bin", test_dir);
    dir := opendir(bin_dir.data);
    if dir == null {
        printf(" -- Test Failed: Unable to open directory '%s'\n", bin_dir.data);
        return false;
    }

    file := readdir(dir);
    found_executable := false;
    while !found_executable && file != null {
        if file.d_type == FileType.DT_REG {
            sprintf(command.data, "./%s/%s", bin_dir.data, &file.d_name);
            found_executable = true;
        }

        file = readdir(dir);
    }

    closedir(dir);

    if !found_executable {
        printf(" -- Test Failed: Executable not found in directory '%s'\n", bin_dir.data);
        return false;
    }

    printf("\nRunning: %s", command.data);
    exit_code = run_command(command.data);

    if exit_code {
        printf(" -- Test Failed\n");
        return false;
    }
    printf("\n");
    return true;
}

int run_command(u8* command) {
    handle := popen(command, "r");
    if handle == null {
        printf(" -- Test Failed: Unable to run '%s'\n", command);
        return -1;
    }

    buffer: List<u8>[50];
    while fgets(buffer.data, 50, handle) != null {}

    status := pclose(handle);
    return (status & 0xFF00) >> 8;
}

// #run main();
