main() {
    constant_size_arrays();

    variable_size_arrays();

    dynamic_arrays();

    array_literals();
}

constant_size_arrays() {
    // Sizes can be constant integers
    a: Array<int>[4];
    a[0] = 8;
    a[1] = 7;
    a[2] = 6;
    a[3] = 5;

    print_array("Array<int>[4]", a);

    // Sizes can be constant variables
    array_size := 8; #const
    b: Array<int>[array_size];
    each arg, i in b then arg = i * 8;

    print_array("Array<int>[array_size]", b);
}

variable_size_arrays() {
    size := 10;
    size++;
    size -= 3;

    a: Array<int>[size];
    each arg, i in a then arg = (i + 1) * 6;

    print_array("Array<int>[size]", a);

    random_size := rand() % 10;
    b: Array<int>[random_size];
    each arg in b then arg = rand() % 100;

    print_array("Array<int>[random_size]", b);
}

dynamic_arrays() {
    a: Array<int>;
    each i in 0..11 {
        array_insert(&a, i * 8);
    }

    print_array("Array<int>", a);
    free(a.data);

    array_struct: DynamicArrayStruct;
    each i in 12..16 {
        array_insert(&array_struct.array, i);
    }
    array_remove(&array_struct.array, 2);

    print_array("DynamicArrayStruct.array", array_struct.array);
    free(array_struct.array.data);
}

struct DynamicArrayStruct {
    int foo;
    Array<int> array;
}

array_literals() {
    a: Array<int> = [1, 2, 3, 4]

    print_array("Array<int> = [1, 2, 3, 4]", a);
}

print_array(string name, Array<int> array) {
    printf("------- %s -------\n\n", name);

    each value, i in array {
        printf("array[%d] = %d\n", i, value);
    }

    printf("\n");
}

int rand() #extern "libc"

#run main();
