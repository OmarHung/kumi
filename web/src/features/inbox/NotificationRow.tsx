import type { MouseEvent } from 'react';
import type { Notification } from '../../api/types';
import { Avatar } from '../../components/Avatar/Avatar';
import { ListRow } from '../../components/List/List';
import { formatInstant, formatRelativeTime } from '../../domain/time';
import { useItemPanelLink } from '../item-page/ItemPanelContext';
import { useItemPageEntry } from '../work-items/itemLinks';
import { notificationLink } from './notificationLink';
import { notificationSentence, plainExcerpt } from './notificationText';
import styles from './Inbox.module.css';

const MIDDLE_BUTTON = 1;

interface NotificationRowProps {
  readonly notification: Notification;
  /** 算相對時間用的「現在」：整份清單用同一個值。 */
  readonly now: number;
  readonly isFirst: boolean;
  /** 旁邊的面板開著的是這一則（同一個項目有好幾則時只有一則是，見 currentNotification.ts）。 */
  readonly isCurrent: boolean;
  /** 點了這一則（任何一種點擊，包括 ⌘／Ctrl 點與中鍵開新分頁）：呼叫端把它標成已讀。 */
  readonly onOpen: (notification: Notification) => void;
  /** 一般的點擊在旁邊的面板開了這一則（⌘／Ctrl 點開新分頁、窄螢幕換頁都不算）。 */
  readonly onSelect: (notification: Notification) => void;
}

/**
 * 收件匣的一則：[頭像] 名稱 一句話／編號 標題 摘要 [時間]。整則是連到項目頁的連結（名稱就是這一則所有的文字，
 * 前面多一個看不見的「未讀：」）；寬螢幕一般的點擊在旁邊開項目面板（和我的工作一樣），窄螢幕直接進項目頁，
 * 兩種都標成已讀。面板開著的那一則標 aria-current。
 */
export function NotificationRow({ notification, now, isFirst, isCurrent, onOpen, onSelect }: NotificationRowProps) {
  const { actor, itemKey, itemTitle } = notification;
  const entry = useItemPageEntry();
  const panelLink = useItemPanelLink(itemKey);
  const isUnread = notification.readAt === null;
  const sentence = notificationSentence(notification);
  const excerpt = plainExcerpt(notification.excerpt);
  const handleClick = (event: MouseEvent<HTMLAnchorElement>) => {
    onOpen(notification);
    panelLink.onClick?.(event);
    // 攔下來、在面板裡開了（一般的左鍵點擊）才換選取；開新分頁時面板開著的還是原本那一則。
    if (event.defaultPrevented) onSelect(notification);
  };
  // 中鍵開新分頁：瀏覽器不送 click，只送 auxclick；看的是同一則，一樣標成已讀。
  const handleAuxClick = (event: MouseEvent<HTMLAnchorElement>) => {
    if (event.button === MIDDLE_BUTTON) onOpen(notification);
  };
  return (
    <ListRow
      to={notificationLink(notification)}
      linkState={entry}
      isContentStart={isFirst}
      isCurrent={isCurrent}
      onLinkClick={handleClick}
      onLinkAuxClick={handleAuxClick}
      leading={<Avatar name={actor.displayName} decorative />}
      titleText={`${actor.displayName} ${sentence}`}
      title={
        <span className={styles.text} data-unread={isUnread}>
          {isUnread && <span className="visually-hidden">未讀：</span>}
          <span className={styles.actor}>{actor.displayName}</span> {sentence}
        </span>
      }
      meta={[
        {
          key: 'item',
          content: (
            <span className={styles.clip}>
              <span className={styles.itemKey}>{itemKey}</span> {itemTitle}
            </span>
          ),
        },
        ...(excerpt === null ? [] : [{ key: 'excerpt', content: <span className={styles.excerpt}>{excerpt}</span> }]),
      ]}
      trailing={
        <time className={styles.time} dateTime={notification.createdAt} title={formatInstant(notification.createdAt)}>
          {formatRelativeTime(notification.createdAt, now)}
        </time>
      }
    />
  );
}
