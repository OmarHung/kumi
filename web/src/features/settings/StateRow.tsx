import type { StateCategory, WorkflowState } from '../../api/blueprint';
import { Badge } from '../../components/Badge/Badge';
import { IconButton } from '../../components/IconButton/IconButton';
import { Notice } from '../../components/Notice/Notice';
import { Select } from '../../components/Select/Select';
import { STATE_CATEGORY_LABELS } from '../../domain/labels';
import { stateAutomationReminder } from '../../domain/settingsAutomations';
import { moveState, removeState, restoreState, setMoveTarget, updateState } from '../../domain/settingsDraft';
import { displayName } from '../../domain/settingsNames';
import { stateStatus, type RemovedState } from '../../domain/settingsStatus';
import { cx } from '../../lib/cx';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { CATEGORY_OPTIONS, stateOptions } from '../work-items/selectOptionMarks';
import { IssueMessages, ROW_CLASSES, StatusBadge } from './RowParts';
import { elementId, issueBinding, useSettingsEditor, type SettingsEditorValue } from './SettingsContext';
import controls from './SettingsControls.module.css';
import styles from './Settings.module.css';

function stateTarget(workflow: string, state: string) {
  return { kind: 'state', workflow, state } as const;
}

/** 列上的名稱用目前藍圖裡的（正在改的名字一直變，操作的名稱跟著變會讓人搞不清楚）；新的狀態用 key。 */
function originalName(editor: SettingsEditorValue, workflowKey: string, state: WorkflowState): string {
  const workflow = editor.base.workflows.find((candidate) => candidate.key === workflowKey);
  return workflow?.states.find((candidate) => candidate.key === state.key)?.name ?? state.key;
}

interface StateRowProps {
  readonly workflowKey: string;
  readonly state: WorkflowState;
  readonly index: number;
  readonly count: number;
  readonly addButtonId: string;
}

function StateActions({ workflowKey, state, index, count, addButtonId }: StateRowProps) {
  const editor = useSettingsEditor();
  const name = `「${originalName(editor, workflowKey, state)}」`;
  const target = stateTarget(workflowKey, state.key);
  const upId = elementId(editor.idPrefix, target, 'up');
  const downId = elementId(editor.idPrefix, target, 'down');
  const move = (direction: -1 | 1) => {
    // 按鈕跟著這一列移動；到頭停用時換到另一顆。
    const ids = direction < 0 ? [upId, downId] : [downId, upId];
    updateThenFocus(() => { editor.update((draft) => moveState(draft, workflowKey, state.key, direction)); }, ids);
  };
  const remove = () => {
    // 原本就有的狀態換到「要刪除」那一列，接著要選項目搬去哪裡；剛新增的直接不見，回到「新增狀態」。
    const ids = [elementId(editor.idPrefix, target, 'move'), addButtonId];
    updateThenFocus(() => { editor.update((draft) => removeState(editor.base, draft, workflowKey, state.key)); }, ids);
  };
  return (
    <div className={styles.actions}>
      <IconButton id={upId} icon="arrowUp" size="sm" disabled={index === 0} label={`把${name}往上移`} onClick={() => { move(-1); }} />
      <IconButton id={downId} icon="arrowDown" size="sm" disabled={index === count - 1} label={`把${name}往下移`} onClick={() => { move(1); }} />
      <IconButton id={elementId(editor.idPrefix, target, 'delete')} icon="delete" size="sm" label={`刪除${name}`} onClick={remove} />
    </div>
  );
}

type StateFieldProps = Pick<StateRowProps, 'workflowKey' | 'state'>;

function useStateChange({ workflowKey, state }: StateFieldProps) {
  const editor = useSettingsEditor();
  return (patch: Parameters<typeof updateState>[3]) => {
    editor.update((draft) => updateState(draft, workflowKey, state.key, patch));
  };
}

function StateName({ workflowKey, state }: StateFieldProps) {
  const editor = useSettingsEditor();
  const change = useStateChange({ workflowKey, state });
  const binding = issueBinding(editor, stateTarget(workflowKey, state.key));
  if (!editor.canEdit) {
    return <span className={styles.name}>{state.name}</span>;
  }
  return (
    <input
      className={controls.input}
      aria-label={`「${originalName(editor, workflowKey, state)}」的名稱`}
      value={state.name}
      aria-invalid={binding.isInvalid || undefined}
      aria-describedby={binding.describedBy}
      onChange={(event) => { change({ name: event.target.value }); }}
    />
  );
}

function StateCategoryField({ workflowKey, state }: StateFieldProps) {
  const editor = useSettingsEditor();
  const change = useStateChange({ workflowKey, state });
  if (!editor.canEdit) {
    return <span className={styles.category}>{STATE_CATEGORY_LABELS[state.category]}</span>;
  }
  const label = `「${originalName(editor, workflowKey, state)}」的類別`;
  return (
    <Select
      size="sm"
      width="content"
      aria-label={label}
      value={state.category}
      options={CATEGORY_OPTIONS}
      onChange={(category) => { change({ category: category as StateCategory }); }}
    />
  );
}

/** 流程裡的一個狀態：順序、名稱、key、類別、還沒送出的修改，與上移、下移、刪除。 */
export function StateRow(props: StateRowProps) {
  const { workflowKey, state, index } = props;
  const editor = useSettingsEditor();
  const status = stateStatus(editor.base, editor.draft, workflowKey, state.key);
  const binding = issueBinding(editor, stateTarget(workflowKey, state.key));
  return (
    <li className={cx(styles.row, ROW_CLASSES[status])}>
      <span className={styles.order} aria-hidden="true">
        {index + 1}
      </span>
      {/* 名稱是第一格，其餘放在 details：窄螢幕時名稱與動作一行，details 一行。 */}
      <div className={styles.cells}>
        <StateName workflowKey={workflowKey} state={state} />
        <span className={styles.details}>
          <StateCategoryField workflowKey={workflowKey} state={state} />
          <span className={styles.key}>{state.key}</span>
          {index === 0 && <Badge>起始</Badge>}
          <StatusBadge status={status} />
        </span>
      </div>
      {editor.canEdit && <StateActions {...props} />}
      <IssueMessages binding={binding} />
    </li>
  );
}

interface RemovedStateRowProps {
  readonly workflowKey: string;
  readonly entry: RemovedState;
}

interface MoveSelectProps extends RemovedStateRowProps {
  /** 自動化提醒的 id；沒有提醒時是空字串。 */
  readonly automationId: string;
}

/**
 * 停在要刪除的狀態的項目搬去哪裡。刪除後焦點到這裡：自動化的提醒也連過來，螢幕報讀器才唸得到。
 * 看得到的「項目搬到」是指到按鈕的 label（點了打開清單）；不包住下拉：清單在按鈕旁邊的 DOM 裡，
 * 包在 label 裡的話點選項會再按一次按鈕、清單又打開。名稱是 aria-label（帶狀態的名稱）。
 * 還沒選時按鈕上是灰字的「選擇狀態」（placeholder，和類型換流程的搬移同一種做法）：必選，選了之後只能換成別的狀態。
 */
function RemovedStateMove({ workflowKey, entry, automationId }: MoveSelectProps) {
  const editor = useSettingsEditor();
  const { state, to } = entry;
  const target = stateTarget(workflowKey, state.key);
  const id = elementId(editor.idPrefix, target, 'move');
  const binding = issueBinding(editor, target);
  const remaining = editor.draft.workflows.find((workflow) => workflow.key === workflowKey)?.states ?? [];
  const describedBy = [automationId, binding.describedBy ?? ''].filter((part) => part !== '').join(' ');
  return (
    <span className={styles.moveField}>
      <label htmlFor={id}>項目搬到</label>
      <Select
        id={id}
        size="sm"
        width="content"
        aria-label={`停在「${state.name}」的項目搬到`}
        value={to}
        placeholder="選擇狀態"
        options={stateOptions(remaining, displayName)}
        aria-invalid={binding.isInvalid || undefined}
        aria-describedby={describedBy === '' ? undefined : describedBy}
        onChange={(next) => { editor.update((draft) => setMoveTarget(draft, workflowKey, state.key, next)); }}
      />
    </span>
  );
}

/** 要刪除的狀態：選停在這裡的項目搬去哪裡（同一個流程裡還在的狀態），或復原。 */
export function RemovedStateRow({ workflowKey, entry }: RemovedStateRowProps) {
  const editor = useSettingsEditor();
  const { state } = entry;
  const target = stateTarget(workflowKey, state.key);
  const automation = stateAutomationReminder(editor.base, editor.draft, state.key);
  const automationId = elementId(editor.idPrefix, target, 'automation');
  const restore = () => {
    updateThenFocus(() => { editor.update((draft) => restoreState(editor.base, draft, workflowKey, state.key)); }, [elementId(editor.idPrefix, target, 'delete')]);
  };
  return (
    <li className={cx(styles.row, styles.rowRemoved)}>
      <span className={styles.order} aria-hidden="true" />
      <div className={styles.cells}>
        <span className={cx(styles.name, styles.removedName)}>{state.name}</span>
        <span className={styles.details}>
          <RemovedStateMove workflowKey={workflowKey} entry={entry} automationId={automation === '' ? '' : automationId} />
          <span className={styles.key}>{state.key}</span>
          <StatusBadge status="removed" />
        </span>
      </div>
      <div className={styles.actions}>
        <IconButton icon="restore" size="sm" label={`復原「${state.name}」`} onClick={restore} />
      </div>
      {automation !== '' && (
        <Notice id={automationId} tone="warning" role="none" className={styles.rowNotice}>
          {automation}
        </Notice>
      )}
      <IssueMessages binding={issueBinding(editor, target)} />
    </li>
  );
}
