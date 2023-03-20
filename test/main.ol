#import standard
#import compiler

main() {
    failed_test_count := 0;

    success, files := get_files_in_directory("test/tests");
    if success {
        defer default_free(files.data);

        each file in files {
            if file.type == FileType.Directory && file.name[0] != '.' {
                test_dir := format_string("test/tests/%", file.name);
                defer default_free(test_dir.data);

                if !run_test(test_dir, file.name) failed_test_count++;
            }

            #if os == OS.Windows {
                default_free(file.name.data);
            }
        }
    }

    if failed_test_count {
        print("\n% Test(s) Failed\n", failed_test_count);
        set_exit_code(-1);
    }
    else {
        print("\nAll Tests Passed\n");
    }
}

bool run_test(string test_dir, string test) {
    #if os == OS.Windows {
        skip_file_name := "no_windows"; #const
    }
    else {
        skip_file_name := "no_linux"; #const
    }
    executable := "./ol/bin/Debug/net6.0/ol"; #const

    skip_file := format_string("%/%", test_dir, skip_file_name);
    skip := file_exists(skip_file);
    default_free(skip_file.data);
    if skip return true;

    command := format_string("% %/%.ol", executable, test_dir, test);
    print("Compiling: %", command);
    compiler_exit_code := execute_command(command, true);
    default_free(command.data);

    if compiler_exit_code {
        print(" -- Test Failed\n");
        return false;
    }

    command = format_string("./%/bin/%", test_dir, test);

    print("\nRunning: %", command);
    run_exit_code := execute_command(command, true);
    default_free(command.data);

    if run_exit_code {
        print(" -- Test Failed\n");
        return false;
    }
    print("\n");
    return true;
}

#run {
    set_executable_name("tests");

    if os != OS.Windows {
        set_linker(LinkerType.Dynamic);
    }
}
