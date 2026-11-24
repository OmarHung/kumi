import type { IconName } from '../components/Icon/Icon';

const KILOBYTE = 1024;
const MEGABYTE = KILOBYTE * 1024;
/** 到這麼大為止 KB 顯示一位小數：1.5 KB 與 2 KB 分得開，幾百 KB 的整數就夠。 */
const KB_DECIMALS_BELOW = 10 * KILOBYTE;

/** 檔案大小給人看：B、KB、MB（1024 進位，和伺服器的 25 MiB 上限一致）。 */
export function formatAttachmentSize(bytes: number): string {
  if (bytes >= MEGABYTE) return `${(bytes / MEGABYTE).toFixed(1)} MB`;
  if (bytes >= KB_DECIMALS_BELOW) return `${String(Math.round(bytes / KILOBYTE))} KB`;
  if (bytes >= KILOBYTE) return `${(bytes / KILOBYTE).toFixed(1)} KB`;
  return `${String(bytes)} B`;
}

/** 伺服器在瀏覽器裡直接顯示的圖片類型；其他一律下載（api/attachments.ts 的 attachmentContentUrl）。 */
const INLINE_IMAGE_TYPES: ReadonlySet<string> = new Set(['image/png', 'image/jpeg', 'image/gif', 'image/webp']);

export function isInlineImage(contentType: string): boolean {
  return INLINE_IMAGE_TYPES.has(contentType.toLowerCase());
}

const ARCHIVE_TYPES: ReadonlySet<string> = new Set([
  'application/zip',
  'application/x-zip-compressed',
  'application/gzip',
  'application/x-gzip',
  'application/x-tar',
  'application/x-7z-compressed',
  'application/x-rar-compressed',
  'application/vnd.rar',
  'application/x-bzip2',
]);

const SPREADSHEET_EXTENSIONS: ReadonlySet<string> = new Set(['xls', 'xlsx', 'xlsm', 'ods', 'csv', 'tsv']);
const DOCUMENT_EXTENSIONS: ReadonlySet<string> = new Set(['doc', 'docx', 'odt', 'rtf', 'txt', 'md', 'ppt', 'pptx', 'odp', 'pages', 'key']);
const ARCHIVE_EXTENSIONS: ReadonlySet<string> = new Set(['zip', 'gz', 'tgz', 'tar', '7z', 'rar', 'bz2']);

export type AttachmentKind = 'image' | 'pdf' | 'document' | 'spreadsheet' | 'archive' | 'other';

function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  return dot <= 0 ? '' : fileName.slice(dot + 1).toLowerCase();
}

function isSpreadsheetType(type: string): boolean {
  return type.includes('spreadsheet') || type === 'application/vnd.ms-excel' || type === 'text/csv' || type === 'text/tab-separated-values';
}

function isDocumentType(type: string): boolean {
  return type.includes('wordprocessing') || type.includes('presentation') || type === 'application/msword' || type === 'application/rtf' || type.startsWith('text/');
}

/** 檔案是哪一類（挑圖示用）：先看伺服器整理過的類型，類型是 octet-stream 或看不出來時再看副檔名。 */
export function attachmentKind(contentType: string, fileName: string): AttachmentKind {
  const type = contentType.toLowerCase();
  const extension = extensionOf(fileName);
  if (type.startsWith('image/')) return 'image';
  if (type === 'application/pdf' || extension === 'pdf') return 'pdf';
  if (ARCHIVE_TYPES.has(type) || ARCHIVE_EXTENSIONS.has(extension)) return 'archive';
  if (isSpreadsheetType(type) || SPREADSHEET_EXTENSIONS.has(extension)) return 'spreadsheet';
  if (isDocumentType(type) || DOCUMENT_EXTENSIONS.has(extension)) return 'document';
  return 'other';
}

const KIND_ICONS: Readonly<Record<AttachmentKind, IconName>> = {
  image: 'fileImage',
  pdf: 'filePdf',
  document: 'fileDocument',
  spreadsheet: 'fileSpreadsheet',
  archive: 'fileArchive',
  other: 'fileOther',
};

export function attachmentIcon(contentType: string, fileName: string): IconName {
  return KIND_ICONS[attachmentKind(contentType, fileName)];
}
