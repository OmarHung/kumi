import { useCallback, useRef, useState, type KeyboardEvent } from 'react';
import { BULK_MAX_ITEMS } from '../../api/itemRecovery';
import type { Member } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { DatePicker } from '../../components/DatePicker/DatePicker';
import { Icon } from '../../components/Icon/Icon';
import { Popover } from '../../components/Popover/Popover';
import { Select, type SelectOption } from '../../components/Select/Select';
import { PRIORITIES } from '../../domain/dueFilter';
import { remInPixels, useElementWidth } from '../../lib/useElementWidth';
import { PRIORITY_LABELS } from '../../domain/labels';
import { isCommitEnter } from '../../lib/keyboard';
import { memberOptions } from '../item-table/cells/CoreCells';
import { usePersonTitles } from '../people/usePersonTitles';
import { useProjectTable, type ProjectAbilities } from '../item-table/ProjectTableContext';
import { usePlanning } from '../item-table/usePlanning';
import { cancelOnEscape } from './cancelOnEscape';
import { collapsedActions, inlineActionKeys, type FieldActionKey } from './barLayout';
import { stateChoicesFor } from './stateChoices';
import type { BulkEdit, BulkPatch } from './useBulkEdit';
import type { ItemSelection } from './useItemSelection';
import styles from './BulkEdit.module.css';

/** 選項的值對不到任何選項：下拉一直顯示欄位的名稱（placeholder），選完不會留著一個「目前的值」。 */
const NO_CURRENT_VALUE = '\u0000';
/** 「沒有負責人」「沒有週期」「沒有里程碑」的選項值（memberOptions 與規劃的選項都用空字串）。 */
const CLEAR_VALUE = '';
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

const PRIORITY_OPTIONS: readonly SelectOption[] = PRIORITIES.map((priority) => ({ value: priority, label: PRIORITY_LABELS[priority] }));

interface ActionProps {
  readonly bulk: BulkEdit;
  /** 超過一次的上限，或正在送出：動作停住（aria-disabled：停得到、焦點不掉）。 */
  readonly isBlocked: boolean;
  readonly describedBy: string | undefined;
}

/** 一個欄位動作：下拉的觸發鈕是欄位的名稱，選了值就送出，不再確認。 */
function FieldPicker({
  label,
  options,
  isBlocked,
  describedBy,
  onPick,
  onOpenChange,
}: {
  readonly label: string;
  readonly options: readonly SelectOption[];
  readonly isBlocked: boolean;
  readonly describedBy: string | undefined;
  readonly onPick: (value: string) => void;
  readonly onOpenChange?: (isOpen: boolean) => void;
}) {
  return (
    <Select
      onOpenChange={onOpenChange}
      aria-label={label}
      aria-describedby={describedBy}
      aria-disabled={isBlocked || undefined}
      className={styles.picker}
      size="sm"
      width="content"
      placeholder={label}
      value={NO_CURRENT_VALUE}
      options={options}
      onChange={onPick}
    />
  );
}

/** 負責人的下拉：選項名稱下面寫職稱（人員清單，和人員頁共用同一份快取）。 */
function AssigneePicker({ members, ...props }: { readonly members: readonly Member[]; readonly isBlocked: boolean; readonly describedBy: string | undefined; readonly onPick: (value: string) => void }) {
  // 打開選單才讀人員清單（規範 8：負責人的選單在打開編輯時才讀）；開過一次就留著，關上再開不閃。
  const [hasOpened, setHasOpened] = useState(false);
  const titles = usePersonTitles(hasOpened);
  const handleOpenChange = useCallback((isOpen: boolean) => {
    if (isOpen) setHasOpened(true);
  }, []);
  return <FieldPicker label="負責人" options={memberOptions(members, '沒有負責人', null, titles)} onOpenChange={handleOpenChange} {...props} />;
}

function sendOrNull(bulk: BulkEdit, build: (value: string | null) => BulkPatch): (value: string) => void {
  return (value) => {
    void bulk.update(build(value === CLEAR_VALUE ? null : value));
  };
}

function planningOptions(rows: readonly { readonly id: string; readonly name: string }[], emptyLabel: string): SelectOption[] {
  return [{ value: CLEAR_VALUE, label: emptyLabel }, ...rows.map((row) => ({ value: row.id, label: row.name }))];
}

/** 到期日浮層：日期欄位（選了、或打好日期按 Enter 就送出）加「清除到期日」。 */
function DueDatePanel({ onPick, onClear }: { readonly onPick: (date: string) => void; readonly onClear: () => void }) {
  const [draft, setDraft] = useState('');
  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (isCommitEnter(event) && ISO_DATE.test(draft)) {
      event.preventDefault();
      onPick(draft);
    }
  };
  return (
    <div className={styles.datePanel}>
      <DatePicker aria-label="到期日" size="sm" value={draft} onChange={setDraft} onPick={onPick} onKeyDown={handleKeyDown} />
      <Button size="sm" onClick={onClear}>
        清除到期日
      </Button>
    </div>
  );
}

/** 刪除不用確認（可以復原）：按了就送出，結果的提示上有「復原」。 */
function DeleteAction({ bulk, isBlocked, describedBy }: ActionProps) {
  return (
    <Button
      variant="ghost"
      size="sm"
      className={styles.remove}
      aria-describedby={describedBy}
      aria-disabled={isBlocked || undefined}
      onClick={() => {
        if (!isBlocked) void bulk.remove();
      }}
    >
      <Icon name="delete" />
      刪除
    </Button>
  );
}


interface ControlProps extends ActionProps {
  readonly selection: ItemSelection;
  /** 收在「更多動作」裡：到期日直接是浮層裡的欄位，不是再開一層浮層的按鈕。 */
  readonly isInMenu: boolean;
}

function DueDateAction({ bulk, isBlocked, describedBy, isInMenu }: ControlProps) {
  const pick = (date: string | null) => {
    void bulk.update({ dueDate: date === '' ? null : date });
  };
  if (isInMenu) {
    return (
      <div role="group" aria-label="到期日" className={styles.menuField}>
        <span className={styles.menuLabel}>到期日</span>
        <DueDatePanel onPick={pick} onClear={() => { pick(null); }} />
      </div>
    );
  }
  return (
    <Popover
      label="到期日"
      renderTrigger={(trigger) => (
        <Button {...trigger} size="sm" aria-haspopup="dialog" aria-describedby={describedBy} aria-disabled={isBlocked || undefined} onClick={isBlocked ? undefined : trigger.onClick}>
          到期日
          <Icon name="chevronDown" />
        </Button>
      )}
    >
      {({ close }) => (
        <DueDatePanel
          onPick={(date) => {
            close();
            pick(date);
          }}
          onClear={() => {
            close();
            pick(null);
          }}
        />
      )}
    </Popover>
  );
}

/** 一個欄位動作（列上或「更多動作」裡長得一樣：下拉，名稱是欄位的名稱）。 */
function FieldControl({ name, ...props }: ControlProps & { readonly name: FieldActionKey }) {
  const { detail } = useProjectTable();
  const { cycles, milestones } = usePlanning(detail.project.key);
  const { bulk, isBlocked, describedBy, selection } = props;
  const common = { isBlocked, describedBy };
  switch (name) {
    case 'state': {
      const choices = stateChoicesFor(detail.blueprint, selection.selectedItems);
      return (
        <FieldPicker
          label="狀態"
          options={choices.options}
          {...common}
          onPick={(value) => {
            const state = choices.stateKeyOf(value);
            if (state !== undefined) void bulk.update({ state });
          }}
        />
      );
    }
    case 'assignee':
      return <AssigneePicker members={detail.members} {...common} onPick={sendOrNull(bulk, (assigneeId) => ({ assigneeId }))} />;
    case 'priority':
      return (
        <FieldPicker
          label="優先度"
          options={PRIORITY_OPTIONS}
          {...common}
          onPick={(value) => {
            const priority = PRIORITIES.find((candidate) => candidate === value);
            if (priority !== undefined) void bulk.update({ priority });
          }}
        />
      );
    case 'due':
      return <DueDateAction {...props} />;
    case 'cycle':
      return <FieldPicker label="週期" options={planningOptions(cycles, '沒有週期')} {...common} onPick={sendOrNull(bulk, (cycleId) => ({ cycleId }))} />;
    case 'milestone':
      return <FieldPicker label="里程碑" options={planningOptions(milestones, '沒有里程碑')} {...common} onPick={sendOrNull(bulk, (milestoneId) => ({ milestoneId }))} />;
  }
}

/**
 * 放不下的欄位動作收在這顆「更多動作」（浮層，不是選單：每個動作要選值，浮層裡就是同樣的下拉與日期欄位）。
 * 選了值就送出、浮層收起來，焦點回到這顆按鈕。
 */
function MoreActions({ keys, ...props }: ControlProps & { readonly keys: readonly FieldActionKey[] }) {
  const { isBlocked, describedBy } = props;
  return (
    <Popover
      label="更多動作"
      renderTrigger={(trigger) => (
        <Button {...trigger} size="sm" aria-haspopup="dialog" aria-describedby={describedBy} aria-disabled={isBlocked || undefined} onClick={isBlocked ? undefined : trigger.onClick}>
          更多動作
          <Icon name="chevronDown" />
        </Button>
      )}
    >
      {({ close }) => {
        const bulk: BulkEdit = {
          ...props.bulk,
          update: (patch) => {
            close();
            return props.bulk.update(patch);
          },
        };
        return (
          <div className={styles.menuPanel}>
            {keys.map((key) => (
              <FieldControl key={key} name={key} {...props} bulk={bulk} isInMenu />
            ))}
          </div>
        );
      }}
    </Popover>
  );
}

function presentActions(can: ProjectAbilities, hasCycles: boolean, hasMilestones: boolean): readonly FieldActionKey[] {
  return [
    ...(can.transition ? (['state'] as const) : []),
    ...(can.update ? (['assignee', 'priority', 'due'] as const) : []),
    ...(can.update && hasCycles ? (['cycle'] as const) : []),
    ...(can.update && hasMilestones ? (['milestone'] as const) : []),
  ];
}

interface BulkActionBarProps {
  readonly selection: ItemSelection;
  readonly bulk: BulkEdit;
}

/**
 * 批次動作列（docs/DESIGN.md 8.3「批次操作」）：選了至少一個項目時換掉工具列，同一個位置、同樣高度，**永遠一行**。
 * 動作看權限（狀態要 item.transition、欄位要 item.update、刪除要 item.delete）；選了超過一次的上限時動作都停住並說明。
 * 列放不下時欄位動作依序收進「更多動作」（barLayout.ts）；「刪除」與「取消選取」一直留在列上。量不到寬度時全部排在列上。
 */
export function BulkActionBar({ selection, bulk }: BulkActionBarProps) {
  const { can, detail } = useProjectTable();
  const { cycles, milestones } = usePlanning(detail.project.key);
  const barRef = useRef<HTMLDivElement>(null);
  const width = useElementWidth(barRef);
  const isOver = selection.count > BULK_MAX_ITEMS;
  const isBlocked = isOver || bulk.isPending;
  const hintId = isOver ? 'bulk-limit-hint' : undefined;
  const props = { bulk, isBlocked, describedBy: hintId, selection, isInMenu: false };
  const present = presentActions(can, cycles.length > 0, milestones.length > 0);
  const inline = inlineActionKeys(width === null ? null : width / remInPixels(), present, { hasDelete: can.delete, hasNote: isBlocked });
  const collapsed = collapsedActions(inline, present);
  return (
    <div
      ref={barRef}
      role="toolbar"
      aria-label="批次操作"
      className={styles.bar}
      onKeyDown={(event) => {
        cancelOnEscape(event, selection.cancel);
      }}
    >
      <div className={styles.group}>
        <span className={styles.count} aria-live="polite">{`已選 ${String(selection.count)} 個`}</span>
        {present.filter((key) => inline.has(key)).map((key) => (
          <FieldControl key={key} name={key} {...props} />
        ))}
        {collapsed.length > 0 && <MoreActions keys={collapsed} {...props} />}
        {can.delete && <DeleteAction {...props} />}
        {isOver && (
          <span id={hintId} className={styles.hint}>
            {`一次最多 ${String(BULK_MAX_ITEMS)} 個`}
          </span>
        )}
        {bulk.isPending && <span className={styles.busy}>處理中…</span>}
      </div>
      <Button variant="ghost" size="sm" onClick={selection.cancel}>
        取消選取
      </Button>
    </div>
  );
}
