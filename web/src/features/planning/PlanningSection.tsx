import { useId, type ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import { Button } from '../../components/Button/Button';
import { EmptyState, ErrorState, LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import styles from './Planning.module.css';

interface PlanningSectionProps<T> {
  /** 「週期」「里程碑」：h2、清單的名稱，也是新增鈕與空白狀態的用字。 */
  readonly title: string;
  readonly query: UseQueryResult<readonly T[]>;
  /** 沒有 item.update 的人沒有新增的入口。 */
  readonly canEdit: boolean;
  readonly emptyHint: string;
  /** 這一節是頁面第一筆內容：標 data-content-start（scripts/ui-audit.js 量高度預算）。 */
  readonly isContentStart?: boolean;
  readonly onAdd: () => void;
  readonly children: (rows: readonly T[]) => ReactNode;
}

/** 週期與里程碑一節的外框：標題與新增、載入與失敗、空白狀態（先說這裡會有什麼，再給新增）、清單。 */
export function PlanningSection<T>({ title, query, canEdit, emptyHint, isContentStart = false, onAdd, children }: PlanningSectionProps<T>) {
  const headingId = useId();
  const rows = query.data;
  const addButton = (size: 'md' | 'sm') =>
    canEdit ? (
      <Button size={size} onClick={onAdd}>
        {`新增${title}`}
      </Button>
    ) : undefined;
  return (
    <section className={styles.section} aria-labelledby={headingId}>
      <div className={styles.sectionHeader}>
        <h2 id={headingId} className={styles.sectionTitle}>
          {title}
        </h2>
        {rows !== undefined && rows.length > 0 && addButton('sm')}
      </div>
      <RefetchErrorNotice query={query} message={`無法更新${title}，畫面上可能是舊的。`} />
      {query.isPending && <LoadingState label={`正在載入${title}…`} />}
      {query.isLoadingError && (
        <ErrorState
          error={query.error}
          title={`${title}載入失敗`}
          onRetry={() => {
            void query.refetch();
          }}
        />
      )}
      {rows !== undefined && (
        <div data-content-start={isContentStart || undefined}>
          {rows.length === 0 ? (
            <EmptyState title={`還沒有${title}`} action={addButton('md')}>
              {emptyHint}
            </EmptyState>
          ) : (
            children(rows)
          )}
        </div>
      )}
    </section>
  );
}
