#import X11
#import compiler

main() {
    window := create_window();

    close_window(window);
}

struct Window {
    handle: void*;
    window: u64;
    graphics_context: void*;
}

Window create_window() {
    display := XOpenDisplay(null);
    screen := XDefaultScreen(display);
    black := XBlackPixel(display, screen);
    white := XWhitePixel(display, screen);

    default_window := XDefaultRootWindow(display);
    x_win := XCreateSimpleWindow(display, default_window, 0, 0, 400, 300, 0, white, black);
    XSetStandardProperties(display, x_win, "My Window", "HI!", 0, null, 0, null);

    XSelectInput(display, x_win, XInputMasks.ExposureMask|XInputMasks.ButtonPressMask|XInputMasks.KeyPressMask);

    gc := XCreateGC(display, x_win, 0, null);

    XSetBackground(display, gc, white);
    XSetForeground(display, gc, black);

    XClearWindow(display, x_win);
    XMapRaised(display, x_win);

    XSync(display, false);

    window: Window = {handle = display; window = x_win; graphics_context = gc;}
    return window;
}

close_window(Window window) {
    XFreeGC(window.handle, window.graphics_context);
    XDestroyWindow(window.handle, window.window);
    XCloseDisplay(window.handle);
}

#run {
    set_linker(LinkerType.Dynamic);
    main();
}
