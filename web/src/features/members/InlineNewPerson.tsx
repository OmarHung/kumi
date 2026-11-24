import { useEffect, useId, type KeyboardEvent } from 'react';
import type { Person } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { isComposingKey } from '../../lib/keyboard';
import { NewPersonFields, useNewPersonForm } from '../people/NewPersonFields';
import styles from './MembersDialog.module.css';

interface InlineNewPersonProps {
  /** 「新增人員…」用 aria-controls 指過來。 */
  readonly id: string;
  readonly onCreated: (person: Person) => void;
  /** 「取消」與 Esc：收起來，焦點回到「新增人員…」由呼叫端做。 */
  readonly onCancel: () => void;
}

/**
 * 成員對話框裡就地展開的「新增人員」：和人員頁的對話框同一套欄位與檢查（people/NewPersonFields.tsx），
 * 只是不再開一層對話框（docs/DESIGN.md 6.2：一個對話框上面不再開對話框）。是「加入成員」表單後面的另一個表單
 * （表單不能包表單）。打開時焦點到「名稱」；Esc 只收起這一塊，不往外傳（成員對話框不會跟著關上），
 * 輸入法選字中的 Esc 也不往外傳、但不收起；送出中不收。
 */
export function InlineNewPerson({ id, onCreated, onCancel }: InlineNewPersonProps) {
  const form = useNewPersonForm(onCreated);
  const headingId = useId();
  const { nameRef, isPending } = form;
  const cancel = isPending ? () => undefined : onCancel;

  useEffect(() => {
    nameRef.current?.focus();
  }, [nameRef]);

  function handleKeyDown(event: KeyboardEvent<HTMLFormElement>) {
    if (event.key !== 'Escape') return;
    // 選字中的 Esc 是輸入法的（取消選字）：不收起表單，也不往外傳，成員對話框才不會被它關掉。
    event.stopPropagation();
    if (!isComposingKey(event)) cancel();
  }

  return (
    <form id={id} className={styles.newPerson} aria-labelledby={headingId} noValidate onSubmit={form.handleSubmit} onKeyDown={handleKeyDown}>
      <h4 id={headingId} className={styles.newPersonHeading}>
        新增人員
      </h4>
      <p className={styles.newPersonHint}>新的人用這裡設定的 Email 與密碼登入；建好之後會自動選進上面的「人員」。</p>
      <NewPersonFields form={form} />
      <div className={styles.newPersonActions}>
        <Button onClick={cancel} disabled={isPending}>
          取消
        </Button>
        {/* 凹下去的這一塊是對話框裡另一個區，可以有自己的主要按鈕（docs/DESIGN.md 6.2）；名稱說出結果，和旁邊的「加入」分得開。 */}
        <Button type="submit" variant="primary" disabled={isPending}>
          {isPending ? '新增中…' : '新增人員'}
        </Button>
      </div>
    </form>
  );
}
