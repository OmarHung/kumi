import { useEffect, useEffectEvent, useId, useState, type ReactNode, type Ref, type SubmitEvent } from 'react';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Button } from '../../components/Button/Button';
import { SelectField, TextField } from '../../components/Field/Field';
import { Icon } from '../../components/Icon/Icon';
import { Popover } from '../../components/Popover/Popover';
import type { SelectOption } from '../../components/Select/Select';
import type { ItemFilters } from '../../domain/itemFilters';
import { filterableStates } from '../../domain/workflow';
import { AnyStateMark, EveryoneMark, NobodyMark, PersonMark, personOption } from '../work-items/selectOptionMarks';
import { stateFilterChoices } from '../work-items/stateChoices';
import { DueField, PriorityField } from './DueAndPriorityFields';
import { FieldFilterFields } from './FieldFilterFields';
import type { CollapsibleCondition, MoreOnlyCondition } from './inlineConditions';
import { CycleField, MilestoneField, usePlanningVisibility } from './PlanningFilterFields';
import { useProjectTable } from './ProjectTableContext';
import { toolbarFieldProps, type ToolbarFieldProps } from './toolbarField';
import styles from './ItemToolbar.module.css';

/** 打字停下來這麼久才去搜尋，不必每個字都打一次 API。 */
const SEARCH_DELAY_MS = 300;

interface FilterBarProps {
  /** role="group" 的名稱，例如「篩選項目」「篩選看板」。 */
  readonly label: string;
  /** 量這一組的寬度、決定哪些條件留在列上、哪些收進「更多條件」（inlineConditions.ts）。 */
  readonly barRef?: Ref<HTMLDivElement>;
  readonly children: ReactNode;
}

/** 寬螢幕工具列上的一組篩選（表格與看板共用）：一列、放不下才換行，欄位矮一號、標籤視覺上隱藏。 */
export function FilterBar({ label, barRef, children }: FilterBarProps) {
  return (
    <div ref={barRef} className={styles.filterBar} role="group" aria-label={label}>
      {children}
    </div>
  );
}

interface FilterPopoverProps {
  /** 目前有幾個條件：按鈕上看得到數字，完整的說法用 aria-describedby，按鈕的名稱只有「篩選」。 */
  readonly count: number;
  /** 「篩選」按鈕的 id：浮層裡的「清除條件」按了跟著不見時，焦點回到這顆按鈕。 */
  readonly buttonId?: string;
  /** 按鈕與浮層的名稱，預設「篩選」；「更多條件」也用這個浮層（沒有漏斗圖示）。 */
  readonly label?: string;
  readonly hasIcon?: boolean;
  /** 浮層裡那一組欄位（role="group"）的名稱。 */
  readonly groupLabel: string;
  readonly children: ReactNode;
}

/**
 * 窄螢幕的「篩選」（表格與看板共用）：工具列上只留一顆按鈕，打開是標籤看得到的欄位。
 * 條件一改就套用（記在網址），浮層留著，可以接著改。
 */
export function FilterPopover({ count, buttonId, groupLabel, label = '篩選', hasIcon = true, children }: FilterPopoverProps) {
  const countId = useId();
  return (
    <>
      <Popover
        label={label}
        renderTrigger={(trigger) => (
          <Button {...trigger} id={buttonId} size="sm" aria-describedby={count > 0 ? countId : undefined}>
            {hasIcon && <Icon name="filter" />}
            {label}
            {count > 0 && (
              <span className={styles.filterCount} aria-hidden="true">
                {count}
              </span>
            )}
          </Button>
        )}
      >
        <div className={styles.compactFilters} role="group" aria-label={groupLabel}>
          {children}
        </div>
      </Popover>
      {count > 0 && (
        <span id={countId} hidden>
          {`目前有 ${String(count)} 個條件`}
        </span>
      )}
    </>
  );
}

interface SearchFieldProps extends ToolbarFieldProps {
  readonly value: string;
  readonly onSearch: (q: string) => void;
  /** 呼叫端要把焦點放回搜尋框時（例如按了會跟著消失的「清除條件」）。 */
  readonly inputRef?: Ref<HTMLInputElement>;
  /** 搜尋的是別的東西時（專案清單頁）換掉名稱與提示；預設是項目。 */
  readonly label?: string;
  readonly placeholder?: string;
  /** 輸入框的字數上限（搜尋的伺服器有上限時給）。 */
  readonly maxLength?: number;
}

/** 表格與看板（專案清單頁也用）共用的搜尋框：停下來或按 Enter 才搜尋；網址的條件從外面變了，輸入框跟著換。 */
export function SearchField({ value, onSearch, inputRef, isToolbar, label = '搜尋', placeholder = '標題或編號，例如 WEB-12', maxLength }: SearchFieldProps) {
  const [text, setText] = useState(value);
  const [syncedValue, setSyncedValue] = useState(value);
  // 網址的條件從外面變了（清除條件、上一頁），輸入框跟著換。
  if (value !== syncedValue) {
    setSyncedValue(value);
    setText(value);
  }
  const emit = useEffectEvent((next: string) => {
    onSearch(next);
  });

  useEffect(() => {
    if (text.trim() === value) {
      return undefined;
    }
    const timer = window.setTimeout(() => {
      emit(text.trim());
    }, SEARCH_DELAY_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [text, value]);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    onSearch(text.trim());
  }

  return (
    <form role="search" className={styles.search} onSubmit={handleSubmit}>
      {/* 標籤在工具列上視覺上隱藏時，放大鏡與提示文字說明這是搜尋（名稱仍然是「搜尋」）。 */}
      <TextField
        {...toolbarFieldProps(isToolbar)}
        leadingIcon="search"
        ref={inputRef}
        label={label}
        type="search"
        value={text}
        placeholder={placeholder}
        maxLength={maxLength}
        onChange={(event) => {
          setText(event.target.value);
        }}
      />
    </form>
  );
}

interface AssigneeFieldProps extends ToolbarFieldProps {
  /** `me`、`unassigned` 或人員編號；null 是所有人。 */
  readonly value: string | null;
  readonly onChange: (assignee: string | null) => void;
}

/**
 * 負責人篩選：所有人、我、沒有負責人一組，其他成員一組（規範 7.3 的分組：中間一條細線）。
 * 每一個選項前面都有頭像大小的小圖，字才對齊：「我」與成員是頭像，「所有人」「沒有負責人」是佔位的小圖。
 */
export function AssigneeField({ value, onChange, isToolbar }: AssigneeFieldProps) {
  const { detail } = useProjectTable();
  const user = useCurrentUser();
  const others = detail.members.filter((member) => member.personId !== user.personId);
  const options: SelectOption[] = [
    { value: '', label: '所有人', leading: <EveryoneMark /> },
    { value: 'me', label: '我', leading: <PersonMark name={user.displayName} /> },
    { value: 'unassigned', label: '沒有負責人', leading: <NobodyMark /> },
    ...others.map((member, index) => personOption(member, index === 0)),
  ];
  return (
    <SelectField
      {...toolbarFieldProps(isToolbar)}
      label="負責人"
      value={value ?? ''}
      options={options}
      onChange={(next) => {
        onChange(next || null);
      }}
    />
  );
}

interface TypeStateFieldsProps extends ToolbarFieldProps {
  /** 類型（多於一種才有）、狀態：依序收進「更多條件」時各自可以不畫（inlineConditions.ts）。預設都畫。 */
  readonly hasType?: boolean;
  readonly hasState?: boolean;
  readonly filters: { readonly type: string | null; readonly state: string | null };
  readonly onChange: (patch: { readonly type: string | null; readonly state: string | null }) => void;
}

/** 類型（多於一種才有）與狀態。表格、行事曆與時間軸共用；選項的文字帶出意思（「所有類型」），工具列上的標籤才能視覺上隱藏。 */
export function TypeStateFields({ filters, onChange, isToolbar, hasType = true, hasState = true }: TypeStateFieldsProps) {
  const { detail } = useProjectTable();
  const fieldProps = toolbarFieldProps(isToolbar);
  const { itemTypes } = detail.blueprint;
  const states = stateFilterChoices(detail.blueprint, filters.type);

  function changeType(type: string | null) {
    // 換了類型以後，原本選的狀態可能不在新類型的流程裡，就不要留著篩出空的結果。
    const nextStates = filterableStates(detail.blueprint, type);
    const state = nextStates.some((candidate) => candidate.key === filters.state) ? filters.state : null;
    onChange({ type, state });
  }

  // 「所有類型」「所有狀態」本身就是一個選擇（不篩選），是真的選項，不是 placeholder（規範 7.3）。
  // 狀態的選項前面是類別的形狀；「所有狀態」不是一個狀態，用一樣大的短線，字才對齊。類型不放小圖。
  const typeOptions = [{ value: '', label: '所有類型' }, ...itemTypes.map((itemType) => ({ value: itemType.key, label: itemType.name }))];
  // 沒選類型而專案有不只一個流程時，狀態依流程分組、組名是用它的類型（stateChoices.ts）。
  const stateChoices = [{ value: '', label: '所有狀態', leading: <AnyStateMark /> }, ...states.options];
  return (
    <>
      {hasType && itemTypes.length > 1 && (
        <SelectField {...fieldProps} label="類型" value={filters.type ?? ''} options={typeOptions} onChange={(type) => { changeType(type || null); }} />
      )}
      {hasState && (
        <SelectField {...fieldProps} label="狀態" value={filters.state ?? ''} options={stateChoices} onChange={(state) => { onChange({ type: filters.type, state: states.stateKeyOf(state) ?? null }); }} />
      )}
    </>
  );
}

/** 篩選的條件：類型、狀態、負責人、到期、優先度、週期、里程碑、自訂欄位（工具列上的順序，也是「更多條件」浮層裡的順序）。 */
export type ConditionKey = CollapsibleCondition | MoreOnlyCondition;

export const ALL_CONDITIONS: ReadonlySet<ConditionKey> = new Set<ConditionKey>(['type', 'state', 'assignee', 'due', 'priority', 'cycle', 'milestone', 'fields']);

export type ConditionValues = Pick<ItemFilters, 'type' | 'state' | 'assignee' | 'due' | 'priority' | 'cycle' | 'milestone' | 'fields'>;

interface ConditionFieldsProps extends ToolbarFieldProps {
  readonly filters: ConditionValues;
  readonly onChange: (patch: Partial<ConditionValues>) => void;
  /** 畫哪些條件，一律照固定的順序。類型只有多於一種才有，週期與里程碑專案沒有的那一個不畫。 */
  readonly keys: ReadonlySet<ConditionKey>;
}

/**
 * 條件那一組下拉，表格、看板、行事曆、時間軸與「更多條件」「篩選」浮層共用：由 keys 決定畫哪幾個，
 * 列上留幾個、浮層裡放幾個都是同一份順序（inlineConditions.ts）。
 */
export function ConditionFields({ filters, onChange, isToolbar, keys }: ConditionFieldsProps) {
  const { hasCycles, hasMilestones } = usePlanningVisibility(filters.cycle, filters.milestone);
  return (
    <>
      {(keys.has('type') || keys.has('state')) && (
        <TypeStateFields
          isToolbar={isToolbar}
          hasType={keys.has('type')}
          hasState={keys.has('state')}
          filters={filters}
          onChange={onChange}
        />
      )}
      {keys.has('assignee') && <AssigneeField isToolbar={isToolbar} value={filters.assignee} onChange={(assignee) => { onChange({ assignee }); }} />}
      {keys.has('due') && <DueField isToolbar={isToolbar} value={filters.due} onChange={(due) => { onChange({ due }); }} />}
      {keys.has('priority') && <PriorityField isToolbar={isToolbar} value={filters.priority} onChange={(priority) => { onChange({ priority }); }} />}
      {keys.has('cycle') && hasCycles && <CycleField isToolbar={isToolbar} value={filters.cycle} onChange={(cycle) => { onChange({ cycle }); }} />}
      {keys.has('milestone') && hasMilestones && (
        <MilestoneField isToolbar={isToolbar} value={filters.milestone} onChange={(milestone) => { onChange({ milestone }); }} />
      )}
      {keys.has('fields') && <FieldFilterFields isToolbar={isToolbar} fields={filters.fields} onChange={(fields) => { onChange({ fields }); }} />}
    </>
  );
}

interface MoreConditionsProps {
  readonly filters: ConditionValues;
  readonly onChange: (patch: Partial<ConditionValues>) => void;
  /** 收進這裡的條件（照列上原本的順序畫）：依序收起來的欄位，加上週期與里程碑。 */
  readonly keys: ReadonlySet<ConditionKey>;
  /** 放在條件前面的控制項：看板放不下時收進來的「分組」（它不是條件，不算進數字）。 */
  readonly leading?: ReactNode;
}

/** 這幾個條件裡有值的數目；自訂欄位每一個有值的欄位算一個。 */
function moreConditionCount(filters: ConditionValues, keys: ReadonlySet<ConditionKey>): number {
  return [...keys].reduce((sum, key) => {
    if (key === 'fields') return sum + Object.keys(filters.fields).length;
    return filters[key] === null ? sum : sum + 1;
  }, 0);
}

/**
 * 工具列上比較少用的條件收進這一顆（inlineConditions.ts）：週期與里程碑一律在這裡，放不下的欄位依序也在這裡。
 * 打開是標籤看得到的同樣幾個下拉，選了就換、浮層留著。按鈕上的數字是這裡面有值的條件，和「篩選」同一套（完整的說法在 aria-describedby）。
 */
export function MoreConditions({ filters, onChange, keys, leading }: MoreConditionsProps) {
  const count = moreConditionCount(filters, keys);
  return (
    <FilterPopover label="更多條件" hasIcon={false} count={count} groupLabel="更多條件">
      {leading}
      <ConditionFields filters={filters} onChange={onChange} keys={keys} />
    </FilterPopover>
  );
}
