import type { Notification } from '../../api/types';

/**
 * 面板開著的項目，在收件匣裡標哪一則（aria-current 與選取的底色）。同一個項目常有好幾則通知（留言、提及、改狀態），
 * 只標一則：點的那一則還是面板開著的項目時就是它；不然（網址直接帶著 item、在面板裡換到上一個或下一個）是那個項目的第一則。
 * 面板沒開、或清單上沒有那個項目時，沒有選取的。
 */
export function currentNotificationId(
  notifications: readonly Notification[],
  openKey: string | null,
  clickedId: string | null,
): string | null {
  if (openKey === null) return null;
  const clicked = notifications.find((entry) => entry.notificationId === clickedId);
  if (clicked?.itemKey === openKey) return clicked.notificationId;
  return notifications.find((entry) => entry.itemKey === openKey)?.notificationId ?? null;
}
