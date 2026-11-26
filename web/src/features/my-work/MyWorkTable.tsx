import type { ReactNode, Ref } from 'react';
import { useSearchParams } from 'react-router';
import { useProjectDetails } from '../../api/queries';
import type { CalendarDate, Item, ProjectDetail } from '../../api/types';
import type { ColumnWidthStore } from '../../components/Table/columnWidths';
import { List, ListRow, type ListMeta } from '../../components/List/List';
import { myWorkMineOf } from '../../domain/myWorkView';
import { useIsCompact } from '../../lib/useMediaQuery';
import type { RovingGridHandle } from '../../lib/useRovingGrid';
import { useItemSequence } from '../item-page/ItemPanelContext';
import { assigneeMeta, dueMeta, priorityMeta, projectMeta, stateMeta } from '../work-items/itemListMeta';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import { MyWorkGrid, type MyWorkViewProps } from './MyWorkGrid';
import { MyWorkGroupSection } from './MyWorkGroupSection';
import type { ColumnOrderStore } from './myWorkColumnOrder';
import { groupingOf, groupMyWork } from './myWorkGroups';
import { myWorkSortOf, nextMyWorkSort, sortMyWorkRows, withMyWorkSort, type MyWorkSortField } from './myWorkSort';
import type { MyWorkRow } from './useMyWorkRows';
import styles from './MyWork.module.css';
import { useWeekStart } from '../../lib/weekStart';

/**
 * 窄螢幕的第二行：狀態、專案（依專案分組時組名就是專案，不重複）、優先度與到期日（後兩個有設才有）。
 * 整筆是連結，名稱會把這一行唸出來，所以每一個欄位都帶著說法；欄位在 work-items/itemListMeta.tsx。
 */
function myWorkMeta(item: Item, project: ProjectDetail | undefined, todayDate: CalendarDate, { showProject, showAssignee }: Pick<MyWorkViewProps, 'showProject' | 'showAssignee'>): ListMeta[] {
  return [
    stateMeta(item, project?.blueprint),
    ...(showProject ? [projectMeta(item, project)] : []),
    ...(showAssignee ? [assigneeMeta(item)] : []),
    ...priorityMeta(item),
    ...dueMeta(item, todayDate),
  ];
}

/**
 * 窄螢幕：和專案的清單同一套（components/List，一筆兩行），跟著整頁捲；每一組一個標題與一份清單。
 * 就地編輯留在詳細頁（docs/DESIGN.md 7.1）：整筆是連到項目頁的連結，狀態在那裡改。
 */
function MyWorkList({ groups, projects, todayDate, showProject, showAssignee, isLeaving, footer }: MyWorkViewProps & { readonly footer?: ReactNode }) {
  const entry = useItemPageEntry();
  // 窄螢幕不用面板；登記順序是為了從整頁用上一頁回來時，焦點放回這一筆。
  useItemSequence(groups.flatMap((group) => group.items.map((row) => row.key)));
  const firstKey = groups[0]?.items[0]?.key;
  return (
    <>
      <div className={styles.groups}>
        {groups.map((group) => (
          <MyWorkGroupSection key={group.key} group={group}>
            <List label={group.label}>
              {group.items.map(({ item }) => (
                <ListRow
                  key={item.key}
                  to={itemPagePath(item.projectKey, item.key)}
                  linkState={entry}
                  code={item.key}
                  // 剛改成完成或取消的（視窗從寬螢幕變窄時）：標題劃掉，和表格一樣。
                  title={isLeaving(item) ? <span className={styles.settledTitle}>{item.title}</span> : item.title}
                  titleText={item.title}
                  meta={myWorkMeta(item, projects.get(item.projectKey), todayDate, { showProject, showAssignee })}
                  isContentStart={item.key === firstKey}
                />
              ))}
            </List>
          </MyWorkGroupSection>
        ))}
      </div>
      {footer}
    </>
  );
}

interface MyWorkTableProps {
  /** 畫面上的列：顯示的是最新的一份，分組與排序看的是它的位置（useMyWorkRows）。 */
  readonly rows: readonly MyWorkRow[];
  /** 寬螢幕表格的欄寬（所有組共用，docs/DESIGN.md 7.1「欄寬」）。 */
  readonly widthStore: ColumnWidthStore;
  /** 寬螢幕表格的欄的順序（所有組與工具列的「欄的順序」共用）。 */
  readonly orderStore: ColumnOrderStore;
  /** 「今天」：分組與過期的標示用同一個值（頁面決定，條件與它也是同一個）。 */
  readonly todayDate: CalendarDate;
  readonly isLeaving: (item: Item) => boolean;
  /** 最下面的「已載入 N 個項目」與「載入更多」。 */
  readonly footer?: ReactNode;
  readonly gridRef?: Ref<RovingGridHandle>;
}

/** 網址上的排序與點表頭換排序（不留歷史紀錄：上一頁回到上一個畫面，不是上一個排序；條件、分組、開著的面板留著）。 */
function useMyWorkSort() {
  const [searchParams, setSearchParams] = useSearchParams();
  const onSort = (field: MyWorkSortField) => {
    setSearchParams((previous) => withMyWorkSort(previous, nextMyWorkSort(myWorkSortOf(previous), field)), { replace: true });
  };
  return { sort: myWorkSortOf(searchParams), grouping: groupingOf(searchParams), mine: myWorkMineOf(searchParams), onSort };
}

/**
 * 指派給我的項目，分組顯示：依急迫程度（預設；已過期、今天、這一週、之後、沒有到期日）或依專案（網址的 group=project）。
 * 組內照網址的排序（預設到期日再編號，myWorkSort.ts）。寬螢幕是每一組一張表格，窄螢幕是每一組一份清單。
 */
export function MyWorkTable({ rows, widthStore, orderStore, todayDate, isLeaving, footer, gridRef }: MyWorkTableProps) {
  // 狀態名稱在各專案的藍圖裡；拿到之前先顯示狀態類別，不擋住整張表。
  const projectKeys = [...new Set(rows.map((row) => row.projectKey))];
  const projects = useProjectDetails(projectKeys);
  const { sort, grouping, mine, onSort } = useMyWorkSort();
  const weekStart = useWeekStart();
  const isCompact = useIsCompact();
  const projectName = (projectKey: string) => projects.get(projectKey)?.project.name;
  const groups = groupMyWork(rows, grouping, todayDate, weekStart, projectName).map((group) => ({
    ...group,
    items: sortMyWorkRows(group.items, sort, projectName),
  }));
  const props: MyWorkViewProps = { groups, projects, todayDate, showProject: grouping !== 'project', showAssignee: mine === 'created', isLeaving };
  if (isCompact) return <MyWorkList {...props} footer={footer} />;
  return <MyWorkGrid {...props} widthStore={widthStore} orderStore={orderStore} sort={sort} onSort={onSort} footer={footer} gridRef={gridRef} />;
}
