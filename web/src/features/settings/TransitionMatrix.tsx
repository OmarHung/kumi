import type { WorkflowState } from '../../api/blueprint';
import { Button } from '../../components/Button/Button';
import type { WorkflowDraft } from '../../domain/settingsDraft';
import { findTransition, setTransition, transitionStatus, type TransitionStatus } from '../../domain/settingsTransitions';
import { cx } from '../../lib/cx';
import { elementId, issueBinding, useSettingsEditor } from './SettingsContext';
import { limitsText, pairName, stateLabel, transitionLimits, workflowNameSuffix, type TransitionLimits } from './transitionLabels';
import styles from './Transitions.module.css';

/** 格子上還沒送出的修改：顏色之外一定有文字。 */
const STATUS_TEXT: Readonly<Record<Exclude<TransitionStatus, 'unchanged'>, string>> = {
  added: '新增',
  modified: '已修改',
  removed: '要刪除',
};

const CELL_CLASSES: Readonly<Record<TransitionStatus, string | undefined>> = {
  unchanged: undefined,
  added: styles.cellAdded,
  modified: styles.cellModified,
  removed: styles.cellRemoved,
};

export interface EditingTransition {
  readonly from: WorkflowState;
  readonly to: WorkflowState;
}

interface CellTagsProps {
  readonly limits: TransitionLimits;
  readonly status: TransitionStatus;
  readonly statusId: string;
  readonly limitsId: string;
  readonly isInvalid: boolean;
}

/**
 * 看得出有限制（「角色」「必填」，完整的說法給螢幕報讀器）、還沒送出的修改與問題。
 * 「有問題」不只靠紅框；勾選框已經有 aria-invalid 與問題的說明，這裡對螢幕報讀器藏起來免得唸兩次。
 */
function CellTags({ limits, status, statusId, limitsId, isInvalid }: CellTagsProps) {
  const text = limitsText(limits);
  return (
    <div className={styles.cellTags}>
      {isInvalid && (
        <span aria-hidden="true" className={styles.cellIssue}>
          有問題
        </span>
      )}
      {limits.roles.length > 0 && <span aria-hidden="true">角色</span>}
      {limits.fields.length > 0 && <span aria-hidden="true">必填</span>}
      {status !== 'unchanged' && (
        <span id={statusId} className={styles.cellStatus}>
          {STATUS_TEXT[status]}
        </span>
      )}
      {text !== '' && (
        <span id={limitsId} className="visually-hidden">
          {text}
        </span>
      )}
    </div>
  );
}

interface CellProps {
  readonly workflow: WorkflowDraft;
  readonly from: WorkflowState;
  readonly to: WorkflowState;
  readonly onEdit: (transition: EditingTransition) => void;
}

function TransitionCell({ workflow, from, to, onEdit }: CellProps) {
  const editor = useSettingsEditor();
  const target = { kind: 'transition', workflow: workflow.key, from: from.key, to: to.key } as const;
  const transition = findTransition(workflow.transitions, from.key, to.key);
  const status = transitionStatus(editor.base, editor.draft, workflow.key, from.key, to.key);
  const binding = issueBinding(editor, target);
  const limits = transition === undefined ? { roles: [], fields: [] } : transitionLimits(editor.draft, transition);
  const statusId = elementId(editor.idPrefix, target, 'status');
  const limitsId = elementId(editor.idPrefix, target, 'limits');
  const limitText = limitsText(limits);
  const describedBy = [status === 'unchanged' ? '' : statusId, limitText === '' ? '' : limitsId, binding.describedBy ?? '']
    .filter((id) => id !== '')
    .join(' ');
  const pair = pairName(editor.base, workflow.key, from.key, to.key);
  const suffix = workflowNameSuffix(editor.base, workflow.key);
  const toggle = (isAllowed: boolean) => {
    editor.update((draft) => setTransition(editor.base, draft, workflow.key, from.key, to.key, isAllowed));
  };
  return (
    <td className={cx(styles.cell, CELL_CLASSES[status], binding.isInvalid && styles.cellInvalid)}>
      <div className={styles.cellBody}>
        {/* 整個 label 是點擊範圍（至少 --target-min 見方，觸控 44px）；名稱照舊是勾選框的 aria-label。 */}
        <label className={styles.cellToggle}>
          <input
            type="checkbox"
            aria-label={`${pair}${suffix}`}
            checked={transition !== undefined}
            aria-invalid={binding.isInvalid || undefined}
            aria-describedby={describedBy === '' ? undefined : describedBy}
            onChange={(event) => { toggle(event.target.checked); }}
          />
        </label>
        {transition !== undefined && (
          <Button size="sm" variant="ghost" className={styles.cellButton} aria-label={`設定${pair}的限制${suffix}`} onClick={() => { onEdit({ from, to }); }}>
            設定
          </Button>
        )}
      </div>
      <CellTags limits={limits} status={status} statusId={statusId} limitsId={limitsId} isInvalid={binding.isInvalid} />
    </td>
  );
}

interface TransitionMatrixProps {
  readonly workflow: WorkflowDraft;
  readonly name: string;
  readonly onEdit: (transition: EditingTransition) => void;
}

/**
 * 「從 → 到」的表格：列是起點、欄是終點（照草稿的狀態順序），勾起來的可以走；自己到自己不能勾。
 * 狀態多時表格自己橫向捲動（第一欄固定），整頁不會被撐寬。
 */
export function TransitionMatrix({ workflow, name, onEdit }: TransitionMatrixProps) {
  return (
    // 捲動的區域要能用鍵盤停下來捲動，所以給 tabIndex 與名稱。
    <div className={styles.scroll} role="region" aria-label={`「${name}」的轉換`} tabIndex={0}>
      <table className={styles.matrix}>
        <caption className="visually-hidden">{`「${name}」的轉換：列是起點，欄是終點`}</caption>
        <thead>
          <tr>
            <th scope="col" className={styles.corner}>
              <span aria-hidden="true">從 ＼ 到</span>
              <span className="visually-hidden">起點（列）到終點（欄）</span>
            </th>
            {workflow.states.map((state) => (
              <th key={state.key} scope="col" className={styles.columnHeader}>
                {stateLabel(workflow.states, state.key)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {workflow.states.map((from) => (
            <tr key={from.key}>
              <th scope="row" className={styles.rowHeader}>
                {stateLabel(workflow.states, from.key)}
              </th>
              {workflow.states.map((to) =>
                to.key === from.key ? (
                  // 自己到自己：淡底就看得出來，不畫符號；說法只給報讀軟體。
                  <td key={to.key} className={styles.diagonal}>
                    <span className="visually-hidden">不能轉到自己</span>
                  </td>
                ) : (
                  <TransitionCell key={to.key} workflow={workflow} from={from} to={to} onEdit={onEdit} />
                ),
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
