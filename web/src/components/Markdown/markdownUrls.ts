/**
 * 連結只認這幾種協定（docs/DESIGN.md 7.4「Markdown」）：http、https、mailto 是真的連結，mention 是提及，
 * attachment: 加附件的 GUID 是這個項目自己的附件（圖片與檔案連結，不認其他寫法）。
 * 白名單而不是黑名單：javascript:、data:、vbscript:、file:、相對路徑、協定相對的網址（//…）都不認，
 * 瀏覽器會忽略的怪寫法（java&#x09;script:）也不會漏。
 */
const ALLOWED_URL = /^(https?:|mailto:|mention:)/i;
const EXTERNAL_URL = /^(https?:|mailto:)/i;
export const MENTION_SCHEME = 'mention:';
export const ATTACHMENT_SCHEME = 'attachment:';
const ATTACHMENT_URL = /^attachment:([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i;

export function isAllowedUrl(url: string): boolean {
  return ALLOWED_URL.test(url) || ATTACHMENT_URL.test(url);
}

/** attachment:<GUID> 的 GUID（小寫）；不是這個寫法（包括後面接的不是 GUID）是 null。 */
export function attachmentIdOf(url: string): string | null {
  const match = ATTACHMENT_URL.exec(url);
  return match?.[1] === undefined ? null : match[1].toLowerCase();
}

/** 真的連結（不是提及）：在新分頁開。 */
export function isExternalUrl(url: string): boolean {
  return EXTERNAL_URL.test(url);
}

export function isMentionUrl(url: string): boolean {
  return url.toLowerCase().startsWith(MENTION_SCHEME);
}

/** react-markdown 的 urlTransform：不認的協定回 null，網址整個拿掉，連結就只剩文字。 */
export function markdownUrlTransform(url: string): string | null {
  return isAllowedUrl(url) ? url : null;
}

/** mention:編號 的編號。 */
export function mentionIdOf(url: string): string {
  const id = url.slice(MENTION_SCHEME.length);
  try {
    return decodeURIComponent(id);
  } catch {
    return id;
  }
}
