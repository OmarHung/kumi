/*
 * 分享一個連結（docs/DESIGN.md 8.3「分享項目」）：觸控裝置用系統的分享（LINE、Email…），其他放進剪貼簿。
 * 剪貼簿能寫富文字時同時給 HTML 與純文字：貼進文件、聊天軟體、說明的編輯器是帶標題的連結（「MNT-3 備份排程檢查」），
 * 貼進純文字的地方是網址。剪貼簿 API 只在安全的來源（https、localhost）能用，正式與本機都是。
 */

export interface SharedLink {
  readonly url: string;
  /** 連結的文字（富文字的剪貼簿、系統分享的標題）。 */
  readonly title: string;
}

/** 複製好了、交給系統的分享了、在系統的分享裡取消了、失敗。 */
export type ShareOutcome = 'copied' | 'shared' | 'canceled' | 'failed';

function escapeHtml(text: string): string {
  return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
}

function canWriteRich(): boolean {
  return typeof ClipboardItem === 'function' && typeof navigator.clipboard.write === 'function';
}

/** 放進剪貼簿：能寫富文字就寫 HTML 加純文字，不行就只寫網址。 */
export async function copyLink({ url, title }: SharedLink): Promise<boolean> {
  try {
    if (canWriteRich()) {
      const html = `<a href="${escapeHtml(url)}">${escapeHtml(title)}</a>`;
      const item = new ClipboardItem({
        'text/plain': new Blob([url], { type: 'text/plain' }),
        'text/html': new Blob([html], { type: 'text/html' }),
      });
      await navigator.clipboard.write([item]);
    } else {
      await navigator.clipboard.writeText(url);
    }
    return true;
  } catch {
    return false;
  }
}

function canShare(link: SharedLink): boolean {
  if (typeof navigator.share !== 'function') return false;
  return typeof navigator.canShare !== 'function' || navigator.canShare({ title: link.title, url: link.url });
}

/**
 * 分享：preferSystem（觸控裝置）而且瀏覽器有系統的分享時打開它；其他放進剪貼簿。
 * 系統的分享被取消（AbortError）不算失敗；其他錯誤（例如沒有使用者的動作）退回放進剪貼簿。
 */
export async function shareLink(link: SharedLink, preferSystem: boolean): Promise<ShareOutcome> {
  if (preferSystem && canShare(link)) {
    try {
      await navigator.share({ title: link.title, url: link.url });
      return 'shared';
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') return 'canceled';
    }
  }
  return (await copyLink(link)) ? 'copied' : 'failed';
}
