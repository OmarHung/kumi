import { useState, type KeyboardEvent, type SubmitEvent } from 'react';
import { KNOWLEDGE_PAGE_TITLE_MAX } from '../../../api/knowledge';
import { TextField } from '../../../components/Field/Field';
import styles from './PageTree.module.css';

interface NewPageInputProps {
  /** 新頁面在樹上的層級（上層的層級加一）：輸入框縮排得和它之後的位置一樣。 */
  readonly level: number;
  readonly isPending: boolean;
  /** 伺服器擋下的原因（標題不合規則、超過層級或頁數的上限）。 */
  readonly error: string | null;
  readonly onSubmit: (title: string) => void;
  readonly onCancel: () => void;
}

const EMPTY_TITLE_MESSAGE = '請輸入頁面的標題。';

/**
 * 在樹上新增頁面的輸入框（docs/design/08-interaction.md 8.3「新增頁面」）：Enter 建立並打開那一頁、Esc 取消。
 * 送出中輸入框唯讀（焦點留得住）、再按 Enter 不會重送；什麼都沒打就離開輸入框當成取消，不留一個空的輸入框在樹上。
 */
export function NewPageInput({ level, isPending, error, onSubmit, onCancel }: NewPageInputProps) {
  const [title, setTitle] = useState('');
  const [emptyError, setEmptyError] = useState(false);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isPending) return;
    if (title.trim() === '') {
      setEmptyError(true);
      return;
    }
    onSubmit(title.trim());
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key !== 'Escape') return;
    // 不讓 Esc 再往外傳：窄螢幕的頁面面板也聽 Esc，只該收起這個輸入框。
    event.stopPropagation();
    if (!isPending) onCancel();
  }

  return (
    <li role="none" className={styles.newItem} data-level={level}>
      <form className={styles.newForm} aria-busy={isPending} onSubmit={handleSubmit}>
        <TextField
          label="新頁面的標題"
          isLabelHidden
          size="sm"
          autoFocus
          value={title}
          placeholder="頁面標題，按 Enter 建立"
          maxLength={KNOWLEDGE_PAGE_TITLE_MAX}
          readOnly={isPending}
          error={error ?? (emptyError ? EMPTY_TITLE_MESSAGE : undefined)}
          onChange={(event) => {
            setTitle(event.target.value);
            setEmptyError(false);
          }}
          onKeyDown={handleKeyDown}
          onBlur={() => {
            if (title === '' && !isPending) onCancel();
          }}
        />
      </form>
    </li>
  );
}
