import { useId, useRef, useState, type ChangeEvent, type ClipboardEvent, type DragEvent, type KeyboardEvent, type RefObject } from 'react';
import type { AiOptions, AiSelection } from '../../api/aiTypes';
import type { AssistantScope } from '../../api/assistant';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { Notice } from '../../components/Notice/Notice';
import { AttachmentChips } from './AttachmentChips';
import { engineSourceOf, FILE_ACCEPT, isUnreadable, PASTED_IMAGE_NAME, unreadableNote, type EngineSource } from './attachmentRules';
import { ModelPicker } from './ModelPicker';
import { SendKeyPicker } from './SendKeyPicker';
import { isSendKeystroke, sendKeyHint, useSendKey, type SendKey } from './sendKey';
import { useAttachmentAnnouncement } from './useAttachmentAnnouncement';
import { useComposerAttachments, type AttachmentEntry, type ComposerAttachments } from './useComposerAttachments';
import { useDrafts, useRestoreFocus, type DraftFill } from './useComposerDrafts';
import styles from './Assistant.module.css';

const SETTINGS_PLACEHOLDER = '描述你想怎麼改這個專案的設定…';
const ITEM_PLACEHOLDER = '問這個項目的事，或請助理建議怎麼改…';
const ORGANIZATION_PLACEHOLDER = '問模板、規劃，或請助理幫你開新專案…';

function placeholderOf(isOrganization: boolean, hasItem: boolean): string {
  if (isOrganization) return ORGANIZATION_PLACEHOLDER;
  return hasItem ? ITEM_PLACEHOLDER : SETTINGS_PLACEHOLDER;
}

interface ComposerProps {
  /** null 是不選專案（組織的對話）：沒有附件，附加檔案、拖進來、貼上都不開放。 */
  readonly projectKey: AssistantScope;
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
  /** 送出：伺服器收下之後才呼叫 onAccepted（輸入框在這時清掉送出的那段字與附件小卡）。attachmentIds 是已經上傳好的附件，依順序。 */
  readonly onSend: (text: string, selection: AiSelection, attachmentIds: readonly string[], onAccepted: () => void) => void;
  readonly onStop: () => void;
  /** 例子按下之後把字填進輸入框（不直接送）。 */
  readonly fill: DraftFill;
  readonly inputRef: RefObject<HTMLTextAreaElement | null>;
  /** 選了項目：提示字改成問這個項目的事。 */
  readonly hasItem: boolean;
  /** 窄的地方（浮動面板）：送出按法的選單鈕只留按鍵。 */
  readonly isCompact?: boolean;
}

interface ComposerBarProps extends Pick<ComposerProps, 'options' | 'selection' | 'onSelectionChange' | 'isStreaming' | 'onStop'> {
  readonly canSend: boolean;
  readonly sendHint: string;
  readonly sendKey: SendKey;
  readonly isCompact: boolean;
  /** null：這個對話不收附件，不畫迴紋針。 */
  readonly onPickFiles: ((files: readonly File[]) => void) | null;
}

/** 沒有可選的模型：有主機來源現在不能用時，逐一說明原因（來源名稱加伺服器給的一句話）；都沒有才叫人去開放。 */
function NoModels({ options }: { readonly options: AiOptions }) {
  const unavailable = options.connections.filter((connection) => connection.notice !== null);
  if (unavailable.length === 0) return <p className={styles.noModels}>組織還沒有開放任何模型：請組織管理員到「AI 助理設定」開放組織的 API 金鑰，或請組織擁有者設定主機 AI 代理。</p>;
  return (
    <div className={styles.noModels}>
      {unavailable.map((connection) => (
        <p key={connection.id}>{`${connection.name}：${connection.notice ?? ''}`}</p>
      ))}
    </div>
  );
}

/** 「附加檔案」：隱藏的選檔欄位加自己畫的迴紋針鈕（不用瀏覽器原生的長相）。 */
function AttachButton({ onPick }: { readonly onPick: (files: readonly File[]) => void }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const handleChange = (event: ChangeEvent<HTMLInputElement>) => {
    onPick(Array.from(event.target.files ?? []));
    // 清掉選的值：同一個檔案移除之後還能再選一次。
    event.target.value = '';
  };
  return (
    <>
      <input ref={inputRef} type="file" hidden multiple accept={FILE_ACCEPT} aria-label="選擇附件檔案" tabIndex={-1} onChange={handleChange} />
      <IconButton
        icon="attach"
        label="附加檔案"
        onClick={() => {
          inputRef.current?.click();
        }}
      />
    </>
  );
}

/** 輸入框下面一列：附加檔案、模型與思考深度、送出（回覆中是停止）。 */
function ComposerBar({ options, selection, onSelectionChange, isStreaming, canSend, sendHint, sendKey, isCompact, onStop, onPickFiles }: ComposerBarProps) {
  return (
    <div className={styles.composerBar}>
      {onPickFiles !== null && <AttachButton onPick={onPickFiles} />}
      <div className={styles.pickers}>
        {selection === null ? (
          // 只剩現在不能用的主機來源：說伺服器給的原因，不叫人去「AI 助理設定」開放（模型其實開著）。
          <NoModels options={options} />
        ) : (
          <ModelPicker options={options} selection={selection} onChange={onSelectionChange} />
        )}
      </div>
      <SendKeyPicker sendKey={sendKey} isCompact={isCompact} />
      {isStreaming ? (
        <Button className={styles.sendButton} onClick={onStop}>
          <Icon name="stop" />
          停止
        </Button>
      ) : (
        <Button type="submit" variant="primary" className={styles.sendButton} disabled={!canSend} title={sendHint}>
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
  readonly isDragging: boolean;
  readonly sendKey: SendKey;
  readonly onPaste: (event: ClipboardEvent<HTMLTextAreaElement>) => void;
}

/** 多行的文字框，跟著內容長高。送出的按法依偏好（Enter，或 ⌘／Ctrl+Enter）；輸入法選字用的 Enter 不算。檔案拖進來時外框換成虛線。 */
function ComposerInput({ text, onChange, onSubmit, inputRef, placeholder, isDragging, sendKey, onPaste }: InputProps) {
  const hintId = useId();
  const handleKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (!isSendKeystroke(event, sendKey)) return;
    event.preventDefault();
    onSubmit();
  };
  return (
    <div className={styles.inputFrame} data-value={text} data-dragging={isDragging ? 'true' : undefined}>
      <span id={hintId} className="visually-hidden">
        {sendKeyHint(sendKey)}
      </span>
      <textarea
        ref={inputRef}
        className={styles.input}
        aria-label="訊息"
        aria-describedby={hintId}
        placeholder={placeholder}
        rows={1}
        value={text}
        onChange={(event) => {
          onChange(event.target.value);
        }}
        onKeyDown={handleKeyDown}
        onPaste={onPaste}
      />
    </div>
  );
}

function hasFiles(event: DragEvent): boolean {
  return Array.from(event.dataTransfer.types).includes('Files');
}

/**
 * 剪貼簿裡的圖片（例如截圖）：沒有檔名的叫「貼上的圖片.png」。文字與檔案一起貼時照文字貼。
 * 取捨：從 Finder 複製檔案再貼上，瀏覽器會同時給檔案與檔名的 text/plain，這裡一律照文字貼（不上傳那個檔案）；
 * 要附檔案請用迴紋針或拖進來。不為了它去猜 text/plain 是不是檔名（網頁複製的圖片也常帶文字，猜錯會吃掉使用者要貼的字）。
 */
function pastedFiles(event: ClipboardEvent): readonly File[] {
  const { clipboardData } = event;
  if (Array.from(clipboardData.types).includes('text/plain')) return [];
  return Array.from(clipboardData.files).map((file) => (file.name === '' || file.name === 'image.png' ? new File([file], PASTED_IMAGE_NAME, { type: file.type }) : file));
}

function idsOf(ready: readonly AttachmentEntry[]): readonly string[] {
  return ready.flatMap((entry) => (entry.attachment === null ? [] : [entry.attachment.id]));
}

function pasteFiles(event: ClipboardEvent<HTMLTextAreaElement>, add: (files: readonly File[]) => void): void {
  const files = pastedFiles(event);
  if (files.length === 0) return;
  event.preventDefault();
  add(files);
}

/** 能不能送出的檢查與送出時要帶的附件。 */
function attachmentState(entries: readonly AttachmentEntry[], source: EngineSource | null) {
  const ready = entries.filter((entry) => entry.status === 'ready' && entry.attachment !== null);
  return {
    ready,
    // 還在傳、失敗、被擋下的：要先等、重試或移除。
    isBlocked: entries.some((entry) => entry.status !== 'ready'),
    hasUnsupported: ready.some((entry) => isUnreadable(source, entry.kind)),
  };
}

/**
 * 檔案拖進輸入框：外框換成虛線，放開就附加；沒有檔案的拖曳（選取的文字）照舊。
 * 用 dragenter／dragleave 計數（進到裡面的子元素會先 enter 再 leave）：Safari 的 dragleave 常沒有 relatedTarget，不能靠它判斷離開。
 */
function useFileDrop(onFiles: ((files: readonly File[]) => void) | null) {
  const [isDragging, setIsDragging] = useState(false);
  const depth = useRef(0);
  const reset = () => {
    depth.current = 0;
    setIsDragging(false);
  };
  const dropProps = {
    onDragEnter: (event: DragEvent<HTMLFormElement>) => {
      if (!hasFiles(event)) return;
      event.preventDefault();
      depth.current += 1;
      setIsDragging(true);
    },
    onDragOver: (event: DragEvent<HTMLFormElement>) => {
      if (!hasFiles(event)) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = 'copy';
    },
    onDragLeave: (event: DragEvent<HTMLFormElement>) => {
      if (!hasFiles(event)) return;
      depth.current = Math.max(0, depth.current - 1);
      if (depth.current === 0) setIsDragging(false);
    },
    onDrop: (event: DragEvent<HTMLFormElement>) => {
      if (!hasFiles(event)) return;
      event.preventDefault();
      reset();
      onFiles?.(Array.from(event.dataTransfer.files));
    },
  };
  // 不收附件的對話（不選專案）：不接管拖放，也不顯示「放開以附加」的狀態。
  return onFiles === null ? { isDragging: false, dropProps: {} } : { isDragging, dropProps };
}

interface AttachmentAreaProps {
  readonly projectKey: string;
  readonly attachments: ComposerAttachments;
  readonly source: EngineSource | null;
  readonly hasUnsupported: boolean;
  readonly isLocked: boolean;
  readonly inputRef: RefObject<HTMLTextAreaElement | null>;
}

/** 文字框上方：來源讀不到某種附件的說明、附件小卡與失敗的原因，加上一個共用的報讀區（上傳完成、失敗都由它唸）。 */
function AttachmentArea({ projectKey, attachments, source, hasUnsupported, isLocked, inputRef }: AttachmentAreaProps) {
  const announcement = useAttachmentAnnouncement(attachments.entries, hasUnsupported ? source : null);
  return (
    <>
      <span className="visually-hidden" role="status">
        {announcement}
      </span>
      {hasUnsupported && source !== null && <p className={styles.attachNote}>{unreadableNote(source)}</p>}
      <AttachmentChips projectKey={projectKey} entries={attachments.entries} source={source} isLocked={isLocked} inputRef={inputRef} onRemove={attachments.remove} onRetry={attachments.retry} />
    </>
  );
}

/**
 * 對話最下面的輸入框。送出的按法是個人偏好（預設 Enter 送出、Shift+Enter 換行；也可以改成 ⌘／Ctrl+Enter 送出）；輸入法組字中（選字用的 Enter）不送出。
 * 沒有字也沒有附件、沒有模型可選、對話還在載入、上一次送出還沒結束、附件還在傳或失敗時不能送；回覆中「送出」換成「停止」。
 * 附件：迴紋針、拖進來、貼上都先上傳，文字框上方一列小卡；伺服器收下訊息之後才清掉字與小卡。
 */
export function Composer({ projectKey, options, selection, onSelectionChange, isStreaming, isSending, createdConversationId, isLoading, conversationId, error, onDismissError, onSend, onStop, fill, inputRef, hasItem, isCompact = false }: ComposerProps) {
  const { text, setText, clearSent, key } = useDrafts(conversationId, fill, createdConversationId);
  const attachments = useComposerAttachments(projectKey ?? '', conversationId, createdConversationId, isSending);
  const addFiles = projectKey === null ? null : attachments.add;
  const formRef = useRef<HTMLFormElement>(null);
  const { isDragging, dropProps } = useFileDrop(addFiles);
  const sendKey = useSendKey();
  useRestoreFocus(isStreaming, formRef, inputRef);
  const source = engineSourceOf(options, selection);
  const { ready, isBlocked, hasUnsupported } = attachmentState(attachments.entries, source);
  const canSend = selection !== null && (text.trim() !== '' || ready.length > 0) && !isBlocked && !hasUnsupported && !isStreaming && !isSending && !isLoading;
  const submit = () => {
    if (!canSend) return;
    onSend(text.trim(), selection, idsOf(ready), () => {
      clearSent(key, text);
      attachments.clearSent(ready.map((entry) => entry.id));
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
      {...dropProps}
    >
      {error !== null && (
        <Notice tone="danger" onDismiss={onDismissError}>
          {error}
        </Notice>
      )}
      {projectKey !== null && <AttachmentArea projectKey={projectKey} attachments={attachments} source={source} hasUnsupported={hasUnsupported} isLocked={isSending} inputRef={inputRef} />}
      <ComposerInput
        text={text}
        onChange={setText}
        onSubmit={submit}
        inputRef={inputRef}
        placeholder={placeholderOf(projectKey === null, hasItem)}
        isDragging={isDragging}
        sendKey={sendKey}
        onPaste={(event) => {
          if (addFiles !== null) pasteFiles(event, addFiles);
        }}
      />
      <ComposerBar options={options} selection={selection} onSelectionChange={onSelectionChange} isStreaming={isStreaming} canSend={canSend} sendHint={sendKeyHint(sendKey)} sendKey={sendKey} isCompact={isCompact} onStop={onStop} onPickFiles={addFiles} />
    </form>
  );
}
