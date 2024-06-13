#if os == OS.Linux {
    #system_library freetype "freetype"
}
#if os == OS.Windows {
    #system_library freetype "freetype" "lib"
}

struct FT_Memory {}
interface void* FT_Alloc_Func(FT_Memory* memory, s64 size)

interface FT_Free_Func(FT_Memory* memory, void* block)

interface void* FT_Realloc_Func(FT_Memory* memory, s64 cur_size, s64 new_size, void* block)

struct FT_MemoryRec_ {
    user: void*;
    alloc: FT_Alloc_Func;
    free: FT_Free_Func;
    realloc: FT_Realloc_Func;
}

struct FT_Stream {}
union FT_StreamDesc {
    value: s64;
    pointer: void*;
}

interface u64 FT_Stream_IoFunc(FT_Stream* stream, u64 offset, u8* buffer, u64 count)

interface FT_Stream_CloseFunc(FT_Stream* stream)

struct FT_StreamRec {
    base: u8*;
    size: u64;
    pos: u64;
    descriptor: FT_StreamDesc;
    pathname: FT_StreamDesc;
    read: FT_Stream_IoFunc;
    close: FT_Stream_CloseFunc;
    memory: FT_Memory*;
    cursor: u8*;
    limit: u8*;
}

struct FT_Vector {
    x: s64;
    y: s64;
}

struct FT_BBox {
    xMin: s64;
    yMin: s64;
    xMax: s64;
    yMax: s64;
}

enum FT_Pixel_Mode {
    FT_PIXEL_MODE_NONE = 0;
    FT_PIXEL_MODE_MONO;
    FT_PIXEL_MODE_GRAY;
    FT_PIXEL_MODE_GRAY2;
    FT_PIXEL_MODE_GRAY4;
    FT_PIXEL_MODE_LCD;
    FT_PIXEL_MODE_LCD_V;
    FT_PIXEL_MODE_BGRA;
    FT_PIXEL_MODE_MAX;
}

struct FT_Bitmap {
    rows: u32;
    width: u32;
    pitch: s32;
    buffer: u8*;
    num_grays: u16;
    pixel_mode: u8;
    palette_mode: u8;
    palette: void*;
}

struct FT_Outline {
    n_contours: s16;
    n_points: s16;
    points: FT_Vector*;
    tags: u8*;
    contours: s16*;
    flags: s32;
}

interface s32 FT_Outline_MoveToFunc(FT_Vector* to, void* user)

interface s32 FT_Outline_LineToFunc(FT_Vector* to, void* user)

interface s32 FT_Outline_ConicToFunc(FT_Vector* control, FT_Vector* to, void* user)

interface s32 FT_Outline_CubicToFunc(FT_Vector* control1, FT_Vector* control2, FT_Vector* to, void* user)

struct FT_Outline_Funcs {
    move_to: FT_Outline_MoveToFunc;
    line_to: FT_Outline_LineToFunc;
    conic_to: FT_Outline_ConicToFunc;
    cubic_to: FT_Outline_CubicToFunc;
    shift: s32;
    delta: s64;
}

enum FT_Glyph_Format {
    FT_GLYPH_FORMAT_NONE = 0;
    FT_GLYPH_FORMAT_COMPOSITE = 0x666F6D70;
    FT_GLYPH_FORMAT_BITMAP = 0x62697473;
    FT_GLYPH_FORMAT_OUTLINE = 0x6F75746C;
    FT_GLYPH_FORMAT_PLOTTER = 0x706C6F74;
    FT_GLYPH_FORMAT_SVG = 0x53564720;
}

struct FT_Span {
    x: s16;
    len: u16;
    coverage: u8;
}

interface FT_SpanFunc(s32 y, s32 count, FT_Span* spans, void* user)

interface s32 FT_Raster_BitTest_Func(s32 y, s32 x, void* user)

interface FT_Raster_BitSet_Func(s32 y, s32 x, void* user)

struct FT_Raster_Params {
    target: FT_Bitmap*;
    source: void*;
    flags: s32;
    gray_spans: FT_SpanFunc;
    black_spans: FT_SpanFunc;
    bit_test: FT_Raster_BitTest_Func;
    bit_set: FT_Raster_BitSet_Func;
    user: void*;
    clip_box: FT_BBox;
}

struct FT_Raster {}
interface s32 FT_Raster_NewFunc(void* memory, FT_Raster** raster)

interface FT_Raster_DoneFunc(FT_Raster* raster)

interface FT_Raster_ResetFunc(FT_Raster* raster, u8* pool_base, u64 pool_size)

interface s32 FT_Raster_SetModeFunc(FT_Raster* raster, u64 mode, void* args)

interface s32 FT_Raster_RenderFunc(FT_Raster* raster, FT_Raster_Params* params)

struct FT_Raster_Funcs {
    glyph_format: FT_Glyph_Format;
    raster_new: FT_Raster_NewFunc;
    raster_reset: FT_Raster_ResetFunc;
    raster_set_mode: FT_Raster_SetModeFunc;
    raster_render: FT_Raster_RenderFunc;
    raster_done: FT_Raster_DoneFunc;
}

struct FT_UnitVector {
    x: s16;
    y: s16;
}

struct FT_Matrix {
    xx: s64;
    xy: s64;
    yx: s64;
    yy: s64;
}

struct FT_Data {
    pointer: u8*;
    length: u32;
}

interface FT_Generic_Finalizer(void* object)

struct FT_Generic {
    data: void*;
    finalizer: FT_Generic_Finalizer;
}

struct FT_ListNode {}
struct FT_List {}
struct FT_ListNodeRec {
    prev: FT_ListNode*;
    next: FT_ListNode*;
    data: void*;
}

struct FT_ListRec {
    head: FT_ListNode*;
    tail: FT_ListNode*;
}

struct FT_Glyph_Metrics {
    width: s64;
    height: s64;
    horiBearingX: s64;
    horiBearingY: s64;
    horiAdvance: s64;
    vertBearingX: s64;
    vertBearingY: s64;
    vertAdvance: s64;
}

struct FT_Bitmap_Size {
    height: s16;
    width: s16;
    size: s64;
    x_ppem: s64;
    y_ppem: s64;
}

struct FT_Library {}
struct FT_Module {}
struct FT_Driver {}
struct FT_Renderer {}
struct FT_Size {}
struct FT_CharMap {}
enum FT_Encoding {
    FT_ENCODING_NONE = 0;
    FT_ENCODING_MS_SYMBOL = 0x73796D62;
    FT_ENCODING_UNICODE = 0x756E6963;
    FT_ENCODING_SJIS = 0x736A6973;
    FT_ENCODING_PRC = 0x73622020;
    FT_ENCODING_BIG5 = 0x62696735;
    FT_ENCODING_WANSUNG = 0x77616E73;
    FT_ENCODING_JOHAB = 0x6A6F6861;
    FT_ENCODING_GB2312 = FT_ENCODING_PRC;
    FT_ENCODING_MS_SJIS = FT_ENCODING_SJIS;
    FT_ENCODING_MS_GB2312 = FT_ENCODING_PRC;
    FT_ENCODING_MS_BIG5 = FT_ENCODING_BIG5;
    FT_ENCODING_MS_WANSUNG = FT_ENCODING_WANSUNG;
    FT_ENCODING_MS_JOHAB = FT_ENCODING_JOHAB;
    FT_ENCODING_ADOBE_STANDARD = 0x41444F42;
    FT_ENCODING_ADOBE_EXPERT = 0x41444245;
    FT_ENCODING_ADOBE_CUSTOM = 0x41444243;
    FT_ENCODING_ADOBE_LATIN_1 = 0x6C617431;
    FT_ENCODING_OLD_LATIN_2 = 0x6C617432;
    FT_ENCODING_APPLE_ROMAN = 0x61726D6E;
}

struct FT_CharMapRec {
    face: FT_Face*;
    encoding: FT_Encoding;
    platform_id: u16;
    encoding_id: u16;
}

struct FT_Face_Internal {}
struct FT_Face {
    num_faces: s64;
    face_index: s64;
    face_flags: s64;
    style_flags: s64;
    num_glyphs: s64;
    family_name: u8*;
    style_name: u8*;
    num_fixed_sizes: s32;
    available_sizes: FT_Bitmap_Size*;
    num_charmaps: s32;
    charmaps: FT_CharMap**;
    generic: FT_Generic;
    bbox: FT_BBox;
    units_per_EM: u16;
    ascender: s16;
    descender: s16;
    height: s16;
    max_advance_width: s16;
    max_advance_height: s16;
    underline_position: s16;
    underline_thickness: s16;
    glyph: FT_GlyphSlot*;
    size: FT_Size*;
    charmap: FT_CharMap*;
    driver: FT_Driver*;
    memory: FT_Memory*;
    stream: FT_Stream*;
    sizes_list: FT_ListRec;
    autohint: FT_Generic;
    extensions: void*;
    internal: FT_Face_Internal*;
}

struct FT_Size_Internal {}
struct FT_Size_Metrics {
    x_ppem: u16;
    y_ppem: u16;
    x_scale: s64;
    y_scale: s64;
    ascender: s64;
    descender: s64;
    height: s64;
    max_advance: s64;
}

struct FT_SizeRec {
    face: FT_Face*;
    generic: FT_Generic;
    metrics: FT_Size_Metrics;
    internal: FT_Size_Internal*;
}

struct FT_SubGlyph {}
struct FT_Slot_Internal {}
struct FT_GlyphSlot {
    library: FT_Library*;
    face: FT_Face*;
    next: FT_GlyphSlot*;
    glyph_index: u32;
    generic: FT_Generic;
    metrics: FT_Glyph_Metrics;
    linearHoriAdvance: s64;
    linearVertAdvance: s64;
    advance: FT_Vector;
    format: FT_Glyph_Format;
    bitmap: FT_Bitmap;
    bitmap_left: s32;
    bitmap_top: s32;
    outline: FT_Outline;
    num_subglyphs: u32;
    subglyphs: FT_SubGlyph*;
    control_data: void*;
    control_len: s64;
    lsb_delta: s64;
    rsb_delta: s64;
    other: void*;
    internal: FT_Slot_Internal*;
}

s32 FT_Init_FreeType(FT_Library** alibrary) #extern freetype

s32 FT_Done_FreeType(FT_Library* library) #extern freetype

struct FT_Parameter {
    tag: u64;
    data: void*;
}

struct FT_Open_Args {
    flags: u32;
    memory_base: u8*;
    memory_size: s64;
    pathname: u8*;
    stream: FT_Stream*;
    driver: FT_Module*;
    num_params: s32;
    params: FT_Parameter*;
}

s32 FT_New_Face(FT_Library* library, u8* filepathname, s64 face_index, FT_Face** aface) #extern freetype

s32 FT_New_Memory_Face(FT_Library* library, u8* file_base, s64 file_size, s64 face_index, FT_Face** aface) #extern freetype

s32 FT_Open_Face(FT_Library* library, FT_Open_Args* args, s64 face_index, FT_Face** aface) #extern freetype

s32 FT_Attach_File(FT_Face* face, u8* filepathname) #extern freetype

s32 FT_Attach_Stream(FT_Face* face, FT_Open_Args* parameters) #extern freetype

s32 FT_Reference_Face(FT_Face* face) #extern freetype

s32 FT_Done_Face(FT_Face* face) #extern freetype

s32 FT_Select_Size(FT_Face* face, s32 strike_index) #extern freetype

enum FT_Size_Request_Type {
    FT_SIZE_REQUEST_TYPE_NOMINAL;
    FT_SIZE_REQUEST_TYPE_REAL_DIM;
    FT_SIZE_REQUEST_TYPE_BBOX;
    FT_SIZE_REQUEST_TYPE_CELL;
    FT_SIZE_REQUEST_TYPE_SCALES;
    FT_SIZE_REQUEST_TYPE_MAX;
}

struct FT_Size_RequestRec {
    type: FT_Size_Request_Type;
    width: s64;
    height: s64;
    horiResolution: u32;
    vertResolution: u32;
}

s32 FT_Request_Size(FT_Face* face, FT_Size_RequestRec* req) #extern freetype

s32 FT_Set_Char_Size(FT_Face* face, s64 char_width, s64 char_height, u32 horz_resolution, u32 vert_resolution) #extern freetype

s32 FT_Set_Pixel_Sizes(FT_Face* face, u32 pixel_width, u32 pixel_height) #extern freetype

[flags]
enum FT_LoadFlags {
    FT_LOAD_DEFAULT                     = 0x0;
    FT_LOAD_NO_SCALE                    = 0x1;
    FT_LOAD_NO_HINTING                  = 0x2;
    FT_LOAD_RENDER                      = 0x4;
    FT_LOAD_NO_BITMAP                   = 0x8;
    FT_LOAD_VERTICAL_LAYOUT             = 0x10;
    FT_LOAD_FORCE_AUTOHINT              = 0x20;
    FT_LOAD_CROP_BITMAP                 = 0x40;
    FT_LOAD_PEDANTIC                    = 0x80;
    FT_LOAD_ADVANCE_ONLY                = 0x100;
    FT_LOAD_IGNORE_GLOBAL_ADVANCE_WIDTH = 0x200;
    FT_LOAD_NO_RECURSE                  = 0x400;
    FT_LOAD_IGNORE_TRANSFORM            = 0x800;
    FT_LOAD_MONOCHROME                  = 0x1000;
    FT_LOAD_LINEAR_DESIGN               = 0x2000;
    FT_LOAD_SBITS_ONLY                  = 0x4000;
    FT_LOAD_NO_AUTOHINT                 = 0x8000;
    FT_LOAD_COLOR                       = 0x100000;
    FT_LOAD_COMPUTE_METRICS             = 0x200000;
    FT_LOAD_BITMAP_METRICS_ONLY         = 0x400000;
    FT_LOAD_SVG_ONLY                    = 0x800000;
}

s32 FT_Load_Glyph(FT_Face* face, u32 glyph_index, FT_LoadFlags load_flags) #extern freetype

s32 FT_Load_Char(FT_Face* face, u64 char_code, FT_LoadFlags load_flags) #extern freetype

FT_Set_Transform(FT_Face* face, FT_Matrix* matrix, FT_Vector* delta) #extern freetype

FT_Get_Transform(FT_Face* face, FT_Matrix* matrix, FT_Vector* delta) #extern freetype

enum FT_Render_Mode {
    FT_RENDER_MODE_NORMAL = 0;
    FT_RENDER_MODE_LIGHT;
    FT_RENDER_MODE_MONO;
    FT_RENDER_MODE_LCD;
    FT_RENDER_MODE_LCD_V;
    FT_RENDER_MODE_SDF;
    FT_RENDER_MODE_MAX;
}

s32 FT_Render_Glyph(FT_GlyphSlot* slot, FT_Render_Mode render_mode) #extern freetype

enum FT_Kerning_Mode {
    FT_KERNING_DEFAULT = 0;
    FT_KERNING_UNFITTED;
    FT_KERNING_UNSCALED;
}

s32 FT_Get_Kerning(FT_Face* face, u32 left_glyph, u32 right_glyph, u32 kern_mode, FT_Vector* akerning) #extern freetype

s32 FT_Get_Track_Kerning(FT_Face* face, s64 point_size, s32 degree, s64* akerning) #extern freetype

s32 FT_Select_Charmap(FT_Face* face, FT_Encoding encoding) #extern freetype

s32 FT_Set_Charmap(FT_Face* face, FT_CharMap* charmap) #extern freetype

s32 FT_Get_Charmap_Index(FT_CharMap* charmap) #extern freetype

u32 FT_Get_Char_Index(FT_Face* face, u64 charcode) #extern freetype

u64 FT_Get_First_Char(FT_Face* face, u32* agindex) #extern freetype

u64 FT_Get_Next_Char(FT_Face* face, u64 char_code, u32* agindex) #extern freetype

s32 FT_Face_Properties(FT_Face* face, u32 num_properties, FT_Parameter* properties) #extern freetype

u32 FT_Get_Name_Index(FT_Face* face, u8* glyph_name) #extern freetype

s32 FT_Get_Glyph_Name(FT_Face* face, u32 glyph_index, void* buffer, u32 buffer_max) #extern freetype

u8* FT_Get_Postscript_Name(FT_Face* face) #extern freetype

s32 FT_Get_SubGlyph_Info(FT_GlyphSlot* glyph, u32 sub_index, s32* p_index, u32* p_flags, s32* p_arg1, s32* p_arg2, FT_Matrix* p_transform) #extern freetype

u16 FT_Get_FSType_Flags(FT_Face* face) #extern freetype

u32 FT_Face_GetCharVariantIndex(FT_Face* face, u64 charcode, u64 variantSelector) #extern freetype

s32 FT_Face_GetCharVariantIsDefault(FT_Face* face, u64 charcode, u64 variantSelector) #extern freetype

u32* FT_Face_GetVariantSelectors(FT_Face* face) #extern freetype

u32* FT_Face_GetVariantsOfChar(FT_Face* face, u64 charcode) #extern freetype

u32* FT_Face_GetCharsOfVariant(FT_Face* face, u64 variantSelector) #extern freetype

s64 FT_MulDiv(s64 a, s64 b, s64 c) #extern freetype

s64 FT_MulFix(s64 a, s64 b) #extern freetype

s64 FT_DivFix(s64 a, s64 b) #extern freetype

s64 FT_RoundFix(s64 a) #extern freetype

s64 FT_CeilFix(s64 a) #extern freetype

s64 FT_FloorFix(s64 a) #extern freetype

FT_Vector_Transform(FT_Vector* vector, FT_Matrix* matrix) #extern freetype

FT_Library_Version(FT_Library* library, s32* amajor, s32* aminor, s32* apatch) #extern freetype

u8 FT_Face_CheckTrueTypePatents(FT_Face* face) #extern freetype

u8 FT_Face_SetUnpatentedHinting(FT_Face* face, u8 value) #extern freetype
