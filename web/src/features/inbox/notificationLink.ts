import type { To } from 'react-router';
import type { Notification } from '../../api/types';
import { commentHash } from '../item-page/commentTarget';
import { itemPagePath } from '../work-items/itemLinks';

/** 由留言產生的通知連到那一則留言：開新分頁、複製連結、窄螢幕進項目頁都帶著它，時間軸才捲得過去。收件匣與新通知的提示共用。 */
export function notificationLink({ projectKey, itemKey, commentId }: Notification): To {
  const pathname = itemPagePath(projectKey, itemKey);
  return commentId === null ? pathname : { pathname, hash: commentHash(commentId) };
}
