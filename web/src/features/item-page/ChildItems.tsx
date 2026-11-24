import { useEffect, useId, useMemo, useRef, type RefObject } from 'react';
import type { ItemTypeDefinition } from '../../api/blueprint';
import { errorMessage } from '../../api/http';
import { useProjectItems } from '../../api/queries';
import { queryKeys } from '../../api/queryKeys';
import type { Item, ItemDetail } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { List, ListRow } from '../../components/List/List';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import { isTerminal } from '../../domain/dates';
import { flattenItemPages } from '../item-table/itemCache';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { assigneeMeta, dueMeta, stateMeta } from '../work-items/itemListMeta';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import { SectionHeading, useItemPanelLink, useItemSequence } from './ItemPanelContext';
import { childItemsQuery, childTypesOf } from './knownChildren';
import { ChildQuickAdd } from './ChildQuickAdd';
import styles from './ChildItems.module.css';

type ChildResult = ReturnType<typeof useProjectItems>;

/** 骨架的列數：還不知道有幾個下層，先畫兩列的形狀。 */
const SKELETON_ROWS = 2;

/**
 * 按了「載入更多」、而且載完的是最後一頁時，按鈕會跟著不見：焦點放到新載入的第一筆（找不到就是清單的第一筆）。
 * 還有下一頁時按鈕留著，焦點本來就在上面；載入時使用者把焦點移到別處，就不搶。
 * 不用 lib/updateThenFocus：那是「同步改完畫面再放焦點」，這裡的更新要等請求回來，所以在畫面更新之後的 effect 裡放。
 */
function useFocusAfterLastPage(result: ChildResult, itemCount: number) {
  const listRef = useRef<HTMLDivElement>(null);
  const firstNewIndex = useRef<number | null>(null);
  const isFetching = result.isFetchingNextPage;
  const { hasNextPage } = result;
  useEffect(() => {
    const index = firstNewIndex.current;
    if (index === null || isFetching) return;
    firstNewIndex.current = null;
    const active = document.activeElement;
    if (hasNextPage || (active !== null && active !== document.body)) return;
    focusRowLink(listRef, index);
  }, [isFetching, hasNextPage, itemCount]);
  return {
    listRef,
    loadMore: () => {
      if (isFetching) return;
      firstNewIndex.current = itemCount;
      void result.fetchNextPage();
    },
  };
}

function focusRowLink(listRef: RefObject<HTMLDivElement | null>, index: number): void {
  const links = Array.from(listRef.current?.querySelectorAll<HTMLElement>('li > a[href]') ?? []);
  (links[index] ?? links[0])?.focus();
}

interface LoadMoreProps {
  readonly result: ChildResult;
  readonly onLoadMore: () => void;
}

/** 載入中用 aria-disabled 而不是 disabled：有焦點的按鈕一停用，焦點就掉回頁面本體。 */
function LoadMore({ result, onLoadMore }: LoadMoreProps) {
  if (!result.hasNextPage && !result.isFetchNextPageError) {
    return null;
  }
  const isFetching = result.isFetchingNextPage;
  return (
    <div className={styles.footer}>
      {result.hasNextPage && (
        <Button size="sm" aria-disabled={isFetching || undefined} onClick={onLoadMore}>
          {isFetching ? '載入中…' : '載入更多'}
        </Button>
      )}
      {result.isFetchNextPageError && (
        <p role="alert" className={styles.loadError}>
          載入更多失敗：{errorMessage(result.error)}
        </p>
      )}
    </div>
  );
}

interface ChildRowProps {
  readonly child: Item;
  readonly entry: ReturnType<typeof useItemPageEntry>;
}

/**
 * 一筆下層項目：整筆連到它的項目頁；寬螢幕一般的點擊開項目面板（整頁上在旁邊開，面板裡換成那個下層項目），
 * 開著的那一筆是選取的樣子（ListRow 的 isCurrent）。第二行和專案的項目清單同一組（work-items/itemListMeta.tsx）。
 */
function ChildRow({ child, entry }: ChildRowProps) {
  const { detail, todayDate } = useProjectTable();
  const panelLink = useItemPanelLink(child.key);
  return (
    <ListRow
      to={itemPagePath(child.projectKey, child.key)}
      linkState={entry}
      code={child.key}
      title={child.title}
      meta={[stateMeta(child, detail.blueprint), assigneeMeta(child), ...dueMeta(child, todayDate)]}
      isCurrent={panelLink['aria-current'] === 'true'}
      onLinkClick={panelLink.onClick}
    />
  );
}

function ChildList({ result, items }: { readonly result: ChildResult; readonly items: readonly Item[] }) {
  const { listRef, loadMore } = useFocusAfterLastPage(result, items.length);
  // 整頁上：面板的上一個／下一個照下層清單的順序（面板裡的這份清單不登記）。
  useItemSequence(
    items.map((child) => child.key),
    listRef,
  );
  // 從下層的整頁用上一頁回來時，焦點放回這一筆。
  const entry = useItemPageEntry();
  if (result.isPending) {
    return <Skeleton label="正在載入下層項目…" rows={SKELETON_ROWS} />;
  }
  if (result.data === undefined) {
    const retry = () => {
      void result.refetch();
    };
    return <ErrorState error={result.error} title="下層項目載入失敗" onRetry={retry} />;
  }
  if (items.length === 0) {
    return <p className={styles.empty}>還沒有下層項目。</p>;
  }
  return (
    <div ref={listRef}>
      <List label="下層項目" footer={<LoadMore result={result} onLoadMore={loadMore} />}>
        {items.map((child) => (
          <ChildRow key={child.key} child={child} entry={entry} />
        ))}
      </List>
    </div>
  );
}

/** 進度只在全部載入時才算得準；還有下一頁時不顯示，免得讓人誤會。 */
function progressText(result: ChildResult, items: readonly Item[]): string | null {
  if (result.hasNextPage || items.length === 0) {
    return null;
  }
  const done = items.filter((child) => isTerminal(child.stateCategory)).length;
  return `${String(done)} / ${String(items.length)} 完成`;
}

interface ChildSectionProps {
  readonly item: ItemDetail;
  readonly childTypes: readonly ItemTypeDefinition[];
}

function ChildSection({ item, childTypes }: ChildSectionProps) {
  const { can } = useProjectTable();
  const headingId = useId();
  const query = childItemsQuery(item.key);
  const result = useProjectItems(item.projectKey, query);
  const items = useMemo(() => flattenItemPages(result.data), [result.data]);
  const progress = progressText(result, items);
  return (
    <section className={styles.section} aria-labelledby={headingId}>
      <div className={styles.header}>
        <SectionHeading id={headingId} className={styles.heading}>
          下層項目
        </SectionHeading>
        {progress !== null && <span className={styles.progress}>{progress}</span>}
      </div>
      <ChildList result={result} items={items} />
      {can.create && (
        <ChildQuickAdd parentKey={item.key} childTypes={childTypes} listKey={queryKeys.items(item.projectKey, query)} />
      )}
    </section>
  );
}

/** 下層項目：有類型可以放在這個類型底下時才有。列出直接下層（整筆是連到它的項目頁的連結），可以快速新增。 */
export function ChildItems({ item }: { readonly item: ItemDetail }) {
  const { detail } = useProjectTable();
  const childTypes = childTypesOf(detail.blueprint, item.type);
  if (childTypes.length === 0) {
    return null;
  }
  return <ChildSection item={item} childTypes={childTypes} />;
}
