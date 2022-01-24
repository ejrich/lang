#import standard

main() {
    run_specified_function();
}

run_specified_function() {
    foo();

    bar();

    baz();
}

int pick_one() {
    value := random_integer();
    return value % 2;
}

#if pick_one() {
    #import "a.ol"
}
else {
    #import "b.ol"
}

#run main();
