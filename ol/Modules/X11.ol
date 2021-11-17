struct GC {}
struct Display {}
struct _XrmHashBucketRec {}
struct XOM {}
struct XOC {}
struct XFontSet {}
struct XIM {}
struct XIC {}
struct Region {}

s32 _Xmblen(u8* str, s32 len) #extern "X11"

interface s32 free_private(XExtData* extension)

struct XExtData {
    number: s32;
    next: XExtData*;
    free_private: free_private;
    private_data: u8*;
}

struct XExtCodes {
    extension: s32;
    major_opcode: s32;
    first_event: s32;
    first_error: s32;
}

struct XPixmapFormatValues {
    depth: s32;
    bits_per_pixel: s32;
    scanline_pad: s32;
}

struct XGCValues {
    function: s32;
    plane_mask: u64;
    foreground: u64;
    background: u64;
    line_width: s32;
    line_style: s32;
    cap_style: s32;
    join_style: s32;
    fill_style: s32;
    fill_rule: s32;
    arc_mode: s32;
    tile: u64;
    stipple: u64;
    ts_x_origin: s32;
    ts_y_origin: s32;
    font: u64;
    subwindow_mode: s32;
    graphics_exposures: s32;
    clip_x_origin: s32;
    clip_y_origin: s32;
    clip_mask: u64;
    dash_offset: s32;
    dashes: u8;
}

struct Visual {
    ext_data: XExtData*;
    visualid: u64;
    class: s32;
    red_mask: u64;
    green_mask: u64;
    blue_mask: u64;
    bits_per_rgb: s32;
    map_entries: s32;
}

struct Depth {
    depth: s32;
    nvisuals: s32;
    visuals: Visual*;
}

struct Screen {
    ext_data: XExtData*;
    display: Display*;
    root: u64;
    width: s32;
    height: s32;
    mwidth: s32;
    mheight: s32;
    ndepths: s32;
    depths: Depth*;
    root_depth: s32;
    root_visual: Visual*;
    default_gc: GC*;
    cmap: u64;
    white_pixel: u64;
    black_pixel: u64;
    max_maps: s32;
    min_maps: s32;
    backing_store: s32;
    save_unders: s32;
    root_input_mask: s64;
}

struct ScreenFormat {
    ext_data: XExtData*;
    depth: s32;
    bits_per_pixel: s32;
    scanline_pad: s32;
}

struct XSetWindowAttributes {
    background_pixmap: u64;
    background_pixel: u64;
    border_pixmap: u64;
    border_pixel: u64;
    bit_gravity: s32;
    win_gravity: s32;
    backing_store: s32;
    backing_planes: u64;
    backing_pixel: u64;
    save_under: s32;
    event_mask: s64;
    do_not_propagate_mask: s64;
    override_redirect: s32;
    colormap: u64;
    cursor: u64;
}

struct XWindowAttributes {
    x: s32;
    y: s32;
    width: s32;
    height: s32;
    border_width: s32;
    depth: s32;
    visual: Visual*;
    root: u64;
    class: s32;
    bit_gravity: s32;
    win_gravity: s32;
    backing_store: s32;
    backing_planes: u64;
    backing_pixel: u64;
    save_under: s32;
    colormap: u64;
    map_installed: s32;
    map_state: s32;
    all_event_masks: s64;
    your_event_mask: s64;
    do_not_propagate_mask: s64;
    override_redirect: s32;
    screen: Screen*;
}

struct XHostAddress {
    family: s32;
    length: s32;
    address: u8*;
}

struct XServerInterpretedAddress {
    typelength: s32;
    valuelength: s32;
    type: u8*;
    value: u8*;
}

interface XImage* create_image(Display* a, Visual* b, u32 c, s32 d, s32 e, u8* f, u32 g, u32 h, s32 i, s32 j)
interface s32 destroy_image(XImage* a)
interface u64 get_pixel(XImage* a, s32 b, s32 c)
interface s32 put_pixel(XImage* a, s32 b, s32 c, u64 d)
interface XImage* sub_image(XImage* a, s32 b, s32 c, u32 d, u32 e)
interface s32 add_pixel(XImage* a, s64 b)

struct XImageFunctions {
    create_image: create_image;
    destroy_image: destroy_image;
    get_pixel: get_pixel;
    put_pixel: put_pixel;
    sub_image: sub_image;
    add_pixel: add_pixel;
}

struct XImage {
    width: s32;
    height: s32;
    xoffset: s32;
    format: s32;
    data: u8*;
    byte_order: s32;
    bitmap_unit: s32;
    bitmap_bit_order: s32;
    bitmap_pad: s32;
    depth: s32;
    bytes_per_line: s32;
    bits_per_pixel: s32;
    red_mask: u64;
    green_mask: u64;
    blue_mask: u64;
    obdata: u8*;
    f: XImageFunctions;
}

struct XWindowChanges {
    x: s32;
    y: s32;
    width: s32;
    height: s32;
    border_width: s32;
    sibling: u64;
    stack_mode: s32;
}

struct XColor {
    pixel: u64;
    red: u16;
    green: u16;
    blue: u16;
    flags: u8;
    pad: u8;
}

struct XSegment {
    x1: s16;
    y1: s16;
    x2: s16;
    y2: s16;
}

struct XPoint {
    x: s16;
    y: s16;
}

struct XRectangle {
    x: s16;
    y: s16;
    width: u16;
    height: u16;
}

struct XArc {
    x: s16;
    y: s16;
    width: u16;
    height: u16;
    angle1: s16;
    angle2: s16;
}

struct XKeyboardControl {
    key_click_percent: s32;
    bell_percent: s32;
    bell_pitch: s32;
    bell_duration: s32;
    led: s32;
    led_mode: s32;
    key: s32;
    auto_repeat_mode: s32;
}

struct XKeyboardState {
    key_click_percent: s32;
    bell_percent: s32;
    bell_pitch: u32;
    bell_duration: u32;
    led_mask: u64;
    global_auto_repeat: s32;
    auto_repeats: CArray<u8>[32];
}

struct XTimeCoord {
    time: u64;
    x: s16;
    y: s16;
}

struct XModifierKeymap {
    max_keypermod: s32;
    modifiermap: u8*;
}


struct XKeyEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    root: u64;
    subwindow: u64;
    time: u64;
    x: s32;
    y: s32;
    x_root: s32;
    y_root: s32;
    state: u32;
    keycode: u32;
    same_screen: s32;
}

struct XButtonEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    root: u64;
    subwindow: u64;
    time: u64;
    x: s32;
    y: s32;
    x_root: s32;
    y_root: s32;
    state: u32;
    button: u32;
    same_screen: s32;
}

struct XMotionEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    root: u64;
    subwindow: u64;
    time: u64;
    x: s32;
    y: s32;
    x_root: s32;
    y_root: s32;
    state: u32;
    is_hint: u8;
    same_screen: s32;
}

struct XCrossingEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    root: u64;
    subwindow: u64;
    time: u64;
    x: s32;
    y: s32;
    x_root: s32;
    y_root: s32;
    mode: s32;
    detail: s32;
    same_screen: s32;
    focus: s32;
    state: u32;
}

struct XFocusChangeEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    mode: s32;
    detail: s32;
}

struct XKeymapEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    key_vector: CArray<u8>[32];
}

struct XExposeEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    x: s32;
    y: s32;
    width: s32;
    height: s32;
    count: s32;
}

struct XGraphicsExposeEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    drawable: u64;
    x: s32;
    y: s32;
    width: s32;
    height: s32;
    count: s32;
    major_code: s32;
    minor_code: s32;
}

struct XNoExposeEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    drawable: u64;
    major_code: s32;
    minor_code: s32;
}

struct XVisibilityEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    state: s32;
}

struct XCreateWindowEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    parent: u64;
    window: u64;
    x: s32;
    y: s32;
    width: s32;
    height: s32;
    border_width: s32;
    override_redirect: s32;
}

struct XDestroyWindowEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    event: u64;
    window: u64;
}

struct XUnmapEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    event: u64;
    window: u64;
    from_configure: s32;
}

struct XMapEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    event: u64;
    window: u64;
    override_redirect: s32;
}

struct XMapRequestEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    parent: u64;
    window: u64;
}

struct XReparentEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    event: u64;
    window: u64;
    parent: u64;
    x: s32;
    y: s32;
    override_redirect: s32;
}

struct XConfigureEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    event: u64;
    window: u64;
    x: s32;
    y: s32;
    width: s32;
    height: s32;
    border_width: s32;
    above: u64;
    override_redirect: s32;
}

struct XGravityEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    event: u64;
    window: u64;
    x: s32;
    y: s32;
}

struct XResizeRequestEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    width: s32;
    height: s32;
}

struct XConfigureRequestEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    parent: u64;
    window: u64;
    x: s32;
    y: s32;
    width: s32;
    height: s32;
    border_width: s32;
    above: u64;
    detail: s32;
    value_mask: u64;
}

struct XCirculateEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    event: u64;
    window: u64;
    place: s32;
}

struct XCirculateRequestEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    parent: u64;
    window: u64;
    place: s32;
}

struct XPropertyEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    atom: u64;
    time: u64;
    state: s32;
}

struct XSelectionClearEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    selection: u64;
    time: u64;
}

struct XSelectionRequestEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    owner: u64;
    requestor: u64;
    selection: u64;
    target: u64;
    property: u64;
    time: u64;
}

struct XSelectionEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    requestor: u64;
    selection: u64;
    target: u64;
    property: u64;
    time: u64;
}

struct XColormapEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    colormap: u64;
    new: s32;
    state: s32;
}

struct XClientMessageEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    message_type: u64;
    format: s32;
}

struct XMappingEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
    request: s32;
    first_keycode: s32;
    count: s32;
}

struct XErrorEvent {
    type: XEventType;
    display: Display*;
    resourceid: u64;
    serial: u64;
    error_code: u8;
    request_code: u8;
    minor_code: u8;
}

struct XAnyEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    window: u64;
}

struct XGenericEvent {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    extension: s32;
    evtype: s32;
}

struct XGenericEventCookie {
    type: XEventType;
    serial: u64;
    send_event: s32;
    display: Display*;
    extension: s32;
    evtype: s32;
    cookie: u32;
    data: void*;
}

enum XEventType {
    KeyPress         = 2;
    KeyRelease       = 3;
    ButtonPress      = 4;
    ButtonRelease    = 5;
    MotionNotify     = 6;
    EnterNotify      = 7;
    LeaveNotify      = 8;
    FocusIn          = 9;
    FocusOut         = 10;
    KeymapNotify     = 11;
    Expose           = 12;
    GraphicsExpose   = 13;
    NoExpose         = 14;
    VisibilityNotify = 15;
    CreateNotify     = 16;
    DestroyNotify    = 17;
    UnmapNotify      = 18;
    MapNotify        = 19;
    MapRequest       = 20;
    ReparentNotify   = 21;
    ConfigureNotify  = 22;
    ConfigureRequest = 23;
    GravityNotify    = 24;
    ResizeRequest    = 25;
    CirculateNotify  = 26;
    CirculateRequest = 27;
    PropertyNotify   = 28;
    SelectionClear   = 29;
    SelectionRequest = 30;
    SelectionNotify  = 31;
    ColormapNotify   = 32;
    ClientMessage    = 33;
    MappingNotify    = 34;
    GenericEvent     = 35;
    LASTEvent        = 36;
}

union XEvent {
    type: XEventType;
    xany: XAnyEvent;
    xkey: XKeyEvent;
    xbutton: XButtonEvent;
    xmotion: XMotionEvent;
    xcrossing: XCrossingEvent;
    xfocus: XFocusChangeEvent;
    xexpose: XExposeEvent;
    xgraphicsexpose: XGraphicsExposeEvent;
    xnoexpose: XNoExposeEvent;
    xvisibility: XVisibilityEvent;
    xcreatewindow: XCreateWindowEvent;
    xdestroywindow: XDestroyWindowEvent;
    xunmap: XUnmapEvent;
    xmap: XMapEvent;
    xmaprequest: XMapRequestEvent;
    xreparent: XReparentEvent;
    xconfigure: XConfigureEvent;
    xgravity: XGravityEvent;
    xresizerequest: XResizeRequestEvent;
    xconfigurerequest: XConfigureRequestEvent;
    xcirculate: XCirculateEvent;
    xcirculaterequest: XCirculateRequestEvent;
    xproperty: XPropertyEvent;
    xselectionclear: XSelectionClearEvent;
    xselectionrequest: XSelectionRequestEvent;
    xselection: XSelectionEvent;
    xcolormap: XColormapEvent;
    xclient: XClientMessageEvent;
    xmapping: XMappingEvent;
    xerror: XErrorEvent;
    xkeymap: XKeymapEvent;
    xgeneric: XGenericEvent;
    xcookie: XGenericEventCookie;
    pad: CArray<s64>[24];
}

struct XCharStruct {
    lbearing: s16;
    rbearing: s16;
    width: s16;
    ascent: s16;
    descent: s16;
    attributes: u16;
}

struct XFontProp {
    name: u64;
    card32: u64;
}

struct XFontStruct {
    ext_data: XExtData*;
    fid: u64;
    direction: u32;
    min_char_or_byte2: u32;
    max_char_or_byte2: u32;
    min_byte1: u32;
    max_byte1: u32;
    all_chars_exist: s32;
    default_char: u32;
    n_properties: s32;
    properties: XFontProp*;
    min_bounds: XCharStruct;
    max_bounds: XCharStruct;
    per_char: XCharStruct*;
    ascent: s32;
    descent: s32;
}

struct XTextItem {
    chars: u8*;
    nchars: s32;
    delta: s32;
    font: u64;
}

struct XChar2b {
    byte1: u8;
    byte2: u8;
}

struct XTextItem16 {
    chars: XChar2b*;
    nchars: s32;
    delta: s32;
    font: u64;
}

union XEDataObject {
    display: Display*;
    gc: GC*;
    visual: Visual*;
    screen: Screen*;
    pixmap_format: ScreenFormat*;
    font: XFontStruct*;
}

struct XFontSetExtents {
    max_ink_extent: XRectangle;
    max_logical_extent: XRectangle;
}

struct XmbTextItem {
    chars: u8*;
    nchars: s32;
    delta: s32;
    font_set: XFontSet*;
}

struct XwcTextItem {
    chars: s32*;
    nchars: s32;
    delta: s32;
    font_set: XFontSet*;
}

struct XOMCharSetList {
    charset_count: s32;
    charset_list: u8**;
}

enum XOrientation {
    XOMOrientation_LTR_TTB;
    XOMOrientation_RTL_TTB;
    XOMOrientation_TTB_LTR;
    XOMOrientation_TTB_RTL;
    XOMOrientation_Context;
}

struct XOMOrientation {
    num_orientation: s32;
    orientation: XOrientation*;
}

struct XOMFontInfo {
    num_font: s32;
    font_struct_list: XFontStruct**;
    font_name_list: u8**;
}

interface XIMProc(XIM* a, u8* b, u8* c)

interface s32 XICProc(XIC* a, u8* b, u8* c)

interface XIDProc(Display* a, u8* b, u8* c)

struct XIMStyles {
    count_styles: u16;
    supported_styles: u64*;
}

struct XIMCallback {
    client_data: u8*;
    callback: XIMProc;
}

struct XICCallback {
    client_data: u8*;
    callback: XICProc;
}
struct XIMText {
    length: u16;
    feedback: u64*;
    encoding_is_wchar: s32;
}

struct XIMPreeditStateNotifyCallbackStruct {
    state: u64;
}

struct XIMStringConversionText {
    length: u16;
    feedback: u64*;
    encoding_is_wchar: s32;
}

enum XIMCaretDirection {
    XIMForwardChar;
    XIMBackwardChar;
    XIMForwardWord;
    XIMBackwardWord;
    XIMCaretUp;
    XIMCaretDown;
    XIMNextLine;
    XIMPreviousLine;
    XIMLineStart;
    XIMLineEnd;
    XIMAbsolutePosition;
    XIMDontChange;
}

struct XIMStringConversionCallbackStruct {
    position: u16;
    direction: XIMCaretDirection;
    operation: u16;
    factor: u16;
    text: XIMStringConversionText*;
}

struct XIMPreeditDrawCallbackStruct {
    caret: s32;
    chg_first: s32;
    chg_length: s32;
    text: XIMText*;
}

enum XIMCaretStyle {
    XIMIsInvisible;
    XIMIsPrimary;
    XIMIsSecondary;
}

struct XIMPreeditCaretCallbackStruct {
    position: s32;
    direction: XIMCaretDirection;
    style: XIMCaretStyle;
}

enum XIMStatusDataType {
    XIMTextType;
    XIMBitmapType;
}

struct XIMStatusDrawCallbackStruct {
    type: XIMStatusDataType;
}

struct XIMHotKeyTrigger {
    keysym: u64;
    modifier: s32;
    modifier_mask: s32;
}

struct XIMHotKeyTriggers {
    num_hot_key: s32;
    key: XIMHotKeyTrigger*;
}

struct XIMValuesList {
    count_values: u16;
    supported_values: u8**;
}

struct XSizeHintsAspect {
    x: s32;
    y: s32;
}

struct XSizeHints {
    flags: s64;
    x: s32;
    y: s32;
    width: s32;
    height: s32;
    min_width: s32;
    min_height: s32;
    max_width: s32;
    max_height: s32;
    width_inc: s32;
    height_inc: s32;
    min_aspect: XSizeHintsAspect;
    max_aspect: XSizeHintsAspect;
    base_width: s32;
    base_height: s32;
    win_gravity: s32;
}

struct XWMHints {
    flags: s64;
    input: s32;
    initial_state: s32;
    icon_pixmap: u64;
    icon_window: u64;
    icon_x: s32;
    icon_y: s32;
    icon_mask: u64;
    window_group: u64;
}

struct XTextProperty {
    value: u8*;
    encoding: u64;
    format: s32;
    nitems: u64;
}

enum XICCEncodingStyle {
    XStringStyle;
    XCompoundTextStyle;
    XTextStyle;
    XStdICCTextStyle;
    XUTF8StringStyle;
}

struct XIconSize {
    min_width: s32;
    min_height: s32;
    max_width: s32;
    max_height: s32;
    width_inc: s32;
    height_inc: s32;
}

struct XClassHint {
    res_name: u8*;
    res_class: u8*;
}

struct XComposeStatus {
    compose_ptr: u8*;
    chars_matched: s32;
}

struct XVisualInfo {
    visual: Visual*;
    visualid: u64;
    screen: s32;
    depth: s32;
    class: s32;
    red_mask: u64;
    green_mask: u64;
    blue_mask: u64;
    colormap_size: s32;
    bits_per_rgb: s32;
}

struct XStandardColormap {
    colormap: u64;
    red_max: u64;
    red_mult: u64;
    green_max: u64;
    green_mult: u64;
    blue_max: u64;
    blue_mult: u64;
    base_pixel: u64;
    visualid: u64;
    killid: u64;
}

enum XInputMasks : s64 {
    NoEventMask              = 0x0;
    KeyPressMask             = 0x1;
    KeyReleaseMask           = 0x2;
    ButtonPressMask          = 0x4;
    ButtonReleaseMask        = 0x8;
    EnterWindowMask          = 0x10;
    LeaveWindowMask          = 0x20;
    PointerMotionMask        = 0x40;
    PointerMotionHintMask    = 0x80;
    Button1MotionMask        = 0x100;
    Button2MotionMask        = 0x200;
    Button3MotionMask        = 0x400;
    Button4MotionMask        = 0x800;
    Button5MotionMask        = 0x1000;
    ButtonMotionMask         = 0x2000;
    KeymapStateMask          = 0x4000;
    ExposureMask             = 0x8000;
    VisibilityChangeMask     = 0x10000;
    StructureNotifyMask      = 0x20000;
    ResizeRedirectMask       = 0x40000;
    SubstructureNotifyMask   = 0x80000;
    SubstructureRedirectMask = 0x100000;
    FocusChangeMask          = 0x200000;
    PropertyChangeMask       = 0x400000;
    ColormapChangeMask       = 0x800000;
    OwnerGrabButtonMask      = 0x1000000;
}

XFontStruct* XQueryFont(Display* a, u64 b) #extern "X11"

XTimeCoord* XGetMotionEvents(Display* a, u64 b, u64 c, u64 d, s32* e) #extern "X11"

XModifierKeymap* XDeleteModifiermapEntry(XModifierKeymap* a, u8 b, s32 c) #extern "X11"

XModifierKeymap* XGetModifierMapping(Display* a) #extern "X11"

XModifierKeymap* XInsertModifiermapEntry(XModifierKeymap* a, u8 b, s32 c) #extern "X11"

XModifierKeymap* XNewModifiermap(s32 a) #extern "X11"

XImage* XCreateImage(Display* a, Visual* b, u32 c, s32 d, s32 e, u8* f, u32 g, u32 h, s32 i, s32 j) #extern "X11"

s32 XInitImage(XImage* a) #extern "X11"

XImage* XGetImage(Display* a, u64 b, s32 c, s32 d, u32 e, u32 f, u64 g, s32 h) #extern "X11"

XImage* XGetSubImage(Display* a, u64 b, s32 c, s32 d, u32 e, u32 f, u64 g, s32 h, XImage* i, s32 j, s32 k) #extern "X11"

Display* XOpenDisplay(u8* a) #extern "X11"

XrmInitialize() #extern "X11"

u8* XFetchBytes(Display* a, s32* b) #extern "X11"

u8* XFetchBuffer(Display* a, s32* b, s32 c) #extern "X11"

u8* XGetAtomName(Display* a, u64 b) #extern "X11"

s32 XGetAtomNames(Display* a, u64* b, s32 c, u8** d) #extern "X11"

u8* XGetDefault(Display* a, u8* b, u8* c) #extern "X11"

u8* XDisplayName(u8* a) #extern "X11"

u8* XKeysymToString(u64 a) #extern "X11"

s32 XSynchronize(Display* display, s32 b) #extern "X11"

s32 XSetAfterFunction(Display* display, s32 b) #extern "X11"

u64 XInternAtom(Display* a, u8* b, s32 c) #extern "X11"

s32 XInternAtoms(Display* a, u8** b, s32 c, s32 d, u64* e) #extern "X11"

u64 XCopyColormapAndFree(Display* a, u64 b) #extern "X11"

u64 XCreateColormap(Display* a, u64 b, Visual* c, s32 d) #extern "X11"

u64 XCreatePixmapCursor(Display* a, u64 b, u64 c, XColor* d, XColor* e, u32 f, u32 g) #extern "X11"

u64 XCreateGlyphCursor(Display* a, u64 b, u64 c, u32 d, u32 e, XColor f, XColor g) #extern "X11"

u64 XCreateFontCursor(Display* a, u32 b) #extern "X11"

u64 XLoadFont(Display* a, u8* b) #extern "X11"

GC* XCreateGC(Display* a, u64 b, u64 c, XGCValues* d) #extern "X11"

u64 XGContextFromGC(GC* a) #extern "X11"

XFlushGC(Display* a, GC* b) #extern "X11"

u64 XCreatePixmap(Display* a, u64 b, u32 c, u32 d, u32 e) #extern "X11"

u64 XCreateBitmapFromData(Display* a, u64 b, u8* c, u32 d, u32 e) #extern "X11"

u64 XCreatePixmapFromBitmapData(Display* a, u64 b, u8* c, u32 d, u32 e, u64 f, u64 g, u32 h) #extern "X11"

u64 XCreateSimpleWindow(Display* a, u64 b, s32 c, s32 d, u32 e, u32 f, u32 g, u64 h, u64 i) #extern "X11"

u64 XGetSelectionOwner(Display* a, u64 b) #extern "X11"

u64 XCreateWindow(Display* a, u64 b, s32 c, s32 d, u32 e, u32 f, u32 g, s32 h, u32 i, Visual* j, u64 k, XSetWindowAttributes* l) #extern "X11"

u64* XListInstalledColormaps(Display* a, u64 b, s32* c) #extern "X11"

u8** XListFonts(Display* a, u8* b, s32 c, s32* d) #extern "X11"

u8** XListFontsWithInfo(Display* a, u8* b, s32 c, s32* d, XFontStruct** e) #extern "X11"

u8** XGetFontPath(Display* a, s32* b) #extern "X11"

u8** XListExtensions(Display* a, s32* b) #extern "X11"

u64* XListProperties(Display* a, u64 b, s32* c) #extern "X11"

XHostAddress* XListHosts(Display* a, s32* b, s32* c) #extern "X11"

u64 XLookupKeysym(XKeyEvent* a, s32 b) #extern "X11"

u64* XGetKeyboardMapping(Display* a, u8 b, s32 c, s32* d) #extern "X11"

u64 XStringToKeysym(u8* a) #extern "X11"

s64 XMaxRequestSize(Display* a) #extern "X11"

s64 XExtendedMaxRequestSize(Display* a) #extern "X11"

u8* XResourceManagerString(Display* a) #extern "X11"

u8* XScreenResourceString(Screen* a) #extern "X11"

u64 XDisplayMotionBufferSize(Display* a) #extern "X11"

u64 XVisualIDFromVisual(Visual* a) #extern "X11"

s32 XInitThreads() #extern "X11"

XLockDisplay(Display* a) #extern "X11"

XUnlockDisplay(Display* a) #extern "X11"

XExtCodes* XInitExtension(Display* a, u8* b) #extern "X11"

XExtCodes* XAddExtension(Display* a) #extern "X11"

XExtData* XFindOnExtensionList(XExtData** a, s32 b) #extern "X11"

XExtData** XEHeadOfExtensionList(XEDataObject a) #extern "X11"

u64 XRootWindow(Display* a, s32 b) #extern "X11"

u64 XDefaultRootWindow(Display* a) #extern "X11"

u64 XRootWindowOfScreen(Screen* a) #extern "X11"

Visual* XDefaultVisual(Display* a, s32 b) #extern "X11"

Visual* XDefaultVisualOfScreen(Screen* a) #extern "X11"

GC* XDefaultGC(Display* a, s32 b) #extern "X11"

GC* XDefaultGCOfScreen(Screen* a) #extern "X11"

u64 XBlackPixel(Display* a, s32 b) #extern "X11"

u64 XWhitePixel(Display* a, s32 b) #extern "X11"

u64 XAllPlanes() #extern "X11"

u64 XBlackPixelOfScreen(Screen* a) #extern "X11"

u64 XWhitePixelOfScreen(Screen* a) #extern "X11"

u64 XNextRequest(Display* a) #extern "X11"

u64 XLastKnownRequestProcessed(Display* a) #extern "X11"

u8* XServerVendor(Display* a) #extern "X11"

u8* XDisplayString(Display* a) #extern "X11"

u64 XDefaultColormap(Display* a, s32 b) #extern "X11"

u64 XDefaultColormapOfScreen(Screen* a) #extern "X11"

Display* XDisplayOfScreen(Screen* a) #extern "X11"

Screen* XScreenOfDisplay(Display* a, s32 b) #extern "X11"

Screen* XDefaultScreenOfDisplay(Display* a) #extern "X11"

s64 XEventMaskOfScreen(Screen* a) #extern "X11"

s32 XScreenNumberOfScreen(Screen* a) #extern "X11"

interface s32 XErrorHandler(Display* a, XErrorEvent* b)

XErrorHandler XSetErrorHandler(XErrorHandler a) #extern "X11"

interface s32 XIOErrorHandler(Display* a)

XIOErrorHandler XSetIOErrorHandler(XIOErrorHandler a) #extern "X11"

interface XIOErrorExitHandler(Display* a, void* b)

XSetIOErrorExitHandler(Display* a, XIOErrorExitHandler b, void* c) #extern "X11"

XPixmapFormatValues* XListPixmapFormats(Display* a, s32* b) #extern "X11"

s32* XListDepths(Display* a, s32 b, s32* c) #extern "X11"

s32 XReconfigureWMWindow(Display* a, u64 b, s32 c, u32 d, XWindowChanges* e) #extern "X11"

s32 XGetWMProtocols(Display* a, u64 b, u64** c, s32* d) #extern "X11"

s32 XSetWMProtocols(Display* a, u64 b, u64* c, s32 d) #extern "X11"

s32 XIconifyWindow(Display* a, u64 b, s32 c) #extern "X11"

s32 XWithdrawWindow(Display* a, u64 b, s32 c) #extern "X11"

s32 XGetCommand(Display* a, u64 b, u8*** c, s32* d) #extern "X11"

s32 XGetWMColormapWindows(Display* a, u64 b, u64** c, s32* d) #extern "X11"

s32 XSetWMColormapWindows(Display* a, u64 b, u64* c, s32 d) #extern "X11"

XFreeStringList(u8** a) #extern "X11"

s32 XSetTransientForHint(Display* a, u64 b, u64 c) #extern "X11"

s32 XActivateScreenSaver(Display* a) #extern "X11"

s32 XAddHost(Display* a, XHostAddress* b) #extern "X11"

s32 XAddHosts(Display* a, XHostAddress* b, s32 c) #extern "X11"

s32 XAddToExtensionList(XExtData** a, XExtData* b) #extern "X11"

s32 XAddToSaveSet(Display* a, u64 b) #extern "X11"

s32 XAllocColor(Display* a, u64 b, XColor* c) #extern "X11"

s32 XAllocColorCells(Display* a, u64 b, s32 c, u64* d, u32 e, u64* f, u32 g) #extern "X11"

s32 XAllocColorPlanes(Display* a, u64 b, s32 c, u64* d, s32 e, s32 f, s32 g, s32 h, u64* i, u64* j, u64* k) #extern "X11"

s32 XAllocNamedColor(Display* a, u64 b, u8* c, XColor* d, XColor* e) #extern "X11"

s32 XAllowEvents(Display* a, s32 b, u64 c) #extern "X11"

s32 XAutoRepeatOff(Display* a) #extern "X11"

s32 XAutoRepeatOn(Display* a) #extern "X11"

s32 XBell(Display* a, s32 b) #extern "X11"

s32 XBitmapBitOrder(Display* a) #extern "X11"

s32 XBitmapPad(Display* a) #extern "X11"

s32 XBitmapUnit(Display* a) #extern "X11"

s32 XCellsOfScreen(Screen* a) #extern "X11"

s32 XChangeActivePointerGrab(Display* a, u32 b, u64 c, u64 d) #extern "X11"

s32 XChangeGC(Display* a, GC* b, u64 c, XGCValues* d) #extern "X11"

s32 XChangeKeyboardControl(Display* a, u64 b, XKeyboardControl* c) #extern "X11"

s32 XChangeKeyboardMapping(Display* a, s32 b, s32 c, u64* d, s32 e) #extern "X11"

s32 XChangePointerControl(Display* a, s32 b, s32 c, s32 d, s32 e, s32 f) #extern "X11"

s32 XChangeProperty(Display* a, u64 b, u64 c, u64 d, s32 e, s32 f, u8* g, s32 h) #extern "X11"

s32 XChangeSaveSet(Display* a, u64 b, s32 c) #extern "X11"

s32 XChangeWindowAttributes(Display* a, u64 b, u64 c, XSetWindowAttributes* d) #extern "X11"

s32 XCheckIfEvent(Display* a, XEvent* b, s32 c) #extern "X11"

s32 XCheckMaskEvent(Display* a, s64 b, XEvent* c) #extern "X11"

s32 XCheckTypedEvent(Display* a, s32 b, XEvent* c) #extern "X11"

s32 XCheckTypedWindowEvent(Display* a, u64 b, s32 c, XEvent* d) #extern "X11"

s32 XCheckWindowEvent(Display* a, u64 b, s64 c, XEvent* d) #extern "X11"

s32 XCirculateSubwindows(Display* a, u64 b, s32 c) #extern "X11"

s32 XCirculateSubwindowsDown(Display* a, u64 b) #extern "X11"

s32 XCirculateSubwindowsUp(Display* a, u64 b) #extern "X11"

s32 XClearArea(Display* a, u64 b, s32 c, s32 d, u32 e, u32 f, s32 g) #extern "X11"

s32 XClearWindow(Display* a, u64 b) #extern "X11"

s32 XCloseDisplay(Display* a) #extern "X11"

s32 XConfigureWindow(Display* a, u64 b, u32 c, XWindowChanges* d) #extern "X11"

s32 XConnectionNumber(Display* a) #extern "X11"

s32 XConvertSelection(Display* a, u64 b, u64 c, u64 d, u64 e, u64 f) #extern "X11"

s32 XCopyArea(Display* a, u64 b, u64 c, GC* d, s32 e, s32 f, u32 g, u32 h, s32 i, s32 j) #extern "X11"

s32 XCopyGC(Display* a, GC* b, u64 c, GC* d) #extern "X11"

s32 XCopyPlane(Display* a, u64 b, u64 c, GC* d, s32 e, s32 f, u32 g, u32 h, s32 i, s32 j, u64 k) #extern "X11"

s32 XDefaultDepth(Display* a, s32 b) #extern "X11"

s32 XDefaultDepthOfScreen(Screen* a) #extern "X11"

s32 XDefaultScreen(Display* a) #extern "X11"

s32 XDefineCursor(Display* a, u64 b, u64 c) #extern "X11"

s32 XDeleteProperty(Display* a, u64 b, u64 c) #extern "X11"

s32 XDestroyWindow(Display* a, u64 b) #extern "X11"

s32 XDestroySubwindows(Display* a, u64 b) #extern "X11"

s32 XDoesBackingStore(Screen* a) #extern "X11"

s32 XDoesSaveUnders(Screen* a) #extern "X11"

s32 XDisableAccessControl(Display* a) #extern "X11"

s32 XDisplayCells(Display* a, s32 b) #extern "X11"

s32 XDisplayHeight(Display* a, s32 b) #extern "X11"

s32 XDisplayHeightMM(Display* a, s32 b) #extern "X11"

s32 XDisplayKeycodes(Display* a, s32* b, s32* c) #extern "X11"

s32 XDisplayPlanes(Display* a, s32 b) #extern "X11"

s32 XDisplayWidth(Display* a, s32 b) #extern "X11"

s32 XDisplayWidthMM(Display* a, s32 b) #extern "X11"

s32 XDrawArc(Display* a, u64 b, GC* c, s32 d, s32 e, u32 f, u32 g, s32 h, s32 i) #extern "X11"

s32 XDrawArcs(Display* a, u64 b, GC* c, XArc* d, s32 e) #extern "X11"

s32 XDrawImageString(Display* a, u64 b, GC* c, s32 d, s32 e, u8* f, s32 g) #extern "X11"

s32 XDrawImageString16(Display* a, u64 b, GC* c, s32 d, s32 e, XChar2b* f, s32 g) #extern "X11"

s32 XDrawLine(Display* a, u64 b, GC* c, s32 d, s32 e, s32 f, s32 g) #extern "X11"

s32 XDrawLines(Display* a, u64 b, GC* c, XPoint* d, s32 e, s32 f) #extern "X11"

s32 XDrawPoint(Display* a, u64 b, GC* c, s32 d, s32 e) #extern "X11"

s32 XDrawPoints(Display* a, u64 b, GC* c, XPoint* d, s32 e, s32 f) #extern "X11"

s32 XDrawRectangle(Display* a, u64 b, GC* c, s32 d, s32 e, u32 f, u32 g) #extern "X11"

s32 XDrawRectangles(Display* a, u64 b, GC* c, XRectangle* d, s32 e) #extern "X11"

s32 XDrawSegments(Display* a, u64 b, GC* c, XSegment* d, s32 e) #extern "X11"

s32 XDrawString(Display* a, u64 b, GC* c, s32 d, s32 e, u8* f, s32 g) #extern "X11"

s32 XDrawString16(Display* a, u64 b, GC* c, s32 d, s32 e, XChar2b* f, s32 g) #extern "X11"

s32 XDrawText(Display* a, u64 b, GC* c, s32 d, s32 e, XTextItem* f, s32 g) #extern "X11"

s32 XDrawText16(Display* a, u64 b, GC* c, s32 d, s32 e, XTextItem16* f, s32 g) #extern "X11"

s32 XEnableAccessControl(Display* a) #extern "X11"

s32 XEventsQueued(Display* a, s32 b) #extern "X11"

s32 XFetchName(Display* a, u64 b, u8** c) #extern "X11"

s32 XFillArc(Display* a, u64 b, GC* c, s32 d, s32 e, u32 f, u32 g, s32 h, s32 i) #extern "X11"

s32 XFillArcs(Display* a, u64 b, GC* c, XArc* d, s32 e) #extern "X11"

s32 XFillPolygon(Display* a, u64 b, GC* c, XPoint* d, s32 e, s32 f, s32 g) #extern "X11"

s32 XFillRectangle(Display* a, u64 b, GC* c, s32 d, s32 e, u32 f, u32 g) #extern "X11"

s32 XFillRectangles(Display* a, u64 b, GC* c, XRectangle* d, s32 e) #extern "X11"

s32 XFlush(Display* a) #extern "X11"

s32 XForceScreenSaver(Display* a, s32 b) #extern "X11"

s32 XFree(void* a) #extern "X11"

s32 XFreeColormap(Display* a, u64 b) #extern "X11"

s32 XFreeColors(Display* a, u64 b, u64* c, s32 d, u64 e) #extern "X11"

s32 XFreeCursor(Display* a, u64 b) #extern "X11"

s32 XFreeExtensionList(u8** a) #extern "X11"

s32 XFreeFont(Display* a, XFontStruct* b) #extern "X11"

s32 XFreeFontInfo(u8** a, XFontStruct* b, s32 c) #extern "X11"

s32 XFreeFontNames(u8** a) #extern "X11"

s32 XFreeFontPath(u8** a) #extern "X11"

s32 XFreeGC(Display* a, GC* b) #extern "X11"

s32 XFreeModifiermap(XModifierKeymap* a) #extern "X11"

s32 XFreePixmap(Display* a, u64 b) #extern "X11"

s32 XGeometry(Display* a, s32 b, u8* c, u8* d, u32 e, u32 f, u32 g, s32 h, s32 i, s32* j, s32* k, s32* l, s32* m) #extern "X11"

s32 XGetErrorDatabaseText(Display* a, u8* b, u8* c, u8* d, u8* e, s32 f) #extern "X11"

s32 XGetErrorText(Display* a, s32 b, u8* c, s32 d) #extern "X11"

s32 XGetFontProperty(XFontStruct* a, u64 b, u64* c) #extern "X11"

s32 XGetGCValues(Display* a, GC* b, u64 c, XGCValues* d) #extern "X11"

s32 XGetGeometry(Display* a, u64 b, u64* c, s32* d, s32* e, u32* f, u32* g, u32* h, u32* i) #extern "X11"

s32 XGetIconName(Display* a, u64 b, u8** c) #extern "X11"

s32 XGetInputFocus(Display* a, u64* b, s32* c) #extern "X11"

s32 XGetKeyboardControl(Display* a, XKeyboardState* b) #extern "X11"

s32 XGetPointerControl(Display* a, s32* b, s32* c, s32* d) #extern "X11"

s32 XGetPointerMapping(Display* a, u8* b, s32 c) #extern "X11"

s32 XGetScreenSaver(Display* a, s32* b, s32* c, s32* d, s32* e) #extern "X11"

s32 XGetTransientForHint(Display* a, u64 b, u64* c) #extern "X11"

s32 XGetWindowProperty(Display* a, u64 b, u64 c, s64 d, s64 e, s32 f, u64 g, u64* h, s32* i, u64* j, u64* k, u8** l) #extern "X11"

s32 XGetWindowAttributes(Display* a, u64 b, XWindowAttributes* c) #extern "X11"

s32 XGrabButton(Display* a, u32 b, u32 c, u64 d, s32 e, u32 f, s32 g, s32 h, u64 i, u64 j) #extern "X11"

s32 XGrabKey(Display* a, s32 b, u32 c, u64 d, s32 e, s32 f, s32 g) #extern "X11"

s32 XGrabKeyboard(Display* a, u64 b, s32 c, s32 d, s32 e, u64 f) #extern "X11"

s32 XGrabPointer(Display* a, u64 b, s32 c, u32 d, s32 e, s32 f, u64 g, u64 h, u64 i) #extern "X11"

s32 XGrabServer(Display* a) #extern "X11"

s32 XHeightMMOfScreen(Screen* a) #extern "X11"

s32 XHeightOfScreen(Screen* a) #extern "X11"

s32 XIfEvent(Display* a, XEvent* b, s32 c) #extern "X11"

s32 XImageByteOrder(Display* a) #extern "X11"

s32 XInstallColormap(Display* a, u64 b) #extern "X11"

u8 XKeysymToKeycode(Display* a, u64 b) #extern "X11"

s32 XKillClient(Display* a, u64 b) #extern "X11"

s32 XLookupColor(Display* a, u64 b, u8* c, XColor* d, XColor* e) #extern "X11"

s32 XLowerWindow(Display* a, u64 b) #extern "X11"

s32 XMapRaised(Display* a, u64 b) #extern "X11"

s32 XMapSubwindows(Display* a, u64 b) #extern "X11"

s32 XMapWindow(Display* a, u64 b) #extern "X11"

s32 XMaskEvent(Display* a, s64 b, XEvent* c) #extern "X11"

s32 XMaxCmapsOfScreen(Screen* a) #extern "X11"

s32 XMinCmapsOfScreen(Screen* a) #extern "X11"

s32 XMoveResizeWindow(Display* a, u64 b, s32 c, s32 d, u32 e, u32 f) #extern "X11"

s32 XMoveWindow(Display* a, u64 b, s32 c, s32 d) #extern "X11"

s32 XNextEvent(Display* a, XEvent* b) #extern "X11"

s32 XNoOp(Display* a) #extern "X11"

s32 XParseColor(Display* a, u64 b, u8* c, XColor* d) #extern "X11"

s32 XParseGeometry(u8* a, s32* b, s32* c, u32* d, u32* e) #extern "X11"

s32 XPeekEvent(Display* a, XEvent* b) #extern "X11"

s32 XPeekIfEvent(Display* a, XEvent* b, s32 c) #extern "X11"

s32 XPending(Display* a) #extern "X11"

s32 XPlanesOfScreen(Screen* a) #extern "X11"

s32 XProtocolRevision(Display* a) #extern "X11"

s32 XProtocolVersion(Display* a) #extern "X11"

s32 XPutBackEvent(Display* a, XEvent* b) #extern "X11"

s32 XPutImage(Display* a, u64 b, GC* c, XImage* d, s32 e, s32 f, s32 g, s32 h, u32 i, u32 j) #extern "X11"

s32 XQLength(Display* a) #extern "X11"

s32 XQueryBestCursor(Display* a, u64 b, u32 c, u32 d, u32* e, u32* f) #extern "X11"

s32 XQueryBestSize(Display* a, s32 b, u64 c, u32 d, u32 e, u32* f, u32* g) #extern "X11"

s32 XQueryBestStipple(Display* a, u64 b, u32 c, u32 d, u32* e, u32* f) #extern "X11"

s32 XQueryBestTile(Display* a, u64 b, u32 c, u32 d, u32* e, u32* f) #extern "X11"

s32 XQueryColor(Display* a, u64 b, XColor* c) #extern "X11"

s32 XQueryColors(Display* a, u64 b, XColor* c, s32 d) #extern "X11"

s32 XQueryExtension(Display* a, u8* b, s32* c, s32* d, s32* e) #extern "X11"

s32 XQueryKeymap(Display* a, CArray<u8>[32] b) #extern "X11"

s32 XQueryPointer(Display* a, u64 b, u64* c, u64* d, s32* e, s32* f, s32* g, s32* h, u32* i) #extern "X11"

s32 XQueryTextExtents(Display* a, u64 b, u8* c, s32 d, s32* e, s32* f, s32* g, XCharStruct* h) #extern "X11"

s32 XQueryTextExtents16(Display* a, u64 b, XChar2b* c, s32 d, s32* e, s32* f, s32* g, XCharStruct* h) #extern "X11"

s32 XQueryTree(Display* a, u64 b, u64* c, u64* d, u64** e, u32* f) #extern "X11"

s32 XRaiseWindow(Display* a, u64 b) #extern "X11"

s32 XReadBitmapFile(Display* a, u64 b, u8* c, u32* d, u32* e, u64* f, s32* g, s32* h) #extern "X11"

s32 XReadBitmapFileData(u8* a, u32* b, u32* c, u8** d, s32* e, s32* f) #extern "X11"

s32 XRebindKeysym(Display* a, u64 b, u64* c, s32 d, u8* e, s32 f) #extern "X11"

s32 XRecolorCursor(Display* a, u64 b, XColor* c, XColor* d) #extern "X11"

s32 XRefreshKeyboardMapping(XMappingEvent* a) #extern "X11"

s32 XRemoveFromSaveSet(Display* a, u64 b) #extern "X11"

s32 XRemoveHost(Display* a, XHostAddress* b) #extern "X11"

s32 XRemoveHosts(Display* a, XHostAddress* b, s32 c) #extern "X11"

s32 XReparentWindow(Display* a, u64 b, u64 c, s32 d, s32 e) #extern "X11"

s32 XResetScreenSaver(Display* a) #extern "X11"

s32 XResizeWindow(Display* a, u64 b, u32 c, u32 d) #extern "X11"

s32 XRestackWindows(Display* a, u64* b, s32 c) #extern "X11"

s32 XRotateBuffers(Display* a, s32 b) #extern "X11"

s32 XRotateWindowProperties(Display* a, u64 b, u64* c, s32 d, s32 e) #extern "X11"

s32 XScreenCount(Display* a) #extern "X11"

s32 XSelectInput(Display* a, u64 b, XInputMasks c) #extern "X11"

s32 XSendEvent(Display* a, u64 b, s32 c, s64 d, XEvent* e) #extern "X11"

s32 XSetAccessControl(Display* a, s32 b) #extern "X11"

s32 XSetArcMode(Display* a, GC* b, s32 c) #extern "X11"

s32 XSetBackground(Display* a, GC* b, u64 c) #extern "X11"

s32 XSetClipMask(Display* a, GC* b, u64 c) #extern "X11"

s32 XSetClipOrigin(Display* a, GC* b, s32 c, s32 d) #extern "X11"

s32 XSetClipRectangles(Display* a, GC* b, s32 c, s32 d, XRectangle* e, s32 f, s32 g) #extern "X11"

s32 XSetCloseDownMode(Display* a, s32 b) #extern "X11"

s32 XSetCommand(Display* a, u64 b, u8** c, s32 d) #extern "X11"

s32 XSetDashes(Display* a, GC* b, s32 c, u8* d, s32 e) #extern "X11"

s32 XSetFillRule(Display* a, GC* b, s32 c) #extern "X11"

s32 XSetFillStyle(Display* a, GC* b, s32 c) #extern "X11"

s32 XSetFont(Display* a, GC* b, u64 c) #extern "X11"

s32 XSetFontPath(Display* a, u8** b, s32 c) #extern "X11"

s32 XSetForeground(Display* a, GC* b, u64 c) #extern "X11"

s32 XSetFunction(Display* a, GC* b, s32 c) #extern "X11"

s32 XSetGraphicsExposures(Display* a, GC* b, s32 c) #extern "X11"

s32 XSetIconName(Display* a, u64 b, u8* c) #extern "X11"

s32 XSetInputFocus(Display* a, u64 b, s32 c, u64 d) #extern "X11"

s32 XSetLineAttributes(Display* a, GC* b, u32 c, s32 d, s32 e, s32 f) #extern "X11"

s32 XSetModifierMapping(Display* a, XModifierKeymap* b) #extern "X11"

s32 XSetPlaneMask(Display* a, GC* b, u64 c) #extern "X11"

s32 XSetPointerMapping(Display* a, u8* b, s32 c) #extern "X11"

s32 XSetScreenSaver(Display* a, s32 b, s32 c, s32 d, s32 e) #extern "X11"

s32 XSetSelectionOwner(Display* a, u64 b, u64 c, u64 d) #extern "X11"

s32 XSetState(Display* a, GC* b, u64 c, u64 d, s32 e, u64 f) #extern "X11"

s32 XSetStipple(Display* a, GC* b, u64 c) #extern "X11"

s32 XSetSubwindowMode(Display* a, GC* b, s32 c) #extern "X11"

s32 XSetTSOrigin(Display* a, GC* b, s32 c, s32 d) #extern "X11"

s32 XSetTile(Display* a, GC* b, u64 c) #extern "X11"

s32 XSetWindowBackground(Display* a, u64 b, u64 c) #extern "X11"

s32 XSetWindowBackgroundPixmap(Display* a, u64 b, u64 c) #extern "X11"

s32 XSetWindowBorder(Display* a, u64 b, u64 c) #extern "X11"

s32 XSetWindowBorderPixmap(Display* a, u64 b, u64 c) #extern "X11"

s32 XSetWindowBorderWidth(Display* a, u64 b, u32 c) #extern "X11"

s32 XSetWindowColormap(Display* a, u64 b, u64 c) #extern "X11"

s32 XStoreBuffer(Display* a, u8* b, s32 c, s32 d) #extern "X11"

s32 XStoreBytes(Display* a, u8* b, s32 c) #extern "X11"

s32 XStoreColor(Display* a, u64 b, XColor* c) #extern "X11"

s32 XStoreColors(Display* a, u64 b, XColor* c, s32 d) #extern "X11"

s32 XStoreName(Display* a, u64 b, u8* c) #extern "X11"

s32 XStoreNamedColor(Display* a, u64 b, u8* c, u64 d, s32 e) #extern "X11"

s32 XSync(Display* a, bool b) #extern "X11"

s32 XTextExtents(XFontStruct* a, u8* b, s32 c, s32* d, s32* e, s32* f, XCharStruct* g) #extern "X11"

s32 XTextExtents16(XFontStruct* a, XChar2b* b, s32 c, s32* d, s32* e, s32* f, XCharStruct* g) #extern "X11"

s32 XTextWidth(XFontStruct* a, u8* b, s32 c) #extern "X11"

s32 XTextWidth16(XFontStruct* a, XChar2b* b, s32 c) #extern "X11"

s32 XTranslateCoordinates(Display* a, u64 b, u64 c, s32 d, s32 e, s32* f, s32* g, u64* h) #extern "X11"

s32 XUndefineCursor(Display* a, u64 b) #extern "X11"

s32 XUngrabButton(Display* a, u32 b, u32 c, u64 d) #extern "X11"

s32 XUngrabKey(Display* a, s32 b, u32 c, u64 d) #extern "X11"

s32 XUngrabKeyboard(Display* a, u64 b) #extern "X11"

s32 XUngrabPointer(Display* a, u64 b) #extern "X11"

s32 XUngrabServer(Display* a) #extern "X11"

s32 XUninstallColormap(Display* a, u64 b) #extern "X11"

s32 XUnloadFont(Display* a, u64 b) #extern "X11"

s32 XUnmapSubwindows(Display* a, u64 b) #extern "X11"

s32 XUnmapWindow(Display* a, u64 b) #extern "X11"

s32 XVendorRelease(Display* a) #extern "X11"

s32 XWarpPointer(Display* a, u64 b, u64 c, s32 d, s32 e, u32 f, u32 g, s32 h, s32 i) #extern "X11"

s32 XWidthMMOfScreen(Screen* a) #extern "X11"

s32 XWidthOfScreen(Screen* a) #extern "X11"

s32 XWindowEvent(Display* a, u64 b, s64 c, XEvent* d) #extern "X11"

s32 XWriteBitmapFile(Display* a, u8* b, u64 c, u32 d, u32 e, s32 f, s32 g) #extern "X11"

s32 XSupportsLocale() #extern "X11"

u8* XSetLocaleModifiers(u8* a) #extern "X11"

XOM* XOpenOM(Display* a, _XrmHashBucketRec* b, u8* c, u8* d) #extern "X11"

s32 XCloseOM(XOM* a) #extern "X11"

u8* XSetOMValues(XOM* a, ... b) #extern "X11"

u8* XGetOMValues(XOM* a, ... b) #extern "X11"

Display* XDisplayOfOM(XOM* a) #extern "X11"

u8* XLocaleOfOM(XOM* a) #extern "X11"

XOC* XCreateOC(XOM* a, ... b) #extern "X11"

XDestroyOC(XOC* a) #extern "X11"

XOM* XOMOfOC(XOC* a) #extern "X11"

u8* XSetOCValues(XOC* a, ... b) #extern "X11"

u8* XGetOCValues(XOC* a, ... b) #extern "X11"

XFontSet* XCreateFontSet(Display* a, u8* b, u8*** c, s32* d, u8** e) #extern "X11"

XFreeFontSet(Display* a, XFontSet* b) #extern "X11"

s32 XFontsOfFontSet(XFontSet* a, XFontStruct*** b, u8*** c) #extern "X11"

u8* XBaseFontNameListOfFontSet(XFontSet* a) #extern "X11"

u8* XLocaleOfFontSet(XFontSet* a) #extern "X11"

s32 XContextDependentDrawing(XFontSet* a) #extern "X11"

s32 XDirectionalDependentDrawing(XFontSet* a) #extern "X11"

s32 XContextualDrawing(XFontSet* a) #extern "X11"

XFontSetExtents* XExtentsOfFontSet(XFontSet* a) #extern "X11"

s32 XmbTextEscapement(XFontSet* a, u8* b, s32 c) #extern "X11"

s32 XwcTextEscapement(XFontSet* a, s32* b, s32 c) #extern "X11"

s32 Xutf8TextEscapement(XFontSet* a, u8* b, s32 c) #extern "X11"

s32 XmbTextExtents(XFontSet* a, u8* b, s32 c, XRectangle* d, XRectangle* e) #extern "X11"

s32 XwcTextExtents(XFontSet* a, s32* b, s32 c, XRectangle* d, XRectangle* e) #extern "X11"

s32 Xutf8TextExtents(XFontSet* a, u8* b, s32 c, XRectangle* d, XRectangle* e) #extern "X11"

s32 XmbTextPerCharExtents(XFontSet* a, u8* b, s32 c, XRectangle* d, XRectangle* e, s32 f, s32* g, XRectangle* h, XRectangle* i) #extern "X11"

s32 XwcTextPerCharExtents(XFontSet* a, s32* b, s32 c, XRectangle* d, XRectangle* e, s32 f, s32* g, XRectangle* h, XRectangle* i) #extern "X11"

s32 Xutf8TextPerCharExtents(XFontSet* a, u8* b, s32 c, XRectangle* d, XRectangle* e, s32 f, s32* g, XRectangle* h, XRectangle* i) #extern "X11"

XmbDrawText(Display* a, u64 b, GC* c, s32 d, s32 e, XmbTextItem* f, s32 g) #extern "X11"

XwcDrawText(Display* a, u64 b, GC* c, s32 d, s32 e, XwcTextItem* f, s32 g) #extern "X11"

Xutf8DrawText(Display* a, u64 b, GC* c, s32 d, s32 e, XmbTextItem* f, s32 g) #extern "X11"

XmbDrawString(Display* a, u64 b, XFontSet* c, GC* d, s32 e, s32 f, u8* g, s32 h) #extern "X11"

XwcDrawString(Display* a, u64 b, XFontSet* c, GC* d, s32 e, s32 f, s32* g, s32 h) #extern "X11"

Xutf8DrawString(Display* a, u64 b, XFontSet* c, GC* d, s32 e, s32 f, u8* g, s32 h) #extern "X11"

XmbDrawImageString(Display* a, u64 b, XFontSet* c, GC* d, s32 e, s32 f, u8* g, s32 h) #extern "X11"

XwcDrawImageString(Display* a, u64 b, XFontSet* c, GC* d, s32 e, s32 f, s32* g, s32 h) #extern "X11"

Xutf8DrawImageString(Display* a, u64 b, XFontSet* c, GC* d, s32 e, s32 f, u8* g, s32 h) #extern "X11"

XIM* XOpenIM(Display* a, _XrmHashBucketRec* b, u8* c, u8* d) #extern "X11"

s32 XCloseIM(XIM* a) #extern "X11"

u8* XGetIMValues(XIM* a, ... b) #extern "X11"

u8* XSetIMValues(XIM* a, ... b) #extern "X11"

Display* XDisplayOfIM(XIM* a) #extern "X11"

u8* XLocaleOfIM(XIM* a) #extern "X11"

XIC* XCreateIC(XIM* a, ... b) #extern "X11"

XDestroyIC(XIC* a) #extern "X11"

XSetICFocus(XIC* a) #extern "X11"

XUnsetICFocus(XIC* a) #extern "X11"

s32* XwcResetIC(XIC* a) #extern "X11"

u8* XmbResetIC(XIC* a) #extern "X11"

u8* Xutf8ResetIC(XIC* a) #extern "X11"

u8* XSetICValues(XIC* a, ... b) #extern "X11"

u8* XGetICValues(XIC* a, ... b) #extern "X11"

XIM* XIMOfIC(XIC* a) #extern "X11"

s32 XFilterEvent(XEvent* a, u64 b) #extern "X11"

s32 XmbLookupString(XIC* a, XKeyEvent* b, u8* c, s32 d, u64* e, s32* f) #extern "X11"

s32 XwcLookupString(XIC* a, XKeyEvent* b, s32* c, s32 d, u64* e, s32* f) #extern "X11"

s32 Xutf8LookupString(XIC* a, XKeyEvent* b, u8* c, s32 d, u64* e, s32* f) #extern "X11"

void* XVaCreateNestedList(s32 a, ... b) #extern "X11"

s32 XRegisterIMInstantiateCallback(Display* a, _XrmHashBucketRec* b, u8* c, u8* d, XIDProc e, u8* f) #extern "X11"

s32 XUnregisterIMInstantiateCallback(Display* a, _XrmHashBucketRec* b, u8* c, u8* d, XIDProc e, u8* f) #extern "X11"

interface XConnectionWatchProc(Display* a, u8* b, s32 c, s32 d, u8** e)

s32 XInternalConnectionNumbers(Display* a, s32** b, s32* c) #extern "X11"

XProcessInternalConnection(Display* a, s32 b) #extern "X11"

s32 XAddConnectionWatch(Display* a, XConnectionWatchProc b, u8* c) #extern "X11"

XRemoveConnectionWatch(Display* a, XConnectionWatchProc b, u8* c) #extern "X11"

XSetAuthorization(u8* a, s32 b, u8* c, s32 d) #extern "X11"

s32 _Xmbtowc(s32* a, u8* b, s32 c) #extern "X11"

s32 _Xwctomb(u8* a, s32 b) #extern "X11"

s32 XGetEventData(Display* a, XGenericEventCookie* b) #extern "X11"

XFreeEventData(Display* a, XGenericEventCookie* b) #extern "X11"

XClassHint* XAllocClassHint() #extern "X11"

XIconSize* XAllocIconSize() #extern "X11"

XSizeHints* XAllocSizeHints() #extern "X11"

XStandardColormap* XAllocStandardColormap() #extern "X11"

XWMHints* XAllocWMHints() #extern "X11"

s32 XClipBox(Region* a, XRectangle* b) #extern "X11"

Region* XCreateRegion() #extern "X11"

u8* XDefaultString() #extern "X11"

s32 XDeleteContext(Display* a, u64 b, s32 c) #extern "X11"

s32 XDestroyRegion(Region* a) #extern "X11"

s32 XEmptyRegion(Region* a) #extern "X11"

s32 XEqualRegion(Region* a, Region* b) #extern "X11"

s32 XFindContext(Display* a, u64 b, s32 c, u8** d) #extern "X11"

s32 XGetClassHint(Display* a, u64 b, XClassHint* c) #extern "X11"

s32 XGetIconSizes(Display* a, u64 b, XIconSize** c, s32* d) #extern "X11"

s32 XGetNormalHints(Display* a, u64 b, XSizeHints* c) #extern "X11"

s32 XGetRGBColormaps(Display* a, u64 b, XStandardColormap** c, s32* d, u64 e) #extern "X11"

s32 XGetSizeHints(Display* a, u64 b, XSizeHints* c, u64 d) #extern "X11"

s32 XGetStandardColormap(Display* a, u64 b, XStandardColormap* c, u64 d) #extern "X11"

s32 XGetTextProperty(Display* a, u64 b, XTextProperty* c, u64 d) #extern "X11"

XVisualInfo* XGetVisualInfo(Display* a, s64 b, XVisualInfo* c, s32* d) #extern "X11"

s32 XGetWMClientMachine(Display* a, u64 b, XTextProperty* c) #extern "X11"

XWMHints* XGetWMHints(Display* a, u64 b) #extern "X11"

s32 XGetWMIconName(Display* a, u64 b, XTextProperty* c) #extern "X11"

s32 XGetWMName(Display* a, u64 b, XTextProperty* c) #extern "X11"

s32 XGetWMNormalHints(Display* a, u64 b, XSizeHints* c, s64* d) #extern "X11"

s32 XGetWMSizeHints(Display* a, u64 b, XSizeHints* c, s64* d, u64 e) #extern "X11"

s32 XGetZoomHints(Display* a, u64 b, XSizeHints* c) #extern "X11"

s32 XIntersectRegion(Region* a, Region* b, Region* c) #extern "X11"

XConvertCase(u64 a, u64* b, u64* c) #extern "X11"

s32 XLookupString(XKeyEvent* a, u8* b, s32 c, u64* d, XComposeStatus* e) #extern "X11"

s32 XMatchVisualInfo(Display* a, s32 b, s32 c, s32 d, XVisualInfo* e) #extern "X11"

s32 XOffsetRegion(Region* a, s32 b, s32 c) #extern "X11"

s32 XPointInRegion(Region* a, s32 b, s32 c) #extern "X11"

Region* XPolygonRegion(XPoint* a, s32 b, s32 c) #extern "X11"

s32 XRectInRegion(Region* a, s32 b, s32 c, u32 d, u32 e) #extern "X11"

s32 XSaveContext(Display* a, u64 b, s32 c, u8* d) #extern "X11"

s32 XSetClassHint(Display* a, u64 b, XClassHint* c) #extern "X11"

s32 XSetIconSizes(Display* a, u64 b, XIconSize* c, s32 d) #extern "X11"

s32 XSetNormalHints(Display* a, u64 b, XSizeHints* c) #extern "X11"

XSetRGBColormaps(Display* a, u64 b, XStandardColormap* c, s32 d, u64 e) #extern "X11"

s32 XSetSizeHints(Display* a, u64 b, XSizeHints* c, u64 d) #extern "X11"

s32 XSetStandardProperties(Display* a, u64 b, u8* c, u8* d, u64 e, u8** f, s32 g, XSizeHints* h) #extern "X11"

XSetTextProperty(Display* a, u64 b, XTextProperty* c, u64 d) #extern "X11"

XSetWMClientMachine(Display* a, u64 b, XTextProperty* c) #extern "X11"

s32 XSetWMHints(Display* a, u64 b, XWMHints* c) #extern "X11"

XSetWMIconName(Display* a, u64 b, XTextProperty* c) #extern "X11"

XSetWMName(Display* a, u64 b, XTextProperty* c) #extern "X11"

XSetWMNormalHints(Display* a, u64 b, XSizeHints* c) #extern "X11"

XSetWMProperties(Display* a, u64 b, XTextProperty* c, XTextProperty* d, u8** e, s32 f, XSizeHints* g, XWMHints* h, XClassHint* i) #extern "X11"

XmbSetWMProperties(Display* a, u64 b, u8* c, u8* d, u8** e, s32 f, XSizeHints* g, XWMHints* h, XClassHint* i) #extern "X11"

Xutf8SetWMProperties(Display* a, u64 b, u8* c, u8* d, u8** e, s32 f, XSizeHints* g, XWMHints* h, XClassHint* i) #extern "X11"

XSetWMSizeHints(Display* a, u64 b, XSizeHints* c, u64 d) #extern "X11"

s32 XSetRegion(Display* a, GC* b, Region* c) #extern "X11"

XSetStandardColormap(Display* a, u64 b, XStandardColormap* c, u64 d) #extern "X11"

s32 XSetZoomHints(Display* a, u64 b, XSizeHints* c) #extern "X11"

s32 XShrinkRegion(Region* a, s32 b, s32 c) #extern "X11"

s32 XStringListToTextProperty(u8** a, s32 b, XTextProperty* c) #extern "X11"

s32 XSubtractRegion(Region* a, Region* b, Region* c) #extern "X11"

s32 XmbTextListToTextProperty(Display* display, u8** list, s32 count, XICCEncodingStyle style, XTextProperty* text_prop_return) #extern "X11"

s32 XwcTextListToTextProperty(Display* display, s32** list, s32 count, XICCEncodingStyle style, XTextProperty* text_prop_return) #extern "X11"

s32 Xutf8TextListToTextProperty(Display* display, u8** list, s32 count, XICCEncodingStyle style, XTextProperty* text_prop_return) #extern "X11"

XwcFreeStringList(s32** list) #extern "X11"

s32 XTextPropertyToStringList(XTextProperty* a, u8*** b, s32* c) #extern "X11"

s32 XmbTextPropertyToTextList(Display* display, XTextProperty* text_prop, u8*** list_return, s32* count_return) #extern "X11"

s32 XwcTextPropertyToTextList(Display* display, XTextProperty* text_prop, s32*** list_return, s32* count_return) #extern "X11"

s32 Xutf8TextPropertyToTextList(Display* display, XTextProperty* text_prop, u8*** list_return, s32* count_return) #extern "X11"

s32 XUnionRectWithRegion(XRectangle* a, Region* b, Region* c) #extern "X11"

s32 XUnionRegion(Region* a, Region* b, Region* c) #extern "X11"

s32 XWMGeometry(Display* a, s32 b, u8* c, u8* d, u32 e, XSizeHints* f, s32* g, s32* h, s32* i, s32* j, s32* k) #extern "X11"

s32 XXorRegion(Region* a, Region* b, Region* c) #extern "X11"
