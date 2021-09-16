main() {
    // This test will test c interoperability
    // and other functionality such as #c_array types
    c_arrays();
}

c_arrays() {
    // Examples include this from the tests project
    // each i in file.d_name then printf("%c", i);
    // each i in 0..255 then printf("%c", file.d_name[i]);
    // printf("File type: %d, File name: %s\n", file.d_type, &file.d_name);

    array: List<int>[10] #c_array; // Correct
    // array: List<int>[10]; #c_array // Incorrect

    each i in 0..9 {
        array[i] = 5 * i;
        printf("Array value %d = %d\n", i, array[i]);
    }

    each x in array then printf("Array value: %d\n", x);
}

// #run main();
