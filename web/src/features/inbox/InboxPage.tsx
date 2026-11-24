import { useEffect } from 'react';
import { useSearchParams } from 'react-router';
import { useMarkAllRead, useMarkRead, useNotifications, useUnreadCount } from '../../api/notificationQueries';
import type { Notification } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { NavTabs } from '../../components/NavTabs/NavTabs';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { Toolbar } from '../../components/Toolbar/Toolbar';
import { ItemPanelHost } from '../item-page/ItemPanelHost';
import { inboxFilterOf, inboxFilterSearch, type InboxFilter } from './inboxFilter';
import { InboxList } from './InboxList';
import styles from './Inbox.module.css';

const FILTERS: readonly { readonly filter: InboxFilter; readonly label: string }[] = [
  { filter: 'unread', label: '未讀' },
  { filter: 'all', label: '全部' },
];

/** 工具列右邊一直掛著的那一句：標成已讀的結果（成功與失敗）。 */
function statusMessage(markAll: ReturnType<typeof useMarkAllRead>, markOne: ReturnType<typeof useMarkRead>): string {
  if (markAll.isError) return '無法全部標為已讀，請再試一次。';
  if (markOne.isError) return '無法標成已讀，下次打開收件匣時還是未讀。';
  if (markAll.isSuccess) return `已將 ${String(markAll.data.updated)} 則通知標為已讀。`;
  return '';
}

/** 分頁：換分頁時其他的參數（開著的面板 item）留著。 */
function useInboxTabs(filter: InboxFilter) {
  const [searchParams] = useSearchParams();
  return FILTERS.map((entry) => ({
    key: entry.filter,
    label: entry.label,
    to: { search: inboxFilterSearch(searchParams, entry.filter) },
    isCurrent: entry.filter === filter,
  }));
}

/** 頁列的「全部標為已讀」：沒有未讀、或正在送時是 aria-disabled，不是 disabled（有焦點的按鈕一停用，焦點就掉回頁面本體）。 */
function MarkAllButton({ markAll }: { readonly markAll: ReturnType<typeof useMarkAllRead> }) {
  const unreadCount = useUnreadCount();
  const isIdle = unreadCount.data === 0 || markAll.isPending;
  return (
    <Button
      size="sm"
      aria-disabled={isIdle || undefined}
      onClick={() => {
        if (!isIdle) markAll.mutate();
      }}
    >
      全部標為已讀
    </Button>
  );
}

/**
 * 收件匣（docs/DESIGN.md 8.3「收件匣」）：我的通知，新的在前。工具列是「未讀」（預設）與「全部」兩個分頁（記在網址的 show），
 * 頁列有「全部標為已讀」。點一則在清單旁邊開那個項目的面板（和我的工作一樣；窄螢幕直接進項目頁），同時標成已讀：
 * 剛讀的留在原位（不再是未讀的樣子），重新整理或換分頁才從「未讀」裡消失，點的當下清單不會跳。
 */
export function InboxPage() {
  const [searchParams] = useSearchParams();
  const filter = inboxFilterOf(searchParams);
  const notifications = useNotifications(filter === 'unread');
  const markOne = useMarkRead();
  const markAll = useMarkAllRead();
  const tabs = useInboxTabs(filter);
  const { reset: resetMarkAll } = markAll;
  const { reset: resetMarkOne } = markOne;
  // 「已將 N 則通知標為已讀。」只說剛剛那一次：換分頁、下一次操作（點一則）就清掉，不要一直掛著。
  useEffect(() => {
    resetMarkAll();
    resetMarkOne();
  }, [filter, resetMarkAll, resetMarkOne]);
  const open = (notification: Notification) => {
    resetMarkAll();
    if (notification.readAt === null) markOne.mutate([notification.notificationId]);
  };
  const status = (
    <p role="status" className={styles.status}>
      {statusMessage(markAll, markOne)}
    </p>
  );
  return (
    <PageLayout
      variant="list"
      bar={<PageBar title="收件匣" actions={<MarkAllButton markAll={markAll} />} />}
      toolbar={
        <Toolbar end={status}>
          <NavTabs label="收件匣的分頁" tabs={tabs} />
        </Toolbar>
      }
    >
      {/* 點一則在右邊開項目面板（跨專案：面板用那個項目自己的專案內容）。 */}
      <ItemPanelHost layout="page">
        <InboxList result={notifications} filter={filter} onOpen={open} />
      </ItemPanelHost>
    </PageLayout>
  );
}
