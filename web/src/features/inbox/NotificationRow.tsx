import type { MouseEvent } from 'react';
import type { To } from 'react-router';
import type { AccountSecurityNotification, ItemNotification, Notification } from '../../api/notificationTypes';
import { Avatar } from '../../components/Avatar/Avatar';
import { Icon } from '../../components/Icon/Icon';
import { ListRow } from '../../components/List/List';
import { formatInstant, formatRelativeTime } from '../../domain/time';
import { useIsCompact } from '../../lib/useMediaQuery';
import { useItemPanelLink } from '../item-page/ItemPanelContext';
import { useItemPageEntry } from '../work-items/itemLinks';
import { CompactNotificationRow } from './CompactNotificationRow';
import { ACCOUNT_NOTICE_LABEL } from './accountNoticeText';
import { notificationLink } from './notificationLink';
import { notificationExcerpt, notificationSentence } from './notificationText';
import styles from './Inbox.module.css';

const MIDDLE_BUTTON = 1;

/** 通知的時間：相對的說法，滑過有完整的時間（寬螢幕在列的最右邊、窄螢幕在第一行的最右邊）。 */
function RelativeTime({ notification, now }: { readonly notification: Notification; readonly now: number }) {
  return (
    <time className={styles.time} dateTime={notification.createdAt} title={formatInstant(notification.createdAt)}>
      {formatRelativeTime(notification.createdAt, now)}
    </time>
  );
}

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

/** 一則連結的共同部分：連到哪裡、點擊與中鍵（寬、窄兩種排法共用）。 */
interface RowLinkProps {
  readonly to: To;
  readonly linkState?: unknown;
  readonly isContentStart: boolean;
  readonly onLinkClick: (event: MouseEvent<HTMLAnchorElement>) => void;
  readonly onLinkAuxClick: (event: MouseEvent<HTMLAnchorElement>) => void;
}

interface ItemRowViewProps {
  readonly notification: ItemNotification;
  readonly now: number;
  readonly link: RowLinkProps;
  readonly isCurrent: boolean;
}

/** 項目通知的排法：窄螢幕照 CompactNotificationRow，寬螢幕是共用清單的一筆。 */
function ItemRowView({ notification, now, link, isCurrent }: ItemRowViewProps) {
  const isCompact = useIsCompact();
  const { actor, itemKey, itemTitle } = notification;
  const isUnread = notification.readAt === null;
  const sentence = notificationSentence(notification);
  const excerpt = notificationExcerpt(notification);
  const time = <RelativeTime notification={notification} now={now} />;
  const leading = <Avatar name={actor.displayName} decorative />;
  if (isCompact) {
    return (
      <CompactNotificationRow
        {...link}
        isUnread={isUnread}
        leading={leading}
        what={`${actor.displayName} ${sentence}`}
        time={time}
        title={itemTitle}
        excerpt={excerpt}
        where={itemKey}
      />
    );
  }
  return (
    <ListRow
      {...link}
      isCurrent={isCurrent}
      leading={leading}
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
      trailing={time}
    />
  );
}

interface ItemRowProps extends NotificationRowProps {
  readonly notification: ItemNotification;
}

/**
 * 收件匣的一則：[頭像] 名稱 一句話／編號 標題 摘要 [時間]。整則是連到項目頁的連結（名稱就是這一則所有的文字，
 * 前面多一個看不見的「未讀：」）；寬螢幕一般的點擊在旁邊開項目面板（和我的工作一樣），窄螢幕直接進項目頁，
 * 兩種都標成已讀。面板開著的那一則標 aria-current。
 */
function ItemNotificationRow({ notification, now, isFirst, isCurrent, onOpen, onSelect }: ItemRowProps) {
  const entry = useItemPageEntry();
  const panelLink = useItemPanelLink(notification.itemKey);
  const link: RowLinkProps = {
    to: notificationLink(notification),
    linkState: entry,
    isContentStart: isFirst,
    onLinkClick: (event) => {
      onOpen(notification);
      panelLink.onClick?.(event);
      // 攔下來、在面板裡開了（一般的左鍵點擊）才換選取；開新分頁時面板開著的還是原本那一則。
      if (event.defaultPrevented) onSelect(notification);
    },
    // 中鍵開新分頁：瀏覽器不送 click，只送 auxclick；看的是同一則，一樣標成已讀。
    onLinkAuxClick: (event) => {
      if (event.button === MIDDLE_BUTTON) onOpen(notification);
    },
  };
  return <ItemRowView notification={notification} now={now} link={link} isCurrent={isCurrent} />;
}

interface AccountRowProps {
  readonly notification: AccountSecurityNotification;
  readonly now: number;
  readonly isFirst: boolean;
  readonly onOpen: (notification: Notification) => void;
}

/**
 * 帳號通知的一則：[鎖的圖示] 一句話／帳號安全 [時間]。做事的是你自己，所以不放頭像與名字，改用同樣大小的中性圓底圖示；
 * 整則連到「命令列與整合」（撤銷在那裡），不開項目面板，點了標成已讀。
 */
function AccountNotificationRow({ notification, now, isFirst, onOpen }: AccountRowProps) {
  const isCompact = useIsCompact();
  const isUnread = notification.readAt === null;
  const sentence = notificationSentence(notification);
  const time = <RelativeTime notification={notification} now={now} />;
  const handleOpen = () => {
    onOpen(notification);
  };
  const link: RowLinkProps = {
    to: notificationLink(notification),
    isContentStart: isFirst,
    onLinkClick: handleOpen,
    onLinkAuxClick: (event) => {
      if (event.button === MIDDLE_BUTTON) handleOpen();
    },
  };
  const leading = (
    <span className={styles.accountIcon}>
      <Icon name="lock" />
    </span>
  );
  if (isCompact) {
    return <CompactNotificationRow {...link} isUnread={isUnread} leading={leading} what={ACCOUNT_NOTICE_LABEL} time={time} title={sentence} />;
  }
  return (
    <ListRow
      {...link}
      leading={leading}
      titleText={sentence}
      title={
        <span className={styles.text} data-unread={isUnread}>
          {isUnread && <span className="visually-hidden">未讀：</span>}
          {sentence}
        </span>
      }
      meta={[{ key: 'account', content: <span className={styles.clip}>{ACCOUNT_NOTICE_LABEL}</span> }]}
      trailing={time}
    />
  );
}

/** 收件匣的一則：帳號通知與項目的通知長得不一樣（沒有做事的人與項目）。 */
export function NotificationRow(props: NotificationRowProps) {
  const { notification } = props;
  return notification.kind === 'account_security' ? (
    <AccountNotificationRow notification={notification} now={props.now} isFirst={props.isFirst} onOpen={props.onOpen} />
  ) : (
    <ItemNotificationRow {...props} notification={notification} />
  );
}
