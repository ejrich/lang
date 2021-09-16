main() {
    // This test will test c interoperability
    // and other functionality such as CArray types
    c_arrays();

    c_array_structs();
}

c_arrays() #print_ir {
    // Examples include this from the tests project
    // each i in file.d_name then printf("%c", i);
    // each i in 0..255 then printf("%c", file.d_name[i]);
    // printf("File type: %d, File name: %s\n", file.d_type, &file.d_name);
    array_size := 10; #const

    array: CArray<int>[array_size]; // Correct

    // array.length = 9; // Does not compile due to length being a constant value
    printf("Array size = %d, should be %d. Array pointer = %p\n", array.length, array_size, &array);
    each i in 0..array_size {
        array[i] = 5 * i;
        printf("Array value %d = %d\n", i, array[i]);
    }

    each x in array then printf("Array value: %d\n", x);

    init_array: CArray<int> = [1, 2, 3, 4, 5]

    each x in init_array then printf("Initialized array value: %d\n", x);
}

c_array_structs() {
    array_struct: ArrayStruct;

    // array_struct.array.length = 90; // Does not compile due to length being a constant value
    printf("ArrayStruct array size = %d, should be %d. Array pointer = %p, Type size = %d\n", array_struct.array.length, struct_array_size, &array_struct.array, size_of(array_struct));
    each i in 0..struct_array_size {
        array_struct.array[i] = 5 * i;
        printf("Struct array value %d = %d\n", i, array_struct.array[i - 1]);
    }

    each x in array_struct.array then printf("Struct array value: %d\n", x);

    each x in array_struct.init_array then printf("Struct init_array value: %d\n", x);
}

struct_array_size := 5; #const

struct ArrayStruct {
    something: int;
    array: CArray<int>[struct_array_size];
    init_array: Array<int> = [1, 2, 3, 4, 5]
}

#run main();
