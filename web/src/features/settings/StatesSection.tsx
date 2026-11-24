import { useState } from 'react';
import type { Workflow } from '../../api/blueprint';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import type { WorkflowDraft } from '../../domain/settingsDraft';
import { removedStates } from '../../domain/settingsStatus';
import { AddStateDialog } from './AddStateDialog';
import { IssueMessages } from './RowParts';
import { elementId, issueBinding, useSettingsEditor } from './SettingsContext';
import { RemovedStateRow, StateRow } from './StateRow';
import { sectionHeadingId } from './settingsSections';
import { TransitionsPanel } from './TransitionsPanel';
import styles from './Settings.module.css';

const RESTRICTED_NOTE = '這個流程有限制轉換：刪除狀態時，進出它的轉換會一起拿掉；新增狀態時可以設定從哪些狀態轉進來、可以轉去哪些狀態。';

function WorkflowStates({ workflow, original }: { readonly workflow: WorkflowDraft; readonly original: Workflow }) {
  const editor = useSettingsEditor();
  const [isAdding, setIsAdding] = useState(false);
  const target = { kind: 'workflow', workflow: workflow.key } as const;
  const headingId = elementId(editor.idPrefix, target, 'heading');
  const addButtonId = elementId(editor.idPrefix, target, 'add');
  const removed = removedStates(editor.base, editor.draft, workflow.key);
  return (
    <section className={styles.group} aria-labelledby={headingId}>
      <header className={styles.groupHeader}>
        <h3 id={headingId} className={styles.groupTitle}>
          {original.name}
        </h3>
        {workflow.transitions !== undefined && <p className={styles.note}>{RESTRICTED_NOTE}</p>}
      </header>
      <IssueMessages binding={issueBinding(editor, target)} />
      <ol className={styles.list} aria-label={`「${original.name}」的狀態`}>
        {workflow.states.map((state, index) => (
          <StateRow key={state.key} workflowKey={workflow.key} state={state} index={index} count={workflow.states.length} addButtonId={addButtonId} />
        ))}
      </ol>
      {removed.length > 0 && (
        <ul className={styles.list} aria-label={`「${original.name}」要刪除的狀態`}>
          {removed.map((entry) => (
            <RemovedStateRow key={entry.state.key} workflowKey={workflow.key} entry={entry} />
          ))}
        </ul>
      )}
      {editor.canEdit && (
        <Button id={addButtonId} size="sm" className={styles.addButton} aria-label={`在「${original.name}」新增狀態`} onClick={() => { setIsAdding(true); }}>
          <Icon name="add" />
          新增狀態
        </Button>
      )}
      {isAdding && (
        <AddStateDialog workflow={original} onClose={() => { setIsAdding(false); }} />
      )}
      <TransitionsPanel workflow={workflow} original={original} />
    </section>
  );
}

/** 每個流程一區：狀態照順序排（第一個是新項目的起始狀態），要刪除的列在後面，最後是轉換。 */
export function StatesSection() {
  const editor = useSettingsEditor();
  const headingId = sectionHeadingId(editor.idPrefix, 'workflows');
  return (
    // 設定頁目前這個區塊是第一筆內容（docs/DESIGN.md 6.2）：給 scripts/ui-audit.js 量高度預算。
    <section className={styles.section} aria-labelledby={headingId} data-content-start>
      <div className={styles.sectionHeader}>
        <h2 id={headingId} tabIndex={-1} className={styles.sectionTitle}>
          流程
        </h2>
        <p className={styles.sectionHint}>
          每個流程的狀態照順序排，第一個是新項目的起始狀態。類別決定「我的工作」與報表怎麼算；轉換決定項目可以從哪個狀態改到哪個狀態。
        </p>
      </div>
      {editor.draft.workflows.map((workflow) => {
        const original = editor.base.workflows.find((candidate) => candidate.key === workflow.key);
        return original && <WorkflowStates key={workflow.key} workflow={workflow} original={original} />;
      })}
    </section>
  );
}
