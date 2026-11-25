import type { To } from 'react-router';
import type { Notification } from '../../api/types';
import { commentHash } from '../item-page/commentTarget';
import { itemPagePath } from '../work-items/itemLinks';
import { ACCOUNT_NOTICE_PATH } from './accountNoticeText';

/**
 * 由留言產生的通知連到那一則留言：開新分頁、複製連結、窄螢幕進項目頁都帶著它，時間軸才捲得過去。收件匣與新通知的提示共用。
 * 帳號通知沒有項目，連到「命令列與整合」。
 */
export function notificationLink(notification: Notification): To {
  if (notification.kind === 'account_security') return ACCOUNT_NOTICE_PATH;
  const { projectKey, itemKey, commentId } = notification;
  const pathname = itemPagePath(projectKey, itemKey);
  return commentId === null ? pathname : { pathname, hash: commentHash(commentId) };
}
