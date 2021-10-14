main() {
    run_specified_function();
}

run_specified_function() {
    foo();

    bar();

    baz();
}

#if true {
    #import "a.ol"
}
else {
    #import "b.ol"
}

#run main();
