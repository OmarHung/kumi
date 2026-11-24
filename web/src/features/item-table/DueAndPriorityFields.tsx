import { SelectField } from '../../components/Field/Field';
import type { SelectOption } from '../../components/Select/Select';
import type { Priority } from '../../api/types';
import { DUE_FILTER_LABELS, DUE_FILTERS, PRIORITIES, PRIORITY_FILTER_LABELS, parseDueFilter, parsePriority, type DueFilter } from '../../domain/dueFilter';
import { toolbarFieldProps, type ToolbarFieldProps } from './toolbarField';

// 「所有…」本身是一個選擇（不篩選），是真的選項，不是 placeholder（規範 7.3）。
const DUE_OPTIONS: readonly SelectOption[] = [
  { value: '', label: '所有到期日' },
  ...DUE_FILTERS.map((due) => ({ value: due, label: DUE_FILTER_LABELS[due] })),
];

const PRIORITY_OPTIONS: readonly SelectOption[] = [
  { value: '', label: '所有優先度' },
  ...PRIORITIES.map((priority) => ({ value: priority, label: PRIORITY_FILTER_LABELS[priority] })),
];

interface DueFieldProps extends ToolbarFieldProps {
  readonly value: DueFilter | null;
  readonly onChange: (due: DueFilter | null) => void;
}

/** 到期：已過期、今天、本週、沒有到期日。選了就換（規範 7.3 的馬上生效）；「今天」是使用者所在地的日期。 */
export function DueField({ value, onChange, isToolbar }: DueFieldProps) {
  return (
    <SelectField
      {...toolbarFieldProps(isToolbar)}
      label="到期"
      value={value ?? ''}
      options={DUE_OPTIONS}
      onChange={(next) => {
        onChange(parseDueFilter(next));
      }}
    />
  );
}

interface PriorityFieldProps extends ToolbarFieldProps {
  readonly value: Priority | null;
  readonly onChange: (priority: Priority | null) => void;
}

/** 優先度：緊急到低、「沒有」是還沒設定的。 */
export function PriorityField({ value, onChange, isToolbar }: PriorityFieldProps) {
  return (
    <SelectField
      {...toolbarFieldProps(isToolbar)}
      label="優先度"
      value={value ?? ''}
      options={PRIORITY_OPTIONS}
      onChange={(next) => {
        onChange(parsePriority(next));
      }}
    />
  );
}
