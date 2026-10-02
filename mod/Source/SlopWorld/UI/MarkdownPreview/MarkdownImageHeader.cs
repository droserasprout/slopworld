namespace SlopWorld
{
    // Unity's shipped ImageConversion API only exposes LoadImage, which allocates pixels.
    // Read dimensions first: a small compressed file can expand beyond the texture budget.
    // This is a PNG/JPEG allocation guard, not a decoder or full format validator. Unity
    // validates the payload afterward; callers also compare decoded and declared dimensions.
    static class MarkdownImageHeader
    {
        const int MaxDimension = 4096;
        const int RgbaBytesPerPixel = 4;
        const int JpegMarkerPrefix = 0xFF;
        const int JpegStartOfImage = 0xD8;
        const int JpegEndOfImage = 0xD9;
        const int JpegStartOfScan = 0xDA;

        public static bool TrySize(byte[] bytes, out int width, out int height)
        {
            width = height = 0;
            if (bytes == null) return false;
            return TryPngSize(bytes, out width, out height) || TryJpegSize(bytes, out width, out height);
        }

        static bool TryPngSize(byte[] bytes, out int width, out int height)
        {
            width = height = 0;
            // PNG begins with an 8-byte signature and the mandatory IHDR chunk:
            // 4-byte length (13), 4-byte type, 13-byte data, 4-byte CRC = 33 bytes total.
            // IHDR data starts at byte 16 with two big-endian 32-bit dimensions.
            // https://www.w3.org/TR/PNG/#11IHDR
            if (bytes.Length >= 33 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 &&
                bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10 &&
                Big32(bytes, 8) == 13 && bytes[12] == 73 && bytes[13] == 72 && bytes[14] == 68 && bytes[15] == 82)
                return Accept(Big32(bytes, 16), Big32(bytes, 20), out width, out height);
            return false;
        }

        static bool TryJpegSize(byte[] bytes, out int width, out int height)
        {
            width = height = 0;
            // JPEG dimensions live in a Start Of Frame (SOF) segment, after optional
            // metadata/table segments. Walk their declared lengths without reading pixels.
            // ITU T.81, Annex B.1.1.3 (markers) and B.2.2 (frame header):
            // https://www.w3.org/Graphics/JPEG/itu-t81.pdf
            if (bytes.Length < 4 || bytes[0] != JpegMarkerPrefix || bytes[1] != JpegStartOfImage) return false;
            int at = 2;
            while (at < bytes.Length)
            {
                if (bytes[at++] != JpegMarkerPrefix) return false;
                // Repeated FF bytes are marker fill bytes, not a segment payload.
                while (at < bytes.Length && bytes[at] == JpegMarkerPrefix) at++;
                if (at >= bytes.Length) return false;
                int marker = bytes[at++];
                // Stop before compressed scan data; do not search it for apparent markers.
                if (marker == JpegEndOfImage || marker == JpegStartOfScan) return false;
                // TEM and restart markers stand alone and have no length field.
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;
                if (at + 2 > bytes.Length) return false;
                // Segment length includes its own two bytes, but excludes the FF marker.
                int length = Big16(bytes, at);
                if (length < 2 || length > bytes.Length - at) return false;
                if (IsStartOfFrame(marker))
                {
                    // SOF: length[2], precision[1], height[2], width[2], component count[1].
                    if (length < 8) return false;
                    return Accept(Big16(bytes, at + 5), Big16(bytes, at + 3), out width, out height);
                }
                at += length;
            }
            return false;
        }

        // C0..CF are frame markers except DHT (Huffman table), JPG (extension),
        // and DAC (arithmetic coding table). Baseline and progressive JPEG share this layout.
        static bool IsStartOfFrame(int marker) =>
            marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;

        static bool Accept(long w, long h, out int width, out int height)
        {
            width = height = 0;
            // Use wide arithmetic and a conservative RGBA allocation estimate even for RGB images.
            if (w <= 0 || h <= 0 || w > MaxDimension || h > MaxDimension ||
                w * h * RgbaBytesPerPixel > MarkdownImageBudget.ImageBytes)
                return false;
            width = (int)w;
            height = (int)h;
            return true;
        }

        static int Big16(byte[] b, int at) => (b[at] << 8) | b[at + 1];
        static long Big32(byte[] b, int at) => ((long)b[at] << 24) | ((long)b[at + 1] << 16)
            | ((long)b[at + 2] << 8) | b[at + 3];
    }
}
