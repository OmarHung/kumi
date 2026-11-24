import { SelectField } from '../../components/Field/Field';
import type { SelectOption } from '../../components/Select/Select';
import {
  CURRENT_CYCLE,
  currentCycle,
  DELETED_CYCLE_TEXT,
  DELETED_MILESTONE_TEXT,
  NO_PLANNING,
  UNKNOWN_CYCLE_TEXT,
  UNKNOWN_MILESTONE_TEXT,
} from '../../domain/planningFilter';
import { useToday } from '../../lib/useToday';
import { useProjectTable } from './ProjectTableContext';
import { toolbarFieldProps, type ToolbarFieldProps } from './toolbarField';
import { usePlanning } from './usePlanning';

/** 現在沒有進行中的週期時，篩「目前的週期」說的話。篩選的旁邊（浮層）與空白狀態都用它。 */
export const NO_CURRENT_CYCLE_TEXT = '現在沒有進行中的週期。';

interface PlanningFieldProps extends ToolbarFieldProps {
  /** 篩選的值；null 是所有週期（所有里程碑）。 */
  readonly value: string | null;
  readonly onChange: (value: string | null) => void;
}

function withMissing(options: readonly SelectOption[], value: string | null, missingLabel: string): readonly SelectOption[] {
  // 網址或檢視指到已經刪除的：選單上要看得到、也選得回「所有…」。
  return value === null || options.some((option) => option.value === value) ? options : [...options, { value, label: missingLabel }];
}

/** 專案裡有沒有週期、里程碑：沒有的那一個不畫（欄位不佔位子）；網址指著它時照畫，才拿得掉。清單讀不到時當成沒有（網址沒指著它就不畫）。 */
export function usePlanningVisibility(cycle: string | null, milestone: string | null) {
  const { detail } = useProjectTable();
  const { cycles, milestones } = usePlanning(detail.project.key);
  return { hasCycles: cycles.length > 0 || cycle !== null, hasMilestones: milestones.length > 0 || milestone !== null };
}

/** 週期：所有週期、目前的週期、沒有週期，再來是每一個（依開始日）。選了就換。 */
export function CycleField({ value, onChange, isToolbar }: PlanningFieldProps) {
  const { detail } = useProjectTable();
  const { cycles, isCyclesPending, isCyclesFailed } = usePlanning(detail.project.key);
  const today = useToday();
  const options = withMissing(
    [
      { value: '', label: '所有週期' },
      { value: CURRENT_CYCLE, label: '目前的週期' },
      { value: NO_PLANNING, label: '沒有週期' },
      ...cycles.map((cycle) => ({ value: cycle.id, label: cycle.name })),
    ],
    isCyclesPending ? null : value,
    isCyclesFailed ? UNKNOWN_CYCLE_TEXT : DELETED_CYCLE_TEXT,
  );
  const hasNoCurrent = value === CURRENT_CYCLE && !isCyclesPending && !isCyclesFailed && currentCycle(cycles, today) === undefined;
  return (
    <SelectField
      {...toolbarFieldProps(isToolbar)}
      label="週期"
      value={value ?? ''}
      options={options}
      // 工具列上放說明會把列撐高：那裡由空白狀態說（ItemResults），浮層裡的標籤看得到，就在欄位旁邊說。
      hint={hasNoCurrent && isToolbar !== true ? NO_CURRENT_CYCLE_TEXT : undefined}
      onChange={(next) => {
        onChange(next || null);
      }}
    />
  );
}

/** 里程碑：所有里程碑、沒有里程碑，再來是每一個（依到期日）。 */
export function MilestoneField({ value, onChange, isToolbar }: PlanningFieldProps) {
  const { detail } = useProjectTable();
  const { milestones, isMilestonesPending, isMilestonesFailed } = usePlanning(detail.project.key);
  const options = withMissing(
    [
      { value: '', label: '所有里程碑' },
      { value: NO_PLANNING, label: '沒有里程碑' },
      ...milestones.map((milestone) => ({ value: milestone.id, label: milestone.name })),
    ],
    isMilestonesPending ? null : value,
    isMilestonesFailed ? UNKNOWN_MILESTONE_TEXT : DELETED_MILESTONE_TEXT,
  );
  return (
    <SelectField
      {...toolbarFieldProps(isToolbar)}
      label="里程碑"
      value={value ?? ''}
      options={options}
      onChange={(next) => {
        onChange(next || null);
      }}
    />
  );
}
