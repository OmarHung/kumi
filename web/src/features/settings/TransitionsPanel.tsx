import { useEffect, useRef, useState } from 'react';
import type { Workflow } from '../../api/blueprint';
import type { WorkflowDraft } from '../../domain/settingsDraft';
import { findTransition, isRestrictionChanged, setRestricted, visibleTransitions } from '../../domain/settingsTransitions';
import { CheckboxField } from '../../components/CheckboxField/CheckboxField';
import { IssueText, StatusBadge } from './RowParts';
import { elementId, issueBinding, useSettingsEditor } from './SettingsContext';
import { TransitionDialog } from './TransitionDialog';
import { limitsText, stateLabel, transitionLimits } from './transitionLabels';
import { TransitionMatrix, type EditingTransition } from './TransitionMatrix';
import settingsStyles from './Settings.module.css';
import styles from './Transitions.module.css';

const OPEN_HINT = '不限制：任何兩個狀態之間都可以互轉。';
const RESTRICTED_HINT =
  '限制：只有勾起來的「從 → 到」可以走。每個狀態都要能從起始狀態走到，完成與已取消以外的狀態都要有可以轉出去的路。';
const READ_ONLY_RESTRICTED_HINT = '限制：只有下面列出的轉換可以走。';

interface PanelProps {
  readonly workflow: WorkflowDraft;
  readonly original: Workflow;
}

function pairText(workflow: WorkflowDraft, from: string, to: string): string {
  return `「${stateLabel(workflow.states, from)}」→「${stateLabel(workflow.states, to)}」`;
}

/** 唯讀時的轉換：一條一行，附上限制。 */
function TransitionList({ workflow, original }: PanelProps) {
  const editor = useSettingsEditor();
  const transitions = visibleTransitions(workflow) ?? [];
  if (transitions.length === 0) {
    return <p className={settingsStyles.note}>一條轉換都沒有。</p>;
  }
  return (
    <ul className={styles.readOnlyList} aria-label={`「${original.name}」的轉換`}>
      {transitions.map((transition) => {
        const limits = limitsText(transitionLimits(editor.draft, transition));
        return (
          <li key={`${transition.from}->${transition.to}`}>
            {pairText(workflow, transition.from, transition.to)}
            {limits !== '' && <span className={settingsStyles.note}>{`（${limits}）`}</span>}
          </li>
        );
      })}
    </ul>
  );
}

/**
 * 表格下面的問題：每一對有問題的「從 → 到」一段（照表格的順序），id 給那一格的勾選框的 aria-describedby。
 * 走過每一對、不只勾著的：伺服器標在一條轉換上的問題，使用者取消那一格之後還要看得到，勾選框的說明也還連得到。
 */
function TransitionIssues({ workflow }: { readonly workflow: WorkflowDraft }) {
  const editor = useSettingsEditor();
  const entries = workflow.states.flatMap((from) =>
    workflow.states.flatMap((to) => {
      const binding = issueBinding(editor, { kind: 'transition', workflow: workflow.key, from: from.key, to: to.key });
      return from.key !== to.key && binding.isInvalid ? [{ pair: pairText(workflow, from.key, to.key), binding }] : [];
    }),
  );
  if (entries.length === 0) {
    return null;
  }
  return (
    <div className={settingsStyles.issues}>
      {entries.map(({ pair, binding }) => (
        <div key={binding.errorId} id={binding.errorId}>
          {binding.messages.map((message, index) => (
            // 同一條轉換的問題照伺服器給的順序，不會重排。
            <IssueText key={index}>{`${pair}：${message}`}</IssueText>
          ))}
        </div>
      ))}
    </div>
  );
}

/**
 * 正在設定限制的那條轉換。草稿被背景重抓換成新的一版、這條轉換不在了時，對話框關上；
 * 打開它的「設定」也跟著不見，焦點等畫面更新完放到「限制轉換」，不會掉到頁面本體。
 */
function useEditingTransition(workflow: WorkflowDraft) {
  const [editing, setEditing] = useState<EditingTransition | null>(null);
  const [goneCount, setGoneCount] = useState(0);
  const toggleRef = useRef<HTMLInputElement>(null);
  if (editing !== null && findTransition(workflow.transitions, editing.from.key, editing.to.key) === undefined) {
    setEditing(null);
    setGoneCount((count) => count + 1);
  }
  useEffect(() => {
    if (goneCount > 0) toggleRef.current?.focus();
  }, [goneCount]);
  return { editing, setEditing, toggleRef };
}

/** 每個流程的轉換：限制的開關與說明，有限制時是「從 → 到」的表格，格子裡的「設定」打開限制的對話框。 */
export function TransitionsPanel({ workflow, original }: PanelProps) {
  const editor = useSettingsEditor();
  const { editing, setEditing, toggleRef } = useEditingTransition(workflow);
  const isRestricted = workflow.transitions !== undefined;
  const target = { kind: 'workflow', workflow: workflow.key } as const;
  const headingId = elementId(editor.idPrefix, target, 'transitions');
  const hintId = elementId(editor.idPrefix, target, 'transitions-hint');
  const toggle = (next: boolean) => {
    editor.update((draft) => setRestricted(editor.base, draft, workflow.key, next));
  };
  return (
    <div className={styles.panel}>
      <div className={styles.panelHeader}>
        <h4 id={headingId} className={styles.panelTitle}>
          轉換
        </h4>
        {editor.canEdit && (
          <CheckboxField
            ref={toggleRef}
            className={styles.toggle}
            checked={isRestricted}
            aria-describedby={hintId}
            onChange={(event) => { toggle(event.target.checked); }}
            label={
              <>
                限制轉換<span className="visually-hidden">{`（${original.name}）`}</span>
              </>
            }
          />
        )}
        {isRestrictionChanged(editor.base, workflow) && <StatusBadge status="modified" />}
      </div>
      <p id={hintId} className={settingsStyles.note}>
        {isRestricted ? (editor.canEdit ? RESTRICTED_HINT : READ_ONLY_RESTRICTED_HINT) : OPEN_HINT}
      </p>
      {isRestricted && editor.canEdit && <TransitionMatrix workflow={workflow} name={original.name} onEdit={setEditing} />}
      {isRestricted && !editor.canEdit && <TransitionList workflow={workflow} original={original} />}
      {isRestricted && <TransitionIssues workflow={workflow} />}
      {editing !== null && (
        <TransitionDialog workflowKey={workflow.key} from={editing.from} to={editing.to} onClose={() => { setEditing(null); }} />
      )}
    </div>
  );
}
