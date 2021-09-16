main() {
    function_overloads();
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
    // print_type_info(sum); // Does not compile due to multiple overloads of 'sum'

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

#run main();
