using System.Buffers.Binary;

namespace PmTool.Ai.Attachments;

/// <summary>
/// 只讀檔頭裡的寬與高（PNG、JPEG、GIF、WebP），不解碼圖片。讀不出來（截斷、不認得的子格式）回 false：
/// 上傳時讀不出尺寸的照收，真的壞掉的由供應商那邊拒絕。
/// </summary>
internal static class ImageDimensions
{
    public static bool TryRead(string contentType, ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        (width, height) = (0, 0);
        return contentType switch
        {
            AttachmentSniffer.PngType => TryReadPng(bytes, out width, out height),
            AttachmentSniffer.JpegType => TryReadJpeg(bytes, out width, out height),
            AttachmentSniffer.GifType => TryReadGif(bytes, out width, out height),
            AttachmentSniffer.WebpType => TryReadWebp(bytes, out width, out height),
            _ => false,
        };
    }

    // 簽章 8 位元組、IHDR 區塊的長度 4 與名稱 4，之後是寬與高（各 4 位元組，大端）。
    private static bool TryReadPng(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        (width, height) = (0, 0);
        if (bytes.Length < 24)
        {
            return false;
        }

        width = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), int.MaxValue);
        height = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]), int.MaxValue);
        return true;
    }

    // 簽章 6 位元組之後是寬與高（各 2 位元組，小端）。
    private static bool TryReadGif(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        (width, height) = (0, 0);
        if (bytes.Length < 10)
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]);
        return true;
    }

    // 從 SOI 之後一段一段跳，找到 SOF0–SOF15（不含 DHT、JPG、DAC）：長度 2、精度 1、高 2、寬 2（大端）。
    private static bool TryReadJpeg(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        (width, height) = (0, 0);
        var offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                return false;
            }

            var marker = bytes[offset + 1];
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD8))
            {
                offset += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 2)..]);
            if (IsStartOfFrame(marker))
            {
                if (offset + 9 > bytes.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 7)..]);
                return true;
            }

            if (marker == 0xDA || length < 2)
            {
                return false;
            }

            offset += 2 + length;
        }

        return false;
    }

    private static bool IsStartOfFrame(byte marker) => marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

    // RIFF 12 位元組之後是第一個區塊：「VP8 」（有損，14 位元的寬高）、「VP8L」（無損，各 14 位元減一）、「VP8X」（擴充，各 24 位元減一）。
    private static bool TryReadWebp(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        (width, height) = (0, 0);
        if (bytes.Length < 16)
        {
            return false;
        }

        var chunk = bytes.Slice(12, 4);
        if (chunk.SequenceEqual("VP8 "u8) && bytes.Length >= 30)
        {
            // 區塊資料前 3 位元組是畫格標籤，接著起始碼 9D 01 2A，再來是寬與高（各 2 位元組，小端，低 14 位元）。
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFF;
            return true;
        }

        if (chunk.SequenceEqual("VP8L"u8) && bytes.Length >= 25)
        {
            // 區塊資料第 1 位元組是簽章 0x2F，之後 4 位元組裡依序是寬減一（14 位元）、高減一（14 位元）。
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            return true;
        }

        if (chunk.SequenceEqual("VP8X"u8) && bytes.Length >= 30)
        {
            width = (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)) + 1;
            height = (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16)) + 1;
            return true;
        }

        return false;
    }
}
