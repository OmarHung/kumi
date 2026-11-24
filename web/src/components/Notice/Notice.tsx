import type { ReactNode, Ref } from 'react';
import { cx } from '../../lib/cx';
import { Button } from '../Button/Button';
import { Icon, type IconName } from '../Icon/Icon';
import styles from './Notice.module.css';

export type NoticeTone = 'info' | 'success' | 'warning' | 'danger';

const TONE_ICONS: Readonly<Record<NoticeTone, IconName>> = {
  info: 'info',
  success: 'success',
  warning: 'warning',
  danger: 'error',
};

export interface NoticeProps {
  readonly tone: NoticeTone;
  readonly title?: string;
  /** 空字串、null 時元素照樣掛著（看不見），之後才填內容的 live region 才唸得到。 */
  readonly children?: ReactNode;
  readonly actions?: ReactNode;
  /** 有值才有「知道了」。 */
  readonly onDismiss?: () => void;
  /** 「知道了」的名稱；同一頁有好幾個時帶上對象，例如「知道了（AGY-4）」。 */
  readonly dismissLabel?: string;
  /** 預設：danger 是 alert，其他是 status；none 是不會自己唸的說明（頁面一開始就在的提示）。 */
  readonly role?: 'alert' | 'status' | 'none';
  /** 給 aria-describedby 連過來用（例如表單上方的錯誤）。 */
  readonly id?: string;
  /** 只放版面（外距、寬度），不要改長相。 */
  readonly className?: string;
  /** 焦點的去處（按的按鈕不見之後、送出時才知道的問題）：和 tabIndex={-1} 一起給，程式把焦點放到提示本身。 */
  readonly ref?: Ref<HTMLDivElement>;
  /** 只用 -1（程式可以放焦點、Tab 不停在它上面）。 */
  readonly tabIndex?: -1;
}

function isEmptyContent(children: ReactNode): boolean {
  return children === undefined || children === null || children === false || children === '';
}

/**
 * 純文字直接放在提示本身裡，不再包一層：「含這段文字的元素」就是提示（有 role、旁邊就是動作），
 * 報讀軟體與既有的測試都靠這一點。其他內容（段落、清單）包在 .message 裡排在同一欄。
 */
function NoticeMessage({ children }: { readonly children: ReactNode }) {
  if (isEmptyContent(children)) return null;
  if (typeof children === 'string' || typeof children === 'number') return children;
  return <div className={styles.message}>{children}</div>;
}

/**
 * 頁面或區塊裡的提示：圖示加文字，不只靠顏色（docs/DESIGN.md 第 7 節）。整個提示是 live region
 * （錯誤是 alert，其他是 status），而且一直是同一個元素：內容晚一點才填進來時，報讀軟體才會唸。
 */
export function Notice({
  tone,
  title,
  children,
  actions,
  onDismiss,
  dismissLabel = '知道了',
  role,
  id,
  className,
  ref,
  tabIndex,
}: NoticeProps) {
  const liveRole = role ?? (tone === 'danger' ? 'alert' : 'status');
  const isEmpty = isEmptyContent(children) && title === undefined;
  return (
    <div
      ref={ref}
      id={id}
      tabIndex={tabIndex}
      role={liveRole === 'none' ? undefined : liveRole}
      data-tone={tone}
      className={isEmpty ? 'visually-hidden' : cx(styles.notice, className)}
    >
      {!isEmpty && <Icon name={TONE_ICONS[tone]} className={styles.icon} />}
      {title !== undefined && <p className={styles.title}>{title}</p>}
      <NoticeMessage>{children}</NoticeMessage>
      {actions !== undefined && <div className={styles.actions}>{actions}</div>}
      {onDismiss !== undefined && (
        <Button size="sm" variant="ghost" onClick={onDismiss} className={styles.dismiss}>
          {dismissLabel}
        </Button>
      )}
    </div>
  );
}
