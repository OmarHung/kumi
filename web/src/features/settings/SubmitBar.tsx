import { useId, useRef, useState, type ReactNode } from 'react';
import { Button } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import type { SettingsIssue } from '../../domain/settingsIssues';
import { SUMMARY_MAX_LENGTH } from '../../domain/settingsValidation';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { useIsCompact } from '../../lib/useMediaQuery';
import styles from './SubmitBar.module.css';

const SUMMARY_PLACEHOLDER = '一句話說明這次修改，可以不填';
const SUMMARY_HINT = '一句話說明這次修改，預覽與套用時看得到；可以不填。';

interface SubmitBarProps {
  readonly count: number;
  readonly summary: string;
  readonly onSummaryChange: (summary: string) => void;
  readonly onSubmit: () => void;
  readonly onDiscard: () => void;
  readonly isPending: boolean;
  readonly alert: string | null;
  readonly summaryError: string | undefined;
  /** 對不到設定頁任何一列的問題（例如自動化或檢視裡的引用）。 */
  readonly unplaced: readonly SettingsIssue[];
  /** 問題落在別的區塊時，連到那些區塊（沒有時是 null）。 */
  readonly elsewhere?: ReactNode;
}

function UnplacedIssues({ issues }: { readonly issues: readonly SettingsIssue[] }) {
  if (issues.length === 0) {
    return null;
  }
  return (
    <ul className={styles.unplaced} aria-label="其他問題">
      {issues.map((issue, index) => (
        // 照伺服器給的順序，不會重排。
        <li key={index}>{issue.message}</li>
      ))}
    </ul>
  );
}

interface SummaryProps {
  readonly isDirty: boolean;
  readonly summary: string;
  readonly error: string | undefined;
  readonly onChange: (summary: string) => void;
}

/**
 * 摘要：沒有修改時不出現（送不出去，摘要沒有用）。窄螢幕收在「加上摘要」後面，送出列才不會佔掉半個畫面；
 * 已經有內容或有問題時一律打開。按了「加上摘要」，按鈕換成輸入框，焦點跟著過去。
 */
function SummaryField({ isDirty, summary, error, onChange }: SummaryProps) {
  const isCompact = useIsCompact();
  const [isOpen, setIsOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const hintId = useId();
  const hasContent = summary !== '' || error !== undefined;
  if (!isDirty && !hasContent) {
    return null;
  }
  if (isCompact && !isOpen && !hasContent) {
    return (
      <Button size="sm" variant="ghost" className={styles.addSummary} onClick={() => { updateThenFocus(() => { setIsOpen(true); }, [inputRef]); }}>
        加上摘要
      </Button>
    );
  }
  // 說明只給報讀軟體（自己的元素，用 aria-describedby 連過去），畫面上是 placeholder：寬螢幕的送出列才排得成一行。
  return (
    <>
      <TextField
        ref={inputRef}
        className={styles.summaryField}
        label="摘要"
        placeholder={SUMMARY_PLACEHOLDER}
        aria-describedby={hintId}
        value={summary}
        error={error}
        maxLength={SUMMARY_MAX_LENGTH}
        onChange={(event) => { onChange(event.target.value); }}
      />
      <span id={hintId} className="visually-hidden">
        {SUMMARY_HINT}
      </span>
    </>
  );
}

interface DiscardProps {
  readonly count: number;
  readonly onDiscard: () => void;
  readonly onClose: () => void;
}

function DiscardDialog({ count, onDiscard, onClose }: DiscardProps) {
  return (
    <ConfirmDialog
      title="捨棄所有修改？"
      description={`這 ${String(count)} 項還沒送出的修改會不見，設定回到目前的樣子。`}
      confirmLabel="捨棄"
      tone="danger"
      onConfirm={() => {
        onClose();
        onDiscard();
      }}
      onCancel={onClose}
    />
  );
}

/**
 * 頁面底部的送出列：還沒送出的修改有幾項、摘要、「捨棄修改」與「預覽變更」，以及問題的說明。
 * 沒有修改時留在頁尾、一行；有修改時黏在畫面底部（data-dirty），改到哪裡都看得到還有幾項沒送出。
 */
export function SubmitBar({ count, summary, onSummaryChange, onSubmit, onDiscard, isPending, alert, summaryError, unplaced, elsewhere }: SubmitBarProps) {
  const [isDiscarding, setIsDiscarding] = useState(false);
  const isDirty = count > 0;
  const status = isDirty ? `還沒送出的修改：${String(count)} 項` : '還沒有修改';
  return (
    <section className={styles.submitBar} data-dirty={isDirty} aria-label="送出修改">
      <FormAlert message={alert} />
      {elsewhere}
      <UnplacedIssues issues={unplaced} />
      <div className={styles.submitRow}>
        <p className={styles.pendingCount} aria-live="polite">
          {status}
        </p>
        <SummaryField isDirty={isDirty} summary={summary} error={summaryError} onChange={onSummaryChange} />
        <div className={styles.submitActions}>
          {isDirty && (
            <Button disabled={isPending} onClick={() => { setIsDiscarding(true); }}>
              捨棄修改
            </Button>
          )}
          <Button variant="primary" disabled={!isDirty || isPending} onClick={onSubmit}>
            {isPending ? '送出中…' : '預覽變更'}
          </Button>
        </div>
      </div>
      {isDiscarding && <DiscardDialog count={count} onDiscard={onDiscard} onClose={() => { setIsDiscarding(false); }} />}
    </section>
  );
}
