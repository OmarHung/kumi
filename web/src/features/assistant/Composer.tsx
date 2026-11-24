import { useRef, type KeyboardEvent, type RefObject } from 'react';
import type { AiOptions, AiSelection } from '../../api/aiTypes';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { isCommitEnter } from '../../lib/keyboard';
import { ModelPicker } from './ModelPicker';
import { useDrafts, useRestoreFocus, type DraftFill } from './useComposerDrafts';
import styles from './Assistant.module.css';

const SETTINGS_PLACEHOLDER = '描述你想怎麼改這個專案的設定…';
const ITEM_PLACEHOLDER = '問這個項目的事，或請助理建議怎麼改…';

interface ComposerProps {
  readonly options: AiOptions;
  /** 現在用哪一個；null 是組織沒有開放任何模型（不能送）。 */
  readonly selection: AiSelection | null;
  readonly onSelectionChange: (selection: AiSelection) => void;
  readonly isStreaming: boolean;
  /** 送出到串流結束的整段（含開對話）：停止鈕還沒出現、也不能再送。 */
  readonly isSending: boolean;
  /** 剛從「新的對話」開出來的那一則：草稿跟著過去（換到別的對話不帶）。 */
  readonly createdConversationId: string | null;
  /** 對話還在載入：不能送（送出的位置還不確定）。 */
  readonly isLoading: boolean;
  /** 草稿依對話分開；沒有對話（新的對話）是自己的一份。 */
  readonly conversationId: string | undefined;
  /** 送出之前的失敗，顯示在輸入框上面；輸入的字留著。 */
  readonly error: string | null;
  readonly onDismissError: () => void;
  /** 送出：伺服器收下之後才呼叫 onAccepted（輸入框在這時清掉送出的那段字）。 */
  readonly onSend: (text: string, selection: AiSelection, onAccepted: () => void) => void;
  readonly onStop: () => void;
  /** 例子按下之後把字填進輸入框（不直接送）。 */
  readonly fill: DraftFill;
  readonly inputRef: RefObject<HTMLTextAreaElement | null>;
  /** 選了項目：提示字改成問這個項目的事。 */
  readonly hasItem: boolean;
}

interface ComposerBarProps extends Pick<ComposerProps, 'options' | 'selection' | 'onSelectionChange' | 'isStreaming' | 'onStop'> {
  readonly canSend: boolean;
}

/** 沒有可選的模型：有主機來源現在不能用時，逐一說明原因（來源名稱加伺服器給的一句話）；都沒有才叫人去開放。 */
function NoModels({ options }: { readonly options: AiOptions }) {
  const unavailable = options.connections.filter((connection) => connection.notice !== null);
  if (unavailable.length === 0) return <p className={styles.noModels}>組織還沒有開放任何模型，請組織管理員到「AI 設定」開放。</p>;
  return (
    <div className={styles.noModels}>
      {unavailable.map((connection) => (
        <p key={connection.id}>{`${connection.name}：${connection.notice ?? ''}`}</p>
      ))}
    </div>
  );
}

/** 輸入框下面一列：模型與思考深度、送出（回覆中是停止）。 */
function ComposerBar({ options, selection, onSelectionChange, isStreaming, canSend, onStop }: ComposerBarProps) {
  return (
    <div className={styles.composerBar}>
      <div className={styles.pickers}>
        {selection === null ? (
          // 只剩現在不能用的主機來源：說伺服器給的原因，不叫人去「AI 設定」開放（模型其實開著）。
          <NoModels options={options} />
        ) : (
          <ModelPicker options={options} selection={selection} onChange={onSelectionChange} />
        )}
      </div>
      {isStreaming ? (
        <Button onClick={onStop}>停止</Button>
      ) : (
        <Button type="submit" variant="primary" disabled={!canSend}>
          送出
        </Button>
      )}
    </div>
  );
}

interface InputProps {
  readonly text: string;
  readonly onChange: (text: string) => void;
  readonly onSubmit: () => void;
  readonly inputRef: RefObject<HTMLTextAreaElement | null>;
  readonly placeholder: string;
}

/** 多行的文字框，跟著內容長高。Enter 送出、Shift+Enter 換行；輸入法選字用的 Enter 不算。 */
function ComposerInput({ text, onChange, onSubmit, inputRef, placeholder }: InputProps) {
  const handleKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (!isCommitEnter(event) || event.shiftKey) return;
    event.preventDefault();
    onSubmit();
  };
  return (
    <div className={styles.inputFrame} data-value={text}>
      <textarea
        ref={inputRef}
        className={styles.input}
        aria-label="訊息"
        placeholder={placeholder}
        rows={1}
        value={text}
        onChange={(event) => {
          onChange(event.target.value);
        }}
        onKeyDown={handleKeyDown}
      />
    </div>
  );
}

/**
 * 對話最下面的輸入框。Enter 送出、Shift+Enter 換行；輸入法組字中（選字用的 Enter）不送出。
 * 空白、沒有模型可選、對話還在載入、上一次送出還沒結束時不能送；回覆中「送出」換成「停止」。
 */
export function Composer({ options, selection, onSelectionChange, isStreaming, isSending, createdConversationId, isLoading, conversationId, error, onDismissError, onSend, onStop, fill, inputRef, hasItem }: ComposerProps) {
  const { text, setText, clearSent, key } = useDrafts(conversationId, fill, createdConversationId);
  const formRef = useRef<HTMLFormElement>(null);
  useRestoreFocus(isStreaming, formRef, inputRef);
  const canSend = selection !== null && text.trim() !== '' && !isStreaming && !isSending && !isLoading;
  const submit = () => {
    if (!canSend) return;
    onSend(text.trim(), selection, () => {
      clearSent(key, text);
    });
  };

  return (
    <form
      ref={formRef}
      className={styles.composer}
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      {error !== null && (
        <Notice tone="danger" onDismiss={onDismissError}>
          {error}
        </Notice>
      )}
      <ComposerInput text={text} onChange={setText} onSubmit={submit} inputRef={inputRef} placeholder={hasItem ? ITEM_PLACEHOLDER : SETTINGS_PLACEHOLDER} />
      <ComposerBar options={options} selection={selection} onSelectionChange={onSelectionChange} isStreaming={isStreaming} canSend={canSend} onStop={onStop} />
    </form>
  );
}
