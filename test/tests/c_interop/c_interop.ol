#import standard

main() {
    // This test will test c interoperability
    // and other functionality such as CArray types
    c_arrays();

    c_array_structs();

    function_calls();

    unions();
}

c_arrays() {
    // Examples include this from the tests project
    // each i in file.d_name then printf("%c", i);
    // each i in 0..255 then printf("%c", file.d_name[i]);
    // printf("File type: %d, File name: %s\n", file.d_type, &file.d_name);
    array_size := 10; #const

    array: CArray<int>[array_size]; // Correct

    // array.length = 9; // Does not compile due to length being a constant value
    print("Array size = %, should be %. Array pointer = %\n", array.length, array_size, &array);
    each i in 0..array_size-1 {
        array[i] = 5 * i;
        print("Array value % = %\n", i, array[i]);
    }

    each x in array print("Array value: %\n", x);

    init_array: CArray<int> = [1, 2, 3, 4, 5]

    each x in init_array print("Initialized array value: %\n", x);
}

c_array_structs() {
    array_struct: ArrayStruct;

    // array_struct.array.length = 90; // Does not compile due to length being a constant value
    print("ArrayStruct array size = %, should be %. Array pointer = %, Type size = %\n", array_struct.array.length, struct_array_size, &array_struct.array, size_of(array_struct));
    each i in 0..struct_array_size-1 {
        array_struct.array[i] = 5 * i;
        print("Struct array value % = %\n", i, array_struct.array[i - 1]);
    }

    each x in array_struct.array print("Struct array value: %\n", x);

    each x in array_struct.init_array print("Struct init_array value: %\n", x);
}

struct_array_size := 5; #const

struct ArrayStruct {
    something: int;
    array: CArray<int>[struct_array_size];
    init_array: Array<int> = [1, 2, 3, 4, 5]
}

function_calls() {
    array := returns_c_array();
    each i in array {
        print("Return c array value %\n", i);
    }

    struct_array := returns_c_array_from_struct();
    each i in struct_array {
        print("Return c array value from struct %\n", i);
    }
}

CArray<int>[4] returns_c_array() {
    a: CArray<int> = [1, 2, 3, 4]
    return a;
}

CArray<int>[5] returns_c_array_from_struct() {
    array_struct: ArrayStruct;
    each i in 0..struct_array_size-1 {
        array_struct.array[i] = 3 * i;
    }

    return array_struct.array;
}

union Event {
    type: int;
    key_event: KeyEvent;
    button_event: ButtonEvent;
}

struct KeyEvent {
    type: int;
    foo: int;
    bar: float64;
}

struct ButtonEvent {
    type: int;
    foo: int;
    bar: float;
    baz: float;
}

unions() {
    event: Event;

    event.type = 5;

    assert(event.type == event.key_event.type);
    assert(event.type == event.button_event.type);

    event.key_event.foo = 6;

    assert(event.key_event.foo == event.button_event.foo);

    print("Union assertions passed\n");
}

#run main();
