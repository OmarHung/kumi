import { useMemo, useState } from 'react';
import { Link } from 'react-router';
import type { useNotifications } from '../../api/notificationQueries';
import type { Notification } from '../../api/types';
import { Button, buttonClassName } from '../../components/Button/Button';
import { List } from '../../components/List/List';
import { EmptyState, ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { useItemPanel, useItemSequence } from '../item-page/ItemPanelContext';
import { currentNotificationId } from './currentNotification';
import { inboxFilterSearch, type InboxFilter } from './inboxFilter';
import { NotificationRow } from './NotificationRow';
import styles from './Inbox.module.css';

type NotificationsResult = ReturnType<typeof useNotifications>;

const SKELETON_ROWS = 6;

function EmptyInbox({ filter }: { readonly filter: InboxFilter }) {
  if (filter === 'all') {
    return <EmptyState title="還沒有通知">被指派、被提及，或你關注的項目有留言、改了狀態時，會出現在這裡。</EmptyState>;
  }
  return (
    <EmptyState
      title="沒有未讀的通知"
      action={
        <Link to={{ search: inboxFilterSearch(new URLSearchParams(), 'all') }} className={buttonClassName('secondary')}>
          看全部通知
        </Link>
      }
    >
      被指派、被提及，或你關注的項目有留言、改了狀態時，會出現在這裡。
    </EmptyState>
  );
}

/** 「載入更多」：載入中用 aria-disabled 而不是 disabled，有焦點的按鈕一停用焦點就掉回頁面本體。 */
function Footer({ result, count }: { readonly result: NotificationsResult; readonly count: number }) {
  if (!result.hasNextPage && !result.isFetchNextPageError) return null;
  const isFetching = result.isFetchingNextPage;
  return (
    <div className={styles.footer}>
      <span className={styles.footerCount}>已載入 {count} 則</span>
      {result.hasNextPage && (
        <Button
          size="sm"
          aria-disabled={isFetching || undefined}
          onClick={() => {
            if (!isFetching) void result.fetchNextPage();
          }}
        >
          {isFetching ? '載入中…' : '載入更多'}
        </Button>
      )}
      {result.isFetchNextPageError && <span role="alert">載入更多失敗，請再試一次。</span>}
    </div>
  );
}

interface InboxListProps {
  readonly result: NotificationsResult;
  readonly filter: InboxFilter;
  readonly onOpen: (notification: Notification) => void;
}

/** 收件匣的內容：載入中、讀不到、空的、清單與「載入更多」。 */
export function InboxList({ result, filter, onOpen }: InboxListProps) {
  const notifications = useMemo(() => result.data?.pages.flatMap((page) => page.items) ?? [], [result.data]);
  // 清單的順序登記給面板（上一個、下一個走這份）：同一個項目有好幾則通知時只算一次。
  useItemSequence([...new Set(notifications.flatMap((entry) => (entry.itemKey === null ? [] : [entry.itemKey])))]);
  // 面板開的是項目、不是通知：記住點的是哪一則，同一個項目的好幾則只標那一則。
  const [clickedId, setClickedId] = useState<string | null>(null);
  const panel = useItemPanel();
  const currentId = currentNotificationId(notifications, panel?.isEnabled === true ? panel.openKey : null, clickedId);
  if (result.isPending) return <Skeleton label="正在載入收件匣…" rows={SKELETON_ROWS} />;
  if (result.isLoadingError) {
    return (
      <ErrorState
        error={result.error}
        title="收件匣載入失敗"
        onRetry={() => {
          void result.refetch();
        }}
      />
    );
  }
  return (
    <>
      <RefetchErrorNotice query={result} message="無法更新收件匣，清單可能是舊的。" />
      {notifications.length === 0 ? (
        <EmptyInbox filter={filter} />
      ) : (
        <List label="通知" layout="page" footer={<Footer result={result} count={notifications.length} />}>
          {notifications.map((entry, index) => (
            <NotificationRow
              key={entry.notificationId}
              notification={entry}
              now={result.dataUpdatedAt}
              isFirst={index === 0}
              isCurrent={entry.notificationId === currentId}
              onOpen={onOpen}
              onSelect={(selected) => {
                setClickedId(selected.notificationId);
              }}
            />
          ))}
        </List>
      )}
    </>
  );
}
