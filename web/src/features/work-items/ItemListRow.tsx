import type { ReactNode } from 'react';
import { Link } from 'react-router';
import type { ProjectBlueprint } from '../../api/blueprint';
import type { CalendarDate, Item } from '../../api/types';
import { Avatar } from '../../components/Avatar/Avatar';
import { Icon } from '../../components/Icon/Icon';
import { itemTypeName, stateLabel } from '../../domain/blueprint';
import { formatDate, formatShortDate, isOverdue } from '../../domain/dates';
import { cx } from '../../lib/cx';
import { PriorityText, StateBadge } from './ItemCells';
import { itemPagePath, useItemPageEntry } from './itemLinks';
import styles from './ItemListRow.module.css';

interface ItemListRowProps {
  readonly item: Item;
  /** 專案的藍圖：類型與狀態的名稱；還沒讀到時用代號與狀態類別。 */
  readonly blueprint: ProjectBlueprint | undefined;
  readonly todayDate: CalendarDate;
  /** 我的工作跨專案：多顯示專案的名稱（依專案分組時組名就是專案，不重複）。 */
  readonly projectName?: string;
  /** 右邊要不要放負責人（我的工作「我負責的」每一筆都是我，不重複）。 */
  readonly showAssignee?: boolean;
  /** 標題要不要劃掉（剛改成完成或取消、還沒離開清單的）。 */
  readonly isSettled?: boolean;
  readonly isContentStart?: boolean;
}

/** 優先度只有高與緊急才在清單上出現：其餘的在詳細頁看（窄螢幕一筆只有兩行，不放不急的字）。 */
function UrgentPriority({ item }: { readonly item: Item }) {
  if (item.priority !== 'high' && item.priority !== 'urgent') return null;
  return (
    <span className={cx(styles.metaItem, styles.urgent)}>
      <span className="visually-hidden">優先度：</span>
      <Icon name="arrowUp" />
      <PriorityText priority={item.priority} />
    </span>
  );
}

function AssigneeMark({ item }: { readonly item: Item }) {
  if (item.assignee === null) return <span className="visually-hidden">沒有負責人</span>;
  return (
    <span className={styles.assignee}>
      <span className="visually-hidden">{`負責人：${item.assignee.displayName}`}</span>
      <Avatar name={item.assignee.displayName} size="sm" decorative />
    </span>
  );
}

/** 到期日：過期的寫「已過期 日期」、用危險色，一行不折（放不下時讓屬性那一格折行，不讓日期斷在中間）。 */
function DueMark({ item, todayDate }: { readonly item: Item; readonly todayDate: CalendarDate }) {
  if (item.dueDate === null) return null;
  const isLate = isOverdue(item.dueDate, item.stateCategory, todayDate);
  return (
    <span className={cx(styles.due, isLate && styles.overdue)}>
      <span className="visually-hidden">到期：</span>
      {isLate && '已過期 '}
      {/* 看得到的是簡短的寫法（今年的省掉年份），報讀照舊唸完整的日期。 */}
      <time dateTime={item.dueDate}>
        <span className="visually-hidden">{formatDate(item.dueDate)}</span>
        <span aria-hidden="true">{formatShortDate(item.dueDate, todayDate)}</span>
      </time>
    </span>
  );
}

function Side({ item, showAssignee }: { readonly item: Item; readonly showAssignee: boolean }): ReactNode {
  if (!showAssignee) return null;
  return (
    <span className={styles.side}>
      <AssigneeMark item={item} />
    </span>
  );
}

/**
 * 窄螢幕的項目清單一筆（docs/design/07-components.md 7.1，2026-11-28 版型比例）：第一行是編號與標題（一行省略號），
 * 第二行左邊是狀態、高與緊急的優先度、類型（一行不折），右邊是到期日；負責人頭像在右邊、兩行置中；我的工作再加第三行專案名稱。整筆是連到項目頁的連結。
 * DOM 的順序是編號、標題、其餘（連結的名稱照這個順序唸，和以前一樣以「編號 標題」開頭）；上下兩行的位置靠 CSS 的格線排。
 */
export function ItemListRow({ item, blueprint, todayDate, projectName, showAssignee = true, isSettled = false, isContentStart = false }: ItemListRowProps) {
  const entry = useItemPageEntry();
  return (
    <li className={styles.row} data-content-start={isContentStart || undefined}>
      <Link to={itemPagePath(item.projectKey, item.key)} state={entry} className={styles.link}>
        <span className={styles.main}>
          <span className={styles.code}>{item.key}</span>{' '}
          <span className={cx(styles.title, isSettled && styles.settled)} title={item.title}>
            {item.title}
          </span>{' '}
          <span className={styles.meta}>
            <span className={styles.metaItem}>
              <span className="visually-hidden">狀態：</span>
              <StateBadge label={stateLabel(blueprint, item.type, item.state, item.stateCategory)} category={item.stateCategory} />
            </span>{' '}
            <span className={cx(styles.metaItem, styles.type)}>
              <span className="visually-hidden">類型：</span>
              {blueprint === undefined ? item.type : itemTypeName(blueprint, item.type)}
            </span>{' '}
            <UrgentPriority item={item} />
          </span>
          {projectName !== undefined && (
            <>
              {' '}
              <span className={styles.project}>
                <span className="visually-hidden">專案：</span>
                {projectName}
              </span>
            </>
          )}
        </span>{' '}
        <Side item={item} showAssignee={showAssignee} />
        {item.dueDate !== null && ' '}
        <DueMark item={item} todayDate={todayDate} />
      </Link>
    </li>
  );
}
