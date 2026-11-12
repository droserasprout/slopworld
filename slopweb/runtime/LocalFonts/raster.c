// Owns FreeType faces and synchronous outline rasterization. Atlas placement and
// texture lifetime belong to BrowserFontBank; terminal emoji never enters here.
#include <ft2build.h>
#include FT_FREETYPE_H
#include FT_SYNTHESIS_H
#include <stdlib.h>
#include <math.h>

static FT_Library library;
typedef struct { FT_Face face; unsigned char *bytes; } Face;
typedef struct {
    float advance, x, y, ascent, descent;
    int width, height, pitch;
    unsigned char *pixels;
} Glyph;
static Glyph result;

Face *font_open(unsigned char *bytes, int length) {
    if (!library && FT_Init_FreeType(&library)) return NULL;
    Face *f = calloc(1, sizeof(Face));
    if (!f) return NULL;
    if (FT_New_Memory_Face(library, bytes, length, 0, &f->face)) { free(f); return NULL; }
    if (FT_Select_Charmap(f->face, FT_ENCODING_UNICODE)) { FT_Done_Face(f->face); free(f); return NULL; }
    f->bytes = bytes;
    return f;
}
void font_close(Face *f) {
    if (!f) return;
    FT_Done_Face(f->face); free(f->bytes); free(f);
}
void font_shutdown(void) { if (library) FT_Done_FreeType(library); library = NULL; }
int font_size(Face *f, float size, float density, unsigned flags) {
    if (!f || !isfinite(size) || !isfinite(density) || size <= 0 || density <= 0) return -1;
    FT_Size_RequestRec req = { (flags & 512) ? FT_SIZE_REQUEST_TYPE_NOMINAL : FT_SIZE_REQUEST_TYPE_REAL_DIM,
        0, (FT_Long)(floorf(size + .5f) * 64 * density), 0, 0 };
    if (FT_Request_Size(f->face, &req)) return -1;
    result.ascent = f->face->size->metrics.ascender / (64.0f * density);
    result.descent = f->face->size->metrics.descender / (64.0f * density);
    return 1;
}
// 0 means absent coverage; -1 is a rasterization failure, never a missing glyph.
int font_glyph(Face *f, unsigned scalar, float size, float density, unsigned flags) {
    if (!f) return -1;
    FT_UInt index = FT_Get_Char_Index(f->face, scalar);
    if (!index && scalar != 0) return 0;
    if (font_size(f, size, density, flags) < 0) return -1;
    FT_Int32 load = FT_LOAD_NO_BITMAP;
    if (flags & 1) load |= FT_LOAD_NO_HINTING;
    if (flags & 2) load |= FT_LOAD_NO_AUTOHINT;
    if (flags & 4) load |= FT_LOAD_FORCE_AUTOHINT;
    load |= (flags & 8) ? FT_LOAD_TARGET_LIGHT : (flags & 16) ? FT_LOAD_TARGET_MONO : FT_LOAD_TARGET_NORMAL;
    if (FT_Load_Glyph(f->face, index, load)) return -1;
    FT_GlyphSlot slot = f->face->glyph;
    if (flags & 32) FT_GlyphSlot_Embolden(slot);
    if (flags & 64) FT_GlyphSlot_Oblique(slot);
    if (FT_Render_Glyph(slot, (flags & 128) ? FT_RENDER_MODE_MONO : FT_RENDER_MODE_NORMAL)) return -1;
    if (slot->bitmap.pixel_mode != FT_PIXEL_MODE_GRAY) return -1;
    result = (Glyph){ slot->advance.x / (64.0f * density), slot->bitmap_left / density,
        -slot->bitmap_top / density, f->face->size->metrics.ascender / (64.0f * density),
        f->face->size->metrics.descender / (64.0f * density), slot->bitmap.width, slot->bitmap.rows,
        slot->bitmap.pitch, slot->bitmap.buffer };
    return 1;
}
Glyph *font_result(void) { return &result; }
