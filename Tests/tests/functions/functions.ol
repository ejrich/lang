main() {
    function_overloads();

    default_arguments();

    polymorphic_functions();
}

function_overloads() {
    sum_list: List<int>[7];
    sum_list[0] = 4;
    sum_list[1] = 41;
    sum_list[2] = 544;
    sum_list[3] = 244;
    sum_list[4] = 42;
    sum_list[5] = 14;
    sum_list[6] = 23;
    // type_of(sum); // Does not compile due to multiple overloads of 'sum'

    printf("Sum of List   = %d\n", sum(sum_list));
    printf("Sum of Params = %d\n", sum(4, 41, 544, 244, 42, 14, 23));
}

int sum(List<int> args) {
    sum := 0;
    each i in args then sum += i;
    return sum;
}

int sum(Params<int> args) {
    sum := 0;
    each i in args then sum += i;
    return sum;
}

default_arguments() {
    // Standard behavior
    foo(3);
    foo(4, 10);
    foo(7, 9, false);

    // Specifying arguments, can also move around non-default argument
    foo(5, c = false);
    foo(6, b = 7);
    foo(b = 7, a = 7);

    // Also works with params
    foo_params(3);
    foo_params(7, 10);
    foo_params(8, 9, 3.1, 2.8, 5.6);
}

foo(int a, int b = 8, bool c = true) {
    printf("a = %d, b = %d, c = %d\n", a, b, c);
}

foo_params(int a, int b = 8, Params<float> args) {
    sum: float;
    each i in args then sum += i;
    printf("a = %d, b = %d, sum = %.2f\n", a, b, sum);
}

polymorphic_functions() {

}

T bar<T>(T a, int b) {
    // TODO Add content
}

baz<T, U>(List<T> list, U* b) {
    // TODO Add content
}

#run main();
