import { useItemTypes, useProjects } from '../../api/queries';
import { SelectField } from '../../components/Field/Field';
import type { SelectOption } from '../../components/Select/Select';
import type { MyWorkFilters } from '../../domain/myWorkView';
import { DueField, PriorityField } from '../item-table/DueAndPriorityFields';
import type { CollapsibleCondition } from '../item-table/inlineConditions';
import { toolbarFieldProps, type ToolbarFieldProps } from '../item-table/toolbarField';
import {
  hasTypeChoice,
  projectOptions,
  pruneToProject,
  statusFromValue,
  statusOptions,
  statusValueOf,
  typeOptions,
} from './myWorkChoices';
import { useMyWorkScope } from './useMyWorkScope';

/**
 * 「我的工作」可以篩選的條件。和專案頁一樣由 keys 決定畫哪幾個、一律照固定的順序（類型、狀態、到期、優先度、專案），
 * 列上留幾個、「更多條件」與「篩選」浮層裡放幾個都是同一份順序。狀態用 'state'（和 inlineConditions.ts 的名稱一致）。
 */
export type MyWorkConditionKey = CollapsibleCondition | 'project';

export const ALL_MY_WORK_CONDITIONS: ReadonlySet<MyWorkConditionKey> = new Set<MyWorkConditionKey>([
  'type',
  'state',
  'due',
  'priority',
  'project',
]);

/** 這個條件和預設不一樣嗎：「更多條件」按鈕上的數字數的是這個。 */
export function isConditionActive(filters: MyWorkFilters, key: MyWorkConditionKey): boolean {
  switch (key) {
    case 'type':
      return filters.type !== null;
    case 'state':
      return filters.status.kind !== 'open';
    case 'assignee':
      // 共用的條件型別（inlineConditions.ts）有負責人；「我的工作」沒有這個條件。
      return false;
    case 'due':
      return filters.due !== null;
    case 'priority':
      return filters.priority !== null;
    case 'project':
      return filters.project !== null;
  }
}

/** 類型下拉的選項，以及放不放（只有一種類型時不放）：工具列要先知道有幾個條件，才算得出放不放得下。 */
export function useTypeChoices(filters: MyWorkFilters): { readonly options: readonly SelectOption[]; readonly hasChoice: boolean } {
  const types = useItemTypes();
  const { detail } = useMyWorkScope(filters.project);
  const options = typeOptions(types.data, detail, filters);
  return { options, hasChoice: hasTypeChoice(options, filters) };
}

interface FieldProps {
  readonly filters: MyWorkFilters;
  readonly onChange: (next: MyWorkFilters) => void;
  readonly fieldProps: ReturnType<typeof toolbarFieldProps>;
}

function TypeField({ filters, onChange, fieldProps }: FieldProps) {
  const { detail } = useMyWorkScope(filters.project);
  const { options, hasChoice } = useTypeChoices(filters);
  if (!hasChoice) return null;
  return (
    <SelectField
      {...fieldProps}
      label="類型"
      value={filters.type ?? ''}
      options={options}
      onChange={(next) => {
        // 換了類型以後，原本選的狀態可能不在新類型的流程裡（選了專案才有個別的狀態）：拿掉，不要留著篩出空的結果。
        onChange(pruneToProject({ ...filters, type: next || null }, detail));
      }}
    />
  );
}

function StatusField({ filters, onChange, fieldProps }: FieldProps) {
  const { detail } = useMyWorkScope(filters.project);
  return (
    <SelectField
      {...fieldProps}
      label="狀態"
      value={statusValueOf(filters.status)}
      options={statusOptions(filters, detail)}
      onChange={(next) => {
        onChange({ ...filters, status: statusFromValue(next) });
      }}
    />
  );
}

function ProjectField({ filters, onChange, fieldProps }: FieldProps) {
  const projects = useProjects();
  return (
    <SelectField
      {...fieldProps}
      label="專案"
      value={filters.project ?? ''}
      options={projectOptions(projects.data, filters.project)}
      onChange={(project) => {
        // 狀態的個別選項是那個專案的：不選專案了就回到預設的狀態，不留著一個對不上的 key。
        // 換到另一個專案時，類型、狀態不在那裡的由頁面清掉（MyWorkPage 讀到那個專案的內容之後）。
        const status = project === '' && filters.status.kind === 'state' ? ({ kind: 'open' } as const) : filters.status;
        onChange({ ...filters, project: project || null, status });
      }}
    />
  );
}

interface MyWorkConditionFieldsProps extends ToolbarFieldProps {
  readonly filters: MyWorkFilters;
  readonly onChange: (next: MyWorkFilters) => void;
  readonly keys: ReadonlySet<MyWorkConditionKey>;
}

/** 條件那一組下拉，寬螢幕的列上、「更多條件」與窄螢幕的「篩選」浮層共用：由 keys 決定畫哪幾個。 */
export function MyWorkConditionFields({ filters, onChange, keys, isToolbar }: MyWorkConditionFieldsProps) {
  const field = { filters, onChange, fieldProps: toolbarFieldProps(isToolbar) };
  return (
    <>
      {keys.has('type') && <TypeField {...field} />}
      {keys.has('state') && <StatusField {...field} />}
      {keys.has('due') && <DueField isToolbar={isToolbar} value={filters.due} onChange={(due) => { onChange({ ...filters, due }); }} />}
      {keys.has('priority') && (
        <PriorityField isToolbar={isToolbar} value={filters.priority} onChange={(priority) => { onChange({ ...filters, priority }); }} />
      )}
      {keys.has('project') && <ProjectField {...field} />}
    </>
  );
}
