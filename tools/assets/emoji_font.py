"""Own the bundled emoji font and its process-local Fontconfig selection.

Emoji and application-icon bakers share this setup. No system font installation
is needed, and host fonts must not substitute for the bundled build input.
"""

import ctypes
import ctypes.util

from tools import ROOT

DEFAULT_FONT = ROOT / 'assets/fonts/noto-emoji/NotoColorEmoji.ttf'
FAMILY = 'Noto Color Emoji'


def configure_font() -> None:
    """Select only the bundled font before creating the baker's Pango font map."""
    if not DEFAULT_FONT.is_file():
        raise FileNotFoundError(DEFAULT_FONT)
    library = ctypes.util.find_library('fontconfig')
    if library is None:
        raise RuntimeError('emoji baking requires Fontconfig')
    fc = ctypes.CDLL(library)
    fc.FcConfigCreate.argtypes = []
    fc.FcConfigCreate.restype = ctypes.c_void_p
    for name, arguments in (
        ('FcConfigAppFontAddFile', [ctypes.c_void_p, ctypes.c_char_p]),
        ('FcConfigBuildFonts', [ctypes.c_void_p]),
        ('FcConfigSetCurrent', [ctypes.c_void_p]),
    ):
        function = getattr(fc, name)
        function.argtypes = arguments
        function.restype = ctypes.c_int
    fc.FcConfigDestroy.argtypes = [ctypes.c_void_p]
    fc.FcConfigDestroy.restype = None

    config = fc.FcConfigCreate()
    if not config:
        raise RuntimeError('cannot create emoji font configuration')
    try:
        if not fc.FcConfigAppFontAddFile(config, str(DEFAULT_FONT).encode()):
            raise RuntimeError(f'cannot load emoji font: {DEFAULT_FONT}')
        if not fc.FcConfigBuildFonts(config) or not fc.FcConfigSetCurrent(config):
            raise RuntimeError('cannot select bundled emoji font')
    finally:
        # SetCurrent retains its own reference for this baking process.
        fc.FcConfigDestroy(config)
