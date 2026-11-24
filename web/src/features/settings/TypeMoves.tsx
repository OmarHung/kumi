import { Select } from '../../components/Select/Select';
import type { ItemTypeDraft } from '../../domain/settingsDraft';
import { displayName } from '../../domain/settingsNames';
import { coveredTypeMoves, requiredTypeMoves, setTypeMoveTarget, type CoveredMove, type TypeMove } from '../../domain/settingsTypes';
import { stateOptions } from '../work-items/selectOptionMarks';
import { IssueMessages } from './RowParts';
import { elementId, issueBinding, useSettingsEditor, type SettingsEditorValue } from './SettingsContext';
import styles from './Settings.module.css';
import moveStyles from './TypeMoves.module.css';

/**
 * 類型換流程時要搬的狀態：每個舊狀態一個選單（預先選好，可以改），以及由「刪除狀態」的搬移涵蓋的舊狀態的說明。
 * 選單出現時一直掛著的即時區域說有幾個要確認，螢幕報讀器才知道畫面多了東西。
 */

interface TypeProps {
  readonly type: ItemTypeDraft;
  /** 「類型「需求」」：目前藍圖裡的名稱，新的類型用 key。 */
  readonly label: string;
}

function typeTarget(itemType: string) {
  return { kind: 'itemType', itemType } as const;
}

export function workflowName(editor: SettingsEditorValue, workflowKey: string): string {
  return editor.base.workflows.find((workflow) => workflow.key === workflowKey)?.name ?? workflowKey;
}

function stateName(editor: SettingsEditorValue, workflowKey: string, stateKey: string): string {
  const states = editor.draft.workflows.find((workflow) => workflow.key === workflowKey)?.states ?? [];
  const state = states.find((candidate) => candidate.key === stateKey);
  return state === undefined ? stateKey : displayName(state);
}

function movesOf(editor: SettingsEditorValue, type: ItemTypeDraft): readonly TypeMove[] {
  return requiredTypeMoves(editor.base, editor.draft).filter((move) => move.itemTypes.includes(type.key));
}

function MoveField({ type, label, move }: TypeProps & { readonly move: TypeMove }) {
  const editor = useSettingsEditor();
  const states = editor.draft.workflows.find((workflow) => workflow.key === type.workflow)?.states ?? [];
  const others = move.itemTypes
    .filter((key) => key !== type.key)
    .map((key) => `「${displayName(editor.draft.itemTypes.find((candidate) => candidate.key === key) ?? { key, name: key })}」`);
  const id = elementId(editor.idPrefix, typeTarget(type.key), `move-${move.from.key}`);
  // 問題標在（新流程, 舊狀態）：共用這條搬移的每個類型列上都顯示，各自用自己的 id。
  const binding = issueBinding(editor, { kind: 'typeMove', workflow: move.workflow, from: move.from.key }, `${id}-error`);
  // 看得到的說明是指到按鈕的 label，不包住下拉（理由見 StateRow 的 RemovedStateMove）；名稱是帶類型的 aria-label。
  // 還沒選（不預選的共用搬移）時按鈕上是灰字的「選擇狀態」：必選、選了就不能改回沒選，所以不是選項。
  return (
    <div className={moveStyles.moveEntry}>
      <span className={styles.moveField}>
        <label htmlFor={id}>{`停在「${move.from.name}」的項目搬到`}</label>
        <Select
          id={id}
          size="sm"
          width="content"
          aria-label={`${label}停在「${move.from.name}」的項目搬到`}
          value={move.to}
          placeholder="選擇狀態"
          options={stateOptions(states, displayName)}
          aria-invalid={binding.isInvalid || undefined}
          aria-describedby={binding.describedBy}
          onChange={(to) => { editor.update((draft) => setTypeMoveTarget(draft, move.workflow, move.from.key, to)); }}
        />
        {others.length > 0 && <span className={styles.note}>{`（和${others.join('、')}共用）`}</span>}
      </span>
      <IssueMessages binding={binding} />
    </div>
  );
}

/** 「停在「待辦」的項目照「任務流程」刪除「待辦」的搬移，搬到「審查」」。 */
function coveredText(editor: SettingsEditorValue, covered: CoveredMove): string {
  const lead = `停在「${covered.from.name}」的項目照「${workflowName(editor, covered.workflow)}」刪除「${covered.deleted.name}」的搬移`;
  return covered.to === '' ? `${lead}（還沒選要搬到哪裡）` : `${lead}，搬到「${stateName(editor, covered.workflow, covered.to)}」`;
}

/** 換了流程之後，舊流程有、新流程沒有的狀態：停在那裡的項目要搬到新流程的哪個狀態。 */
export function TypeMoves({ type, label }: TypeProps) {
  const editor = useSettingsEditor();
  const moves = movesOf(editor, type);
  const covered = coveredTypeMoves(editor.base, editor.draft, type.key);
  if ((moves.length === 0 && covered.length === 0) || !editor.canEdit) {
    return null;
  }
  return (
    <div className={moveStyles.typeMoves}>
      {moves.length > 0 && (
        <p className={styles.note}>{`換到「${workflowName(editor, type.workflow)}」之後，原本流程有、新流程沒有的狀態要選項目搬到哪裡：`}</p>
      )}
      {moves.map((move) => (
        <MoveField key={move.from.key} type={type} label={label} move={move} />
      ))}
      {covered.map((entry) => (
        <p key={entry.from.key} className={styles.note}>
          {coveredText(editor, entry)}
        </p>
      ))}
    </div>
  );
}

function announcement(editor: SettingsEditorValue, type: ItemTypeDraft): string {
  const moves = movesOf(editor, type);
  if (moves.length === 0) return '';
  const unchosen = moves.filter((move) => move.to === '').length;
  const lead = `換到「${workflowName(editor, type.workflow)}」之後，有 ${String(moves.length)} 個狀態要選項目搬到哪裡`;
  if (unchosen === 0) return `${lead}（已經預選好，請確認）`;
  return unchosen === moves.length ? `${lead}（請逐一選擇）` : `${lead}（${String(unchosen)} 個要自己選，其他已經預選好，請確認）`;
}

/**
 * 一直掛著（先掛空的）：和選單一起出現的文字，螢幕報讀器常常不唸。
 * 不用 role="status"：每個類型一個，頁面上「已套用」的提示才是唯一的 status。
 */
export function TypeMovesStatus({ type }: { readonly type: ItemTypeDraft }) {
  const editor = useSettingsEditor();
  if (!editor.canEdit) {
    return null;
  }
  return (
    <p aria-live="polite" className="visually-hidden">
      {announcement(editor, type)}
    </p>
  );
}
