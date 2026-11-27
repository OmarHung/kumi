import { useEffect } from 'react';
import { useSearchParams } from 'react-router';
import { useMarkAllRead, useMarkRead, useNotifications, useUnreadCount } from '../../api/notificationQueries';
import type { Notification } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { Toolbar } from '../../components/Toolbar/Toolbar';
import { ItemPanelHost } from '../item-page/ItemPanelHost';
import { inboxFilterOf } from './inboxFilter';
import { InboxList } from './InboxList';
import { InboxCategoryTabs, InboxFilterControls } from './InboxToolbar';
import styles from './Inbox.module.css';

/** 工具列右邊一直掛著的那一句：標成已讀的結果（成功與失敗）。 */
function statusMessage(markAll: ReturnType<typeof useMarkAllRead>, markOne: ReturnType<typeof useMarkRead>): string {
  if (markAll.isError) return '無法全部標為已讀，請再試一次。';
  if (markOne.isError) return '無法標成已讀，下次打開收件匣時還是未讀。';
  if (markAll.isSuccess) return `已將 ${String(markAll.data.updated)} 則通知標為已讀。`;
  return '';
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
 * 收件匣（docs/DESIGN.md 8.3「收件匣」）：我的通知，新的在前。工具列是通知的分類（全部、提及與回覆、指派給我；網址的 type）、搜尋框（q）與「只看未讀」切換（預設按下；放開是網址的 show=all），
 * 頁列有「全部標為已讀」（不受分類與搜尋影響）。點一則在清單旁邊開那個項目的面板（和我的工作一樣；窄螢幕直接進項目頁），同時標成已讀：
 * 剛讀的留在原位（不再是未讀的樣子），重新整理或換條件才從只看未讀的清單裡消失，點的當下清單不會跳。
 */
export function InboxPage() {
  const [searchParams] = useSearchParams();
  const filter = inboxFilterOf(searchParams);
  const notifications = useNotifications(filter.unreadOnly, filter.category === 'all' ? undefined : filter.category, filter.q);
  const markOne = useMarkRead();
  const markAll = useMarkAllRead();
  const { reset: resetMarkAll } = markAll;
  const { reset: resetMarkOne } = markOne;
  // 「已將 N 則通知標為已讀。」只說剛剛那一次：換分類、搜尋、只看未讀、下一次操作（點一則）就清掉，不要一直掛著。
  const { category, q, unreadOnly } = filter;
  useEffect(() => {
    resetMarkAll();
    resetMarkOne();
  }, [category, q, unreadOnly, resetMarkAll, resetMarkOne]);
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
          <InboxCategoryTabs filter={filter} />
          <InboxFilterControls filter={filter} />
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
