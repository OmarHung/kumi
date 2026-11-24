import type { UploadedFile } from './useUploadQueue';

/** 伺服器會在瀏覽器裡直接顯示的圖片類型（api/attachments.ts 的 attachmentContentUrl）：只有這幾種插成圖片，其他是檔案連結。 */
const INLINE_IMAGE_TYPES: ReadonlySet<string> = new Set(['image/png', 'image/jpeg', 'image/gif', 'image/webp']);

export function isInlineImageType(contentType: string): boolean {
  return INLINE_IMAGE_TYPES.has(contentType.toLowerCase());
}

/**
 * 檔名裡會弄壞 Markdown 的字：中括號、反斜線加反斜線跳脫，換行換成空白。
 * 星號與底線也要跳脫：不然 `a*b*c.pdf` 會被讀成斜體，傳完之後編輯器裡的名稱變成 `abc.pdf`。
 * 反引號與 < 也要跳脫：不然檔名裡成對的反引號會把後面的參照吞進 code span，< 開頭的會被當成自動連結。
 */
export function escapeFileName(name: string): string {
  return name.replace(/\r\n|[\r\n]/g, ' ').replace(/[[\]\\`<*_]/g, (char) => `\\${char}`);
}

/** 上傳中先放在游標位置的記號；沒有網址，所以不會被當成真的圖片或連結。serial 是同名檔案的序號（2 起算）。 */
export function placeholderOf(file: File, serial = 1): string {
  const label = `上傳中：${escapeFileName(labelOf(file, serial))}…`;
  return isInlineImageType(file.type) ? `![${label}]()` : `[${label}]()`;
}

function labelOf(file: File, serial: number): string {
  return serial === 1 ? file.name : `${file.name}（${String(serial)}）`;
}

export interface NextPlaceholder {
  readonly placeholder: string;
  /** 給人看的名稱（失敗與完成的說明用）：同名的第二個起帶序號，和記號一致。 */
  readonly label: string;
}

/** 還沒有人用的記號：兩個同名的檔案（連貼兩張 image.png）記號要分得開，完成時才換得到各自的那一個。 */
export function nextPlaceholder(file: File, isTaken: (placeholder: string) => boolean): NextPlaceholder {
  for (let serial = 1; ; serial += 1) {
    const placeholder = placeholderOf(file, serial);
    if (!isTaken(placeholder)) return { placeholder, label: labelOf(file, serial) };
  }
}

/** attachment: 後面接的編號（GUID）有幾個字：預留長度時用。 */
const GUID_LENGTH = 36;

/** 傳完之後參照會比記號長多少（記號沒有編號，參照有）：長度檢查要預留，不然送出才 422。 */
export function growthOf(file: File, placeholder: string): number {
  const link = `[${escapeFileName(file.name)}](attachment:${'0'.repeat(GUID_LENGTH)})`;
  const reference = isInlineImageType(file.type) ? `!${link}` : link;
  return Math.max(0, reference.length - placeholder.length);
}

/** 草稿裡的「上傳中」記號（含序號）；後面緊接的一個換行一起拿掉。 */
const STALE_PLACEHOLDER = /!?\[上傳中：[^\n]*?…\]\(\)\n?/g;

/**
 * 還原的草稿裡殘留的「上傳中」記號：上傳在換頁時已經中止，留著送出去就是壞的參照。整個拿掉，回報拿掉了幾個。
 */
export function stripPlaceholders(value: string): { readonly value: string; readonly removed: number } {
  let removed = 0;
  const next = value.replace(STALE_PLACEHOLDER, () => {
    removed += 1;
    return '';
  });
  return { value: next, removed };
}

/** 剪貼簿給的名稱（Chrome 的截圖叫 image.png）沒有意義，不拿來當附件的名稱。 */
const GENERIC_IMAGE_NAME = /^image(\.[a-z0-9]+)?$/i;

function twoDigits(value: number): string {
  return String(value).padStart(2, '0');
}

/**
 * 貼上的檔案要用的名稱：沒有意義的圖片名稱換成「截圖 年-月-日 時.分.副檔名」（用使用者的時區）；同一分鐘多張加（2）、（3）。
 * taken 是已經用掉的名稱。其他的名稱照舊。
 */
export function pastedFileName(file: File, now: Date, taken: ReadonlySet<string>): string {
  if (!file.type.startsWith('image/') || !GENERIC_IMAGE_NAME.test(file.name)) return file.name;
  const extension = /\.[a-z0-9]+$/i.exec(file.name)?.[0] ?? `.${file.type.slice('image/'.length).replace('jpeg', 'jpg')}`;
  const date = `${String(now.getFullYear())}-${twoDigits(now.getMonth() + 1)}-${twoDigits(now.getDate())}`;
  const base = `截圖 ${date} ${twoDigits(now.getHours())}.${twoDigits(now.getMinutes())}`;
  for (let serial = 1; ; serial += 1) {
    const name = serial === 1 ? `${base}${extension}` : `${base} (${String(serial)})${extension}`;
    if (!taken.has(name)) return name;
  }
}

/** 傳完之後的參照：圖片是 ![檔名](attachment:編號)，其他是 [檔名](attachment:編號)。 */
export function referenceOf(attachment: UploadedFile): string {
  const link = `[${escapeFileName(attachment.fileName)}](attachment:${attachment.id})`;
  return isInlineImageType(attachment.contentType) ? `!${link}` : link;
}

export interface CaretReplacement {
  readonly value: string;
  readonly start: number;
  readonly end: number;
}

function shifted(position: number, from: number, to: number, replacementLength: number): number {
  if (position <= from) return position;
  // 游標在記號裡面：放到換上去的字後面。在記號後面的依長度差平移。
  return position >= to ? position + replacementLength - (to - from) : from + replacementLength;
}

/** 把記號換成別的字，游標（或選取範圍）跟著平移；找不到記號是 null。 */
export function replaceWithCaret(value: string, placeholder: string, replacement: string, start: number, end: number): CaretReplacement | null {
  const from = value.indexOf(placeholder);
  if (from === -1) return null;
  const to = from + placeholder.length;
  return {
    value: `${value.slice(0, from)}${replacement}${value.slice(to)}`,
    start: shifted(start, from, to, replacement.length),
    end: shifted(end, from, to, replacement.length),
  };
}

export interface Insertion {
  readonly value: string;
  /** 插入的字後面的位置。 */
  readonly caret: number;
}

/** 在選取的範圍（沒有選取就是游標）換成要插入的字。 */
export function insertText(value: string, start: number, end: number, text: string): Insertion {
  const from = Math.min(start, end);
  const to = Math.max(start, end);
  return { value: `${value.slice(0, from)}${text}${value.slice(to)}`, caret: from + text.length };
}
