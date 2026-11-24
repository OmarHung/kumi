import { useRef, useState, type ReactNode } from 'react';
import type { ProjectDetail } from '../../api/types';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { ArchivedNotice } from '../projects/ArchivedNotice';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { DeletedItemsBody } from './DeletedItemsBody';
import { RestoreNotices } from './RestoreNotices';
import { flattenDeletedItems, useDeletedItemPages } from './useDeletedItemPages';
import { useRestoreDeletedItem } from './useRestoreDeletedItem';
import { useRestoreFocus } from './useRestoreFocus';
import styles from './DeletedItems.module.css';

interface DeletedItemsContentProps {
  readonly detail: ProjectDetail;
  /** 專案資料重抓失敗的提示（頁面外殼給的）。 */
  readonly notice: ReactNode;
}

/** 已刪除的項目：頁列、說明、提示、表格（窄螢幕是清單）與「載入更多」。 */
export function DeletedItemsContent({ detail, notice }: DeletedItemsContentProps) {
  const projectKey = detail.project.key;
  const result = useDeletedItemPages(projectKey);
  const items = flattenDeletedItems(result.data);
  // 整份清單用同一個「現在」算相對時間；頁面開著期間不必每分鐘重算。
  const [now] = useState(() => Date.now());
  const headingRef = useRef<HTMLHeadingElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const focus = useRestoreFocus(listRef, headingRef, result.data);
  const restoring = useRestoreDeletedItem({ projectKey, onRemoved: focus.planFocus });
  const crumbs = [{ label: projectKey, mono: true, to: useProjectPageLink(projectKey) }];
  return (
    <PageLayout variant="list" bar={<PageBar title="已刪除的項目" crumbs={crumbs} headingRef={headingRef} />}>
      {notice}
      <ArchivedNotice detail={detail} />
      <div className={styles.page}>
        <RestoreNotices
          projectKey={projectKey}
          notice={restoring.notice}
          isParentListed={(parentKey) => items.some((item) => item.key === parentKey)}
          onGoToParent={focus.focusRestoreButton}
          onDismiss={() => {
            restoring.dismissNotice();
            headingRef.current?.focus();
          }}
        />
        <RefetchErrorNotice query={result} message="無法更新已刪除的項目，畫面上可能是舊的。" />
        <div ref={listRef}>
          <DeletedItemsBody
            result={result}
            items={items}
            blueprint={detail.blueprint}
            now={now}
            canRestore={detail.myPermissions.includes('item.delete')}
            pendingKeys={restoring.pendingKeys}
            onRestore={(item) => {
              void restoring.restore(item);
            }}
          />
        </div>
      </div>
    </PageLayout>
  );
}
