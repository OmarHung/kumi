import type { StateCategory } from '../../api/blueprint';
import type { CalendarDate, Priority } from '../../api/types';
import { Badge, type BadgeTone } from '../../components/Badge/Badge';
import type { IconName } from '../../components/Icon/Icon';
import { formatDate, isOverdue } from '../../domain/dates';
import { PRIORITY_LABELS } from '../../domain/labels';
import { cx } from '../../lib/cx';
import styles from './ItemCells.module.css';

/** 項目欄位的顯示方式。我的工作、專案的表格與清單、項目頁、看板共用，同一個值在每個畫面長得一樣。 */

const CATEGORY_TONES: Readonly<Record<StateCategory, BadgeTone>> = {
  backlog: 'stateIdle',
  not_started: 'stateIdle',
  in_progress: 'stateActive',
  done: 'stateDone',
  canceled: 'stateIdle',
};

// 顏色之外一定有形狀（docs/DESIGN.md 第 4 節）：同樣是灰色的待規劃、未開始、已取消也分得出來。
// 下拉的狀態選項前面也畫同一組形狀（規範 7.3），所以匯出。
export const CATEGORY_ICONS: Readonly<Record<StateCategory, IconName>> = {
  backlog: 'stateBacklog',
  not_started: 'stateNotStarted',
  in_progress: 'stateInProgress',
  done: 'stateDone',
  canceled: 'stateCanceled',
};

interface StateBadgeProps {
  /** 藍圖裡的狀態名稱；用 domain/blueprint 的 stateLabel 取得。 */
  readonly label: string;
  readonly category: StateCategory;
}

export function StateBadge({ label, category }: StateBadgeProps) {
  return (
    <Badge tone={CATEGORY_TONES[category]} icon={CATEGORY_ICONS[category]}>
      {label}
    </Badge>
  );
}

/** 沒有值：畫面上是一條裝飾的短線（CSS 畫的，不是文字符號，docs/DESIGN.md 7.1），輔助科技唸出 label。 */
export function EmptyValue({ label }: { readonly label: string }) {
  return (
    <>
      <span aria-hidden="true" className={styles.empty} />
      <span className="visually-hidden">{label}</span>
    </>
  );
}

const PRIORITY_CLASSES: Readonly<Record<Priority, string | undefined>> = {
  none: undefined,
  low: styles.low,
  medium: styles.medium,
  high: styles.high,
  urgent: styles.urgent,
};

export function PriorityText({ priority }: { readonly priority: Priority }) {
  if (priority === 'none') {
    return <EmptyValue label={PRIORITY_LABELS.none} />;
  }
  return (
    <span className={cx(styles.priority, PRIORITY_CLASSES[priority])}>
      {PRIORITY_LABELS[priority]}
    </span>
  );
}

interface DueDateTextProps {
  readonly dueDate: CalendarDate | null;
  readonly category: StateCategory;
  /** 由呼叫端傳入，同一張表的每一列用同一個「今天」。 */
  readonly today: CalendarDate;
}

export function DueDateText({ dueDate, category, today }: DueDateTextProps) {
  if (dueDate === null) {
    return <EmptyValue label="沒有到期日" />;
  }
  const overdue = isOverdue(dueDate, category, today);
  return (
    <span className={styles.due}>
      <time dateTime={dueDate} className={cx(overdue && styles.overdue)}>
        {formatDate(dueDate)}
      </time>
      {overdue && <Badge tone="danger">已過期</Badge>}
    </span>
  );
}
