import type { MouseEvent, ReactNode } from 'react';
import { Link, type To } from 'react-router';
import styles from './CompactNotificationRow.module.css';

interface CompactNotificationRowProps {
  readonly to: To;
  readonly linkState?: unknown;
  readonly isContentStart: boolean;
  readonly isUnread: boolean;
  readonly onLinkClick?: (event: MouseEvent<HTMLAnchorElement>) => void;
  readonly onLinkAuxClick?: (event: MouseEvent<HTMLAnchorElement>) => void;
  /** 最前面：頭像（或帳號通知的圖示）。未讀的小圓點由這裡加。 */
  readonly leading: ReactNode;
  /** 第一行：誰做了什麼（一行，放不下用省略號）。 */
  readonly what: ReactNode;
  readonly time: ReactNode;
  /** 項目的標題（帳號通知是那一句話）：最多兩行。 */
  readonly title: string;
  readonly excerpt?: string | null;
  /** 最後一行：項目編號（等寬字）。 */
  readonly where?: string;
}

/**
 * 窄螢幕的收件匣一則（docs/DESIGN.md 8.3「收件匣」，2026-11-27 使用者看過手機版設計稿後採用）：
 * 頭像（未讀時左邊留白裡一個小圓點）、第一行「誰做了什麼」加時間、標題與摘要各最多兩行、最後是項目編號。
 * 寬螢幕的一行式排法會把標題截成一行，手機上看不出是哪一件事，所以窄螢幕不沿用。
 * 未讀：小圓點只是裝飾，報讀軟體靠名稱最前面看不見的「未讀：」（和寬螢幕同一個做法，不只靠顏色）。
 */
export function CompactNotificationRow(props: CompactNotificationRowProps) {
  const { to, linkState, isContentStart, isUnread, onLinkClick, onLinkAuxClick, leading, what, time, title, excerpt, where } = props;
  return (
    <li className={styles.row} data-content-start={isContentStart || undefined}>
      <Link to={to} state={linkState} className={styles.link} onClick={onLinkClick} onAuxClick={onLinkAuxClick}>
        {isUnread && <span className={styles.dot} aria-hidden="true" />}
        <span className={styles.leading}>{leading}</span>
        <span className={styles.body}>
          <span className={styles.head}>
            {isUnread && <span className="visually-hidden">未讀：</span>}
            <span className={styles.what}>{what}</span> <span className={styles.time}>{time}</span>
          </span>{' '}
          <span className={styles.title} data-unread={isUnread} title={title}>
            {title}
          </span>
          {excerpt !== null && excerpt !== undefined && (
            <>
              {' '}
              <span className={styles.excerpt}>{excerpt}</span>
            </>
          )}
          {where !== undefined && (
            <>
              {' '}
              <span className={styles.where}>{where}</span>
            </>
          )}
        </span>
      </Link>
    </li>
  );
}
