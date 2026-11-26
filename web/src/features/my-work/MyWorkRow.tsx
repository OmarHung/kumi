import type { ReactNode } from 'react';
import { Link } from 'react-router';
import type { CalendarDate, Item, ProjectDetail } from '../../api/types';
import { tableStyles } from '../../components/Table/Table';
import { stateLabel } from '../../domain/blueprint';
import { cx } from '../../lib/cx';
import { useItemScope } from '../item-page/ItemContent';
import { useItemPanelLink } from '../item-page/ItemPanelContext';
import { StateCell } from '../item-table/cells/StateCell';
import { ProjectTableProvider } from '../item-table/ProjectTableContext';
import { DueDateText, EmptyValue, PriorityText, StateBadge } from '../work-items/ItemCells';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import type { MyWorkColumnId } from './myWorkColumns';
import { pinClass } from './MyWorkHeader';
import { MyWorkProjectLink } from './MyWorkProjectLink';
import styles from './MyWork.module.css';

/**
 * 一列的專案範圍：狀態那一格就是表格的格子（StateCell），要那一列自己專案的藍圖、權限與成員對話框——
 * 我的工作跨專案，每個專案的流程與權限都不一樣（internal/HANDOFF-ui-ux-refactor.md 第 5 節）。
 * 成員對話框（改狀態被角色擋下時「到「成員」調整角色」）也是那個專案的。
 */
function RowScope({ detail, children }: { readonly detail: ProjectDetail; readonly children: ReactNode }) {
  const { value, membersDialog } = useItemScope(detail);
  return (
    <ProjectTableProvider value={value}>
      {children}
      {membersDialog}
    </ProjectTableProvider>
  );
}

/**
 * 狀態：專案內容讀到了就可以直接改（沒有 item.transition 的人是唯讀的文字，EditableCell 自己判斷）；
 * 還沒讀到或讀不到時先顯示狀態類別，不擋住整張表。
 */
function StateValue({ item, project }: { readonly item: Item; readonly project: ProjectDetail | undefined }) {
  if (project === undefined) {
    return (
      <div className={styles.stateFallback}>
        <StateBadge label={stateLabel(undefined, item.type, item.state, item.stateCategory)} category={item.stateCategory} />
      </div>
    );
  }
  return (
    <RowScope detail={project}>
      <StateCell item={item} />
    </RowScope>
  );
}

function TitleLink({ item }: { readonly item: Item }) {
  // 從整頁用上一頁回來時，焦點放回這一列（returnTo 不是專案頁，刪除之後照舊回到專案的表格）。
  const entry = useItemPageEntry();
  const panelLink = useItemPanelLink(item.key);
  return (
    <Link
      to={itemPagePath(item.projectKey, item.key)}
      state={entry}
      className={cx(tableStyles.rowLink, styles.title)}
      title={item.title}
      {...panelLink}
    >
      {item.title}
    </Link>
  );
}

interface CellProps {
  readonly id: MyWorkColumnId;
  readonly item: Item;
  readonly project: ProjectDetail | undefined;
  readonly todayDate: CalendarDate;
  /** 固定在左邊的欄（標題和它左邊的）：那些格子是 sticky。 */
  readonly pinned: readonly MyWorkColumnId[];
}

/** 一格，照欄畫（欄的順序是使用者調的）。 */
function MyWorkCell({ id, item, project, todayDate, pinned }: CellProps) {
  const pin = pinClass(id, pinned);
  switch (id) {
    case 'key':
      return (
        <td className={cx(tableStyles.mono, styles.clip, pin)} title={item.key}>
          {item.key}
        </td>
      );
    case 'title':
      return (
        <td className={cx(tableStyles.primaryCell, styles.clip, styles.titleCell, pin)}>
          <TitleLink item={item} />
        </td>
      );
    case 'state':
      return (
        <td className={cx(styles.stateCell, pin)}>
          <StateValue item={item} project={project} />
        </td>
      );
    case 'priority':
      return (
        <td className={cx(styles.priorityCell, pin)}>
          <PriorityText priority={item.priority} />
        </td>
      );
    case 'due':
      return (
        <td className={cx(styles.dueCell, pin)}>
          <DueDateText dueDate={item.dueDate} category={item.stateCategory} today={todayDate} />
        </td>
      );
    case 'assignee':
      return (
        <td className={cx(styles.clip, pin)} title={item.assignee?.displayName}>
          {item.assignee?.displayName ?? <EmptyValue label="沒有負責人" />}
        </td>
      );
    case 'project':
      return (
        <td className={cx(styles.clip, pin)}>
          <MyWorkProjectLink projectKey={item.projectKey} name={project?.project.name ?? item.projectKey} />
        </td>
      );
  }
}

interface RowProps {
  readonly item: Item;
  readonly project: ProjectDetail | undefined;
  readonly todayDate: CalendarDate;
  /** 畫面上的欄（照使用者的順序；依專案分組時沒有專案）。 */
  readonly columns: readonly MyWorkColumnId[];
  readonly pinned: readonly MyWorkColumnId[];
  /** 剛改成完成或取消、而且不再符合條件的：留在原位、變淡、標題劃掉。 */
  readonly isSettled: boolean;
  /** 第一組的第一列：給 scripts/ui-audit.js 量第一筆內容離頂端多遠（docs/DESIGN.md 6.2）。 */
  readonly isFirst: boolean;
}

/**
 * 一列。標題連到項目頁；寬螢幕一般的點擊在旁邊開項目面板，用那個項目自己的專案內容。
 * 開著的那一列標 aria-current，選取的樣子照它選（components/Table）。剛改成完成或取消的標 data-settled：
 * 留在原位、變淡、標題劃掉（docs/DESIGN.md 8.3「我的工作」）。標題和它左邊的欄固定在左邊（放不下、橫向捲動時）。
 */
export function MyWorkRowView({ item, project, todayDate, columns, pinned, isSettled, isFirst }: RowProps) {
  return (
    // data-row-key：停駐點那一列換了位置或不見了（換分組、重抓、換排序）時，用它找回那一列或相鄰的一列（lib/useRovingGrid.ts）。
    <tr className={styles.row} data-row-key={item.key} data-content-start={isFirst || undefined} data-settled={isSettled || undefined}>
      {columns.map((id) => (
        <MyWorkCell key={id} id={id} item={item} project={project} todayDate={todayDate} pinned={pinned} />
      ))}
    </tr>
  );
}
