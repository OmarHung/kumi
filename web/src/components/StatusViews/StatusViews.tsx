import type { ReactNode, Ref } from 'react';
import { errorMessage } from '../../api/http';
import { Button } from '../Button/Button';
import { Notice } from '../Notice/Notice';
import styles from './StatusViews.module.css';

interface LoadingStateProps {
  readonly label?: string;
}

export function LoadingState({ label = '載入中…' }: LoadingStateProps) {
  return (
    <div role="status" className={styles.loading}>
      <span className={styles.spinner} aria-hidden="true" />
      {label}
    </div>
  );
}

interface ErrorStateProps {
  readonly error: unknown;
  readonly title?: string;
  readonly onRetry?: () => void;
}

export function ErrorState({ error, title = '載入失敗', onRetry }: ErrorStateProps) {
  const retry = onRetry && (
    <Button size="sm" onClick={onRetry}>
      重試
    </Button>
  );
  return (
    <Notice tone="danger" role="alert" title={title} actions={retry}>
      {errorMessage(error)}
    </Notice>
  );
}

/** 重抓得到的資料：只用到這幾個，useQuery 與 useInfiniteQuery 的結果都可以直接給。 */
interface RefetchableQuery {
  readonly isRefetchError: boolean;
  readonly refetch: () => Promise<unknown>;
}

interface RefetchErrorNoticeProps {
  readonly query: RefetchableQuery;
  /** 說明哪些資料可能是舊的，例如「無法更新這個項目的最新資料，畫面上可能是舊的。」 */
  readonly message: string;
}

/**
 * 已經有資料、背景重抓卻失敗時的一行提示（長相是共用的 Notice）。畫面與編輯中的內容照常留著、可以繼續操作，
 * 只說明看到的可能是舊的，可以重試。第一次就讀不到（沒有資料）時才用 ErrorState 換掉整個畫面。
 */
export function RefetchErrorNotice({ query, message }: RefetchErrorNoticeProps) {
  if (!query.isRefetchError) {
    return null;
  }
  const retry = (
    <Button
      size="sm"
      onClick={() => {
        void query.refetch();
      }}
    >
      重試
    </Button>
  );
  return (
    <Notice tone="warning" role="status" actions={retry} className={styles.pageNotice}>
      {message}
    </Notice>
  );
}

interface EmptyStateProps {
  readonly title: string;
  readonly children?: ReactNode;
  readonly action?: ReactNode;
}

export function EmptyState({ title, children, action }: EmptyStateProps) {
  return (
    <div className={styles.empty}>
      <p className={styles.title}>{title}</p>
      {children && <div className={styles.body}>{children}</div>}
      {action && <div className={styles.action}>{action}</div>}
    </div>
  );
}

interface ArrivalNoticeProps {
  readonly message: string;
  /** 提示旁邊的動作（例如「復原」）：元件不知道是什麼，由頁面（功能層）給。 */
  readonly actions?: ReactNode;
  /** 預設是 success；動作失敗時頁面換成 danger（錯誤是 alert）。 */
  readonly tone?: 'success' | 'danger';
  /** 提示下面多一行說明（動作失敗的原因，alert）：提示與動作都留著，可以再試一次。 */
  readonly detail?: string;
  /** 焦點的去處（動作做完、按的按鈕不見之後）：給了就能用程式把焦點放到提示本身。 */
  readonly noticeRef?: Ref<HTMLDivElement>;
}

/**
 * 做完一件事換到這一頁時的提示（例如「已刪除 AGY-7「…」」）。一直掛著：沒有提示時是空的、看不見，
 * 提示晚一點才填進來（lib/useArrivalNotice.ts），螢幕報讀器才唸得到。
 * 可以復原的事（刪除項目）由頁面用 features/bulk-edit 的 UndoableArrival 給 actions 與結果。
 */
export function ArrivalNotice({ message, actions, tone = 'success', detail, noticeRef }: ArrivalNoticeProps) {
  return (
    <Notice
      tone={tone}
      role={tone === 'danger' ? 'alert' : 'status'}
      actions={actions}
      className={styles.pageNotice}
      ref={noticeRef}
      tabIndex={noticeRef === undefined ? undefined : -1}
    >
      {detail === undefined ? (
        message
      ) : (
        <>
          {message}
          <p role="alert">{detail}</p>
        </>
      )}
    </Notice>
  );
}
