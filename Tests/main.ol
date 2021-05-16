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
    sprintf(command.data, "%s %s --no-stats", executable, test_dir);
    printf("Compiling: %s\n", command.data);
    exit_code := system(command.data);

    if exit_code {
        printf("Test Failed\n");
        return false;
    }

    sprintf(command.data, "./%s/bin/*", test_dir);
    printf("Running: %s\n", command.data);
    exit_code = system(command.data);

    if exit_code {
        printf("Test Failed\n");
        return false;
    }
    return true;
}

// #run main();

sprintf(u8* buffer, string format, ... args) #extern "libc"
int system(string command) #extern "libc"
