/**
 * 把檔案的位元組讀成文字（PM-4b）：先用 UTF-8（失敗就是不合法的位元組），再退回 Big5。
 * 台灣的 Excel 另存「CSV」常常是 Big5（CP950）。UTF-8 的 BOM 由解碼器去掉。
 */
export type CsvEncoding = 'utf-8' | 'big5' | 'utf-16le' | 'utf-16be';

export interface DecodedCsv {
  readonly text: string;
  readonly encoding: CsvEncoding;
}

/** Excel 的「Unicode 文字」是 UTF-16（加 tab 分隔），開頭一定有 BOM；沒有 BOM 的 UTF-16 認不出來。 */
function utf16Encoding(bytes: Uint8Array): CsvEncoding | null {
  if (bytes[0] === 0xff && bytes[1] === 0xfe) return 'utf-16le';
  if (bytes[0] === 0xfe && bytes[1] === 0xff) return 'utf-16be';
  return null;
}

export function decodeCsvBytes(input: ArrayBuffer | Uint8Array): DecodedCsv {
  const bytes = input instanceof Uint8Array ? input : new Uint8Array(input);
  const utf16 = utf16Encoding(bytes);
  if (utf16 !== null) return { text: new TextDecoder(utf16).decode(bytes), encoding: utf16 };
  try {
    return { text: new TextDecoder('utf-8', { fatal: true }).decode(bytes), encoding: 'utf-8' };
  } catch {
    // 不是 UTF-8：改用 Big5。這裡不再用 fatal：少數字元（造字區）解不出來時顯示成 �，比整個檔案打不開好。
    return { text: new TextDecoder('big5').decode(bytes), encoding: 'big5' };
  }
}
