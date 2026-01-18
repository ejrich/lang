#import standard

main() {
    constant_size_arrays();

    variable_size_arrays();

    dynamic_arrays();

    array_literals();

    structs_with_arrays();
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
    each arg, i in b arg = i * 8;

    print_array("Array<int>[array_size]", b);
}

variable_size_arrays() {
    size := 10;
    size++;
    size -= 3;

    a: Array<int>[size];
    each arg, i in a arg = (i + 1) * 6;

    print_array("Array<int>[size]", a);

    random_size := random_integer() % 10;
    b: Array<int>[random_size];
    each arg in b arg = random_integer() % 100;

    print_array("Array<int>[random_size]", b);
}

dynamic_arrays() {
    a: Array<int>;
    each i in 0..10 {
        array_insert(&a, i * 8);
    }

    print_array("Array<int>", a);
    default_free(a.data);

    array_struct: DynamicArrayStruct;
    each i in 12..16 {
        array_insert(&array_struct.array, i);
    }
    array_remove(&array_struct.array, 2);

    print_array("DynamicArrayStruct.array", array_struct.array);
    default_free(array_struct.array.data);
}

struct DynamicArrayStruct {
    foo: int;
    array: Array<int>;
}

array_literals() {
    a: Array<int> = [1, 2, 3, 4]

    print_array("Array<int> = [1, 2, 3, 4]", a);
}

structs_with_arrays() {
    a: StructWithArray = {
        array = [1, 2, 4, 8, 16]
    }

    print_array("Array<int> = [1, 2, 4, 8, 16]", a.array);
}

struct StructWithArray {
    array: Array<int>;
}

print_array(string name, Array<int> array) {
    print("------- % -------\n\n", name);

    each value, i in array {
        print("array[%] = %\n", i, value);
    }

    print("\n");
}

#run main();
