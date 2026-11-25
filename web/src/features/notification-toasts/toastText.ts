import type { Notification } from '../../api/types';
import { ACCOUNT_NOTICE_LABEL } from '../inbox/accountNoticeText';
import { notificationHeadline, plainExcerpt } from '../inbox/notificationText';
import type { ToastMode } from './toastMode';

/** 同時在畫面上的提示最多幾個（含「還有 N 則」那一個）。 */
export const MAX_TOASTS = 3;

export interface ToastLayout {
  /** 照順序一則一則畫出來的（舊的在上、新的在下）。 */
  readonly visible: readonly Notification[];
  /** 放不下、併成「還有 N 則新通知」的（比 visible 舊）。 */
  readonly folded: readonly Notification[];
}

/** 不超過上限就全部畫出來；超過就留最新的 MAX_TOASTS - 1 則，其餘的併成一則。 */
export function layoutToasts(entries: readonly Notification[]): ToastLayout {
  if (entries.length <= MAX_TOASTS) return { visible: entries, folded: [] };
  const keep = MAX_TOASTS - 1;
  return { visible: entries.slice(-keep), folded: entries.slice(0, -keep) };
}

/** 第一行：「王小明 提及了你」；帳號通知是它自己的一句話（做事的就是你）。 */
export function toastHeadline(notification: Notification): string {
  return notificationHeadline(notification);
}

/** 第二行：項目的編號與標題；帳號通知是「帳號安全」。報讀也用這一行。 */
export function toastSubject(notification: Notification): string {
  return notification.kind === 'account_security' ? ACCOUNT_NOTICE_LABEL : `${notification.itemKey} ${notification.itemTitle}`;
}

/** 完整時第三段的內容（留言或提及的摘錄，和收件匣同一種處理）；精簡、沒有內容時是 null。 */
export function toastExcerpt(notification: Notification, mode: ToastMode): string | null {
  return mode === 'full' ? plainExcerpt(notification.excerpt) : null;
}

/** 報讀軟體唸的一句：「新通知：王小明 提及了你，MNT-12 修正登入頁」；完整時接著唸內容，精簡不唸。 */
export function toastAnnouncement(notification: Notification, mode: ToastMode): string {
  const excerpt = toastExcerpt(notification, mode);
  const base = `新通知：${toastHeadline(notification)}，${toastSubject(notification)}`;
  return excerpt === null ? base : `${base}：${excerpt}`;
}
