import { useState } from 'react';
import { SelectField, type SelectFieldProps } from '../../components/Field/Field';
import { Select, type SelectOption, type SelectProps } from '../../components/Select/Select';
import { EmptyValue, StateBadge } from '../work-items/ItemCells';
import { EveryoneMark, NobodyMark, PersonMark, personOption, stateOptions } from '../work-items/selectOptionMarks';
import { Demo } from './DemoSection';
import styles from './Design.module.css';

const TYPES: readonly SelectOption[] = [
  { value: 'requirement', label: '需求' },
  { value: 'task', label: '任務' },
  { value: 'change_request', label: '需求變更單' },
];

const WORKFLOWS: readonly SelectOption[] = [
  { value: 'delivery', label: '交付流程' },
  { value: 'task_flow', label: '任務流程' },
];

const STATE_LIST = [
  { key: 'backlog', name: '待規劃', category: 'backlog' },
  { key: 'todo', name: '待辦', category: 'not_started' },
  { key: 'doing', name: '進行中', category: 'in_progress' },
  { key: 'done', name: '完成', category: 'done' },
  { key: 'canceled', name: '已取消', category: 'canceled' },
] as const;

// 狀態的選項和表格、設定頁同一份（work-items/selectOptionMarks.tsx）：前面是狀態類別的形狀。
const STATES: readonly SelectOption[] = stateOptions(STATE_LIST);

// 負責人的篩選照工具列的做法：「所有人、我、沒有負責人」一組（佔位的小圖和頭像一樣大），成員一組。
const ASSIGNEES: readonly SelectOption[] = [
  { value: '', label: '所有人', leading: <EveryoneMark /> },
  { value: 'me', label: '我', leading: <PersonMark name="範例使用者" /> },
  { value: 'unassigned', label: '沒有負責人', leading: <NobodyMark /> },
  personOption({ personId: 'member', displayName: '範例成員' }, true),
  personOption({ personId: 'designer', displayName: '範例設計師' }),
];

// 選項裡的原因是短語；完整的句子在清單外面（選單用 aria-describedby 連過去），和成員對話框一樣。
const LAST_ADMIN_SHORT = '最後一位管理員不能換角色';
const LAST_ADMIN = '最後一位管理員：要先把另一位成員設成管理員，才能換掉角色或移出專案。';

const ROLES: readonly SelectOption[] = [
  { value: 'admin', label: '管理員' },
  { value: 'project_manager', label: '專案經理', disabledReason: LAST_ADMIN_SHORT },
  { value: 'editor', label: '編輯者', disabledReason: LAST_ADMIN_SHORT },
  { value: 'viewer', label: '檢視者', disabledReason: LAST_ADMIN_SHORT },
];

const MANY: readonly SelectOption[] = [
  { value: '', label: '沒有上層項目' },
  ...Array.from({ length: 30 }, (_, index) => {
    const number = index + 1;
    return { value: `AGY-${String(number)}`, label: `AGY-${String(number)} 範例需求 ${String(number)}` };
  }),
];

// 一個一個成員拿掉 value、onChange（名稱至少給一個是聯集型別，一般的 Omit 會把它攤平）。
type WithoutValue<P> = P extends unknown ? Omit<P, 'value' | 'onChange'> : never;
type DemoSelectProps = WithoutValue<SelectProps> & { readonly initial: string };

/** 展示用的下拉：自己記著值，選了就換。 */
function DemoSelect({ initial, ...props }: DemoSelectProps) {
  const [value, setValue] = useState(initial);
  return <Select {...props} value={value} onChange={setValue} />;
}

type DemoSelectFieldProps = Omit<SelectFieldProps, 'value' | 'onChange'> & { readonly initial: string };

function DemoSelectField({ initial, ...props }: DemoSelectFieldProps) {
  const [value, setValue] = useState(initial);
  return <SelectField {...props} value={value} onChange={setValue} />;
}

/** 格子裡的狀態：按鈕上是格子平常的樣子（狀態標籤，display），和真的表格一樣沒有箭頭。 */
function DemoCellState() {
  const [value, setValue] = useState('doing');
  const state = STATE_LIST.find((entry) => entry.key === value);
  const display = state === undefined ? null : <StateBadge label={state.name} category={state.category} />;
  return <Select aria-label="格子裡的狀態" variant="bare" size="sm" display={display} value={value} options={STATES} onChange={setValue} />;
}

/** 格子裡的負責人：按鈕上是格子平常的樣子（名字，沒有人時是「沒有值」的短線）。 */
function DemoCellAssignee() {
  const [value, setValue] = useState('member');
  const name = ASSIGNEES.find((option) => option.value === value && option.value !== 'unassigned')?.label;
  const display = name === undefined ? <EmptyValue label="沒有負責人" /> : name;
  return <Select aria-label="格子裡的負責人" variant="bare" size="sm" display={display} value={value} options={ASSIGNEES} onChange={setValue} />;
}

/** Select 的每一種狀態（docs/DESIGN.md 7.3）：兩種大小、有小圖、有不能選的、有分組、沒有值、有錯、停用、選項很多、格子裡的 bare。 */
export function SelectDemos() {
  return (
    <>
      <Demo name="SelectField（options）">
        <DemoSelectField label="類型" initial="task" options={TYPES} />
        <DemoSelectField label="負責人" hint="選項是人員：前面有頭像；「所有人、我、沒有負責人」一組，成員一組。" initial="member" options={ASSIGNEES} />
        <DemoSelectField label="流程" error="先選一個流程。" initial="" placeholder="選擇流程" options={WORKFLOWS} />
        <DemoSelectField label="上層項目" hint="選項很多（10 個以上）：最上面有篩選框，打字就篩選；清單最高 18rem，在清單裡捲動。" initial="AGY-12" options={MANY} />
        <DemoSelectField label="幣別" hint="停用：整顆變淡、打不開。" initial="TWD" disabled options={[{ value: 'TWD', label: 'TWD' }]} />
      </Demo>
      <Demo name="Select size=sm（工具列）">
        <div className={styles.selectRow}>
          <DemoSelectField label="狀態" size="sm" isLabelHidden initial="doing" options={STATES} />
          <DemoSelectField label="篩選負責人" size="sm" isLabelHidden initial="" options={ASSIGNEES} />
          <DemoSelectField label="類型（工具列）" size="sm" isLabelHidden initial="" options={[{ value: '', label: '所有類型' }, ...TYPES]} />
        </div>
      </Demo>
      <Demo name="Select 的其他狀態">
        <DemoSelect aria-label="範例使用者的角色" aria-describedby="design-last-admin" initial="admin" options={ROLES} />
        <p id="design-last-admin" className={styles.note}>
          {LAST_ADMIN}
        </p>
        <DemoSelect aria-label="沒有選項的下拉" initial="" placeholder="選擇人員" options={[]} />
        <DemoSelect aria-label="停得到、打不開的下拉" aria-disabled aria-describedby="design-select-blocked" initial="task" options={TYPES} />
        <span id="design-select-blocked" className={styles.note}>
          aria-disabled：停得到、打不開，原因用 aria-describedby 連過去。
        </span>
        <DemoSelect aria-label="送出中的下拉" aria-busy initial="doing" options={STATES} />
      </Demo>
      <Demo name="Select variant=bare（格子裡）">
        {/*
          模擬編輯中的格子（span：不吃展示頁「一行一個欄位」的 div 規則）：外框由格子畫，下拉本身沒有外框、底色與焦點光暈，
          撐滿格子；按鈕上是格子平常的樣子（display），沒有箭頭（規範 7.3、8.3）。
        */}
        <span className={styles.cellFrame}>
          <DemoCellState />
        </span>
        <span className={styles.cellFrame}>
          <DemoCellAssignee />
        </span>
      </Demo>
    </>
  );
}
