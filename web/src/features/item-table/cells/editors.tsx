import type { KeyboardEvent, ReactNode } from 'react';
import { fieldControlClassName } from '../../../components/Field/Field';
import type { DatePickerDismissReason } from '../../../components/DatePicker/DatePicker';
import { Select, type SelectDismissReason, type SelectOption } from '../../../components/Select/Select';
import { cx } from '../../../lib/cx';
import { isCommitEnter } from '../../../lib/keyboard';
import type { EditorApi } from './EditableCell';
import styles from './Cells.module.css';

export type { SelectOption } from '../../../components/Select/Select';

export function handleKeys(event: KeyboardEvent, api: EditorApi<string>, needsModifier = false): void {
  if (event.key === 'Escape') {
    event.preventDefault();
    api.cancel();
    return;
  }
  const hasModifier = event.ctrlKey || event.metaKey;
  // 輸入中文時，選字用的 Enter 不能當成「儲存」。
  if (isCommitEnter(event) && (!needsModifier || hasModifier)) {
    event.preventDefault();
    api.commit('enter');
  }
}

export function isInDialog<T>(api: EditorApi<T>): boolean {
  return api.placement === 'dialog';
}

const CELL_CLASSES = { input: styles.input, textarea: styles.textarea } as const;

/**
 * 格子裡是格子的長相（矮一號、補到一列高、沒有自己的外框：框由格子畫）；對話框裡和 TextField 一樣（components/Field 的 .control：
 * 內距、滑過外框變色、觸控 44px），表單裡的欄位長得一致。下拉是 components/Select，自己有這兩種大小與 bare 的長相。
 */
function controlClassName<T>(api: EditorApi<T>, kind: 'input' | 'textarea'): string | undefined {
  return isInDialog(api) ? fieldControlClassName(kind, api.isInvalid) : CELL_CLASSES[kind];
}

function sharedProps<T>(api: EditorApi<T>) {
  return {
    id: api.id,
    'aria-label': api.label,
    'aria-invalid': api.isInvalid || undefined,
    'aria-describedby': api.describedBy,
    // 格子裡打開編輯器就是要打字：焦點由 EditableCell 的 EditorFrame 放進來（不用 autoFocus：它不能 preventScroll，
    // 標題是固定欄，瀏覽器自己的捲動會把橫向捲過去的表格捲回最左邊）。對話框自己把焦點放在第一個欄位。
  };
}

interface InputEditorProps {
  readonly api: EditorApi<string>;
  readonly type?: 'text' | 'url';
  readonly inputMode?: 'decimal';
  readonly maxLength?: number;
  /** 格子裡的額外樣子（表格的標題是 medium 字重，和格子平常的字一樣）。 */
  readonly className?: string;
}

/** 一行的輸入：文字、網址、數字（日期是 DateEditor）。 */
export function InputEditor({ api, type = 'text', inputMode, maxLength, className }: InputEditorProps) {
  return (
    <input
      {...sharedProps(api)}
      className={cx(controlClassName(api, 'input'), !isInDialog(api) && className)}
      type={type}
      inputMode={inputMode}
      maxLength={maxLength}
      value={api.draft}
      readOnly={api.isSaving}
      onChange={(event) => {
        api.setDraft(event.target.value);
      }}
      onKeyDown={(event) => {
        handleKeys(event, api);
      }}
      onBlur={() => {
        api.commit('blur');
      }}
    />
  );
}

interface TextAreaEditorProps {
  readonly api: EditorApi<string>;
  readonly maxLength: number;
}

/**
 * 長文字：Enter 換行，Ctrl+Enter（Mac 是 ⌘+Enter）或離開格子儲存。
 * 對話框裡 Ctrl+Enter 是送出整個對話框（Enter 要留給換行），提示照著說，並連到文字框（aria-describedby）。
 */
export function TextAreaEditor({ api, maxLength }: TextAreaEditorProps) {
  const hintId = api.id === undefined ? undefined : `${api.id}-keys`;
  const hint = isInDialog(api) ? 'Ctrl+Enter（Mac 是 ⌘+Enter）送出' : 'Ctrl+Enter 儲存，Esc 取消';
  const describedBy = isInDialog(api) ? [api.describedBy, hintId].filter(Boolean).join(' ') : api.describedBy;
  return (
    <>
      <textarea
        {...sharedProps(api)}
        aria-describedby={describedBy}
        className={controlClassName(api, 'textarea')}
        rows={4}
        maxLength={maxLength}
        value={api.draft}
        readOnly={api.isSaving}
        onChange={(event) => {
          api.setDraft(event.target.value);
        }}
        onKeyDown={(event) => {
          handleKeys(event, api, true);
        }}
        onBlur={() => {
          api.commit('blur');
        }}
      />
      <span id={hintId} className={styles.hint}>
        {hint}
      </span>
    </>
  );
}

interface SelectEditorProps {
  readonly api: EditorApi<string>;
  readonly options: readonly SelectOption[];
}

/**
 * 格子裡沒選就關上清單（internal/HANDOFF-custom-controls.md 3A.3）：
 * - Esc：取消編輯（一次就關上清單並取消，規範 7.3）。
 * - 點外面（焦點移到外面也算）：和離開格子一樣（沒改就只是結束編輯）。焦點已經去了別的地方，不搶回來。
 * - 按鈕被捲出畫面（scrolled）：取消編輯，焦點回到格子（不捲動）。焦點原本就在下拉裡，用「離開」結束的話按鈕跟著不見、
 *   焦點會掉到頁面本體（項目頁與面板沒有表格幫忙找回停駐點）。
 * - 再按一次按鈕：取消編輯，焦點回到格子。選了就已經存了，這時草稿一定沒改（剛存失敗的除外，和 Esc 一樣放掉）；
 *   用「離開」結束的話焦點不回到格子，按鈕跟著不見、焦點會掉到頁面本體。
 * - Tab：交給 onBlur。onDismiss 是在按鍵事件裡叫的，這時就結束編輯，按鈕先不見、焦點掉到頁面本體，Tab 就不是往下一個走。
 */
export function dismissInCell(api: EditorApi<string>, reason: SelectDismissReason | DatePickerDismissReason): void {
  if (reason === 'escape' || reason === 'toggle' || reason === 'scrolled') api.cancel();
  else if (reason === 'outside') api.commit('blur');
}

/** 清單關著時（例如剛存失敗）的 Esc 是取消編輯，不往外傳給面板（preventDefault：lib/shortcuts.ts 看它）。 */
function cancelOnEscape(event: KeyboardEvent, api: EditorApi<string>): void {
  if (event.key !== 'Escape') return;
  event.preventDefault();
  api.cancel();
}

/**
 * 格子裡下拉的按鈕上顯示什麼：還是目前的值就是格子平常的樣子（同一個狀態標籤、名字）；選了別的值（儲存中、存不進去）
 * 是那個選項的小圖與文字（不是目前的值，沒有「（目前）」）。寬度由格子決定（.cellSelect），長的用省略號。
 */
function cellDisplay(api: EditorApi<string>, options: readonly SelectOption[]): ReactNode {
  if (api.display !== undefined) return api.display;
  const selected = options.find((option) => option.value === api.draft);
  if (selected === undefined) return null;
  return (
    <>
      {selected.leading !== undefined && (
        <span className={styles.selectLeading} aria-hidden="true">
          {selected.leading}
        </span>
      )}
      {selected.label}
    </>
  );
}

/**
 * 下拉（components/Select）：值只有明確選了（點選、Enter、空白鍵）才變，方向鍵只移動反白，不必再分滑鼠與鍵盤。
 * 格子裡：進入編輯清單就打開；選了就存並結束編輯（選了目前的值也結束，不送出）；Esc 取消；Tab、點外面是關上、不改。
 * 格子裡的按鈕是 bare（沒有自己的外框、底色與焦點光暈）：框由格子畫，只有一圈。按鈕裡是格子平常的樣子（api.display，
 * 例如同一個狀態標籤），不是選項的文字與箭頭：和格子一樣大，欄寬不變；文字照樣唸得出目前的值。
 * 對話框裡：不自己打開，選了只是填好，和其他欄位一起送；關著時的 Esc 交給對話框（關上）。
 * 送出中（aria-busy）選了不收：送出的是按下時的內容，畫面也不會和送出的不一樣。
 */
export function SelectEditor({ api, options }: SelectEditorProps) {
  const isCell = !isInDialog(api);
  return (
    <Select
      id={api.id}
      aria-label={api.label}
      aria-invalid={api.isInvalid || undefined}
      aria-describedby={api.describedBy}
      aria-busy={api.isSaving || undefined}
      size={isCell ? 'sm' : 'md'}
      variant={isCell ? 'bare' : 'default'}
      className={isCell ? styles.cellSelect : undefined}
      display={isCell ? <span className={styles.selectDisplay}>{cellDisplay(api, options)}</span> : undefined}
      value={api.draft}
      options={options}
      isOpenInitially={isCell}
      onChange={api.setDraft}
      onPick={isCell ? (value) => { api.commit('enter', value); } : undefined}
      onDismiss={isCell ? (reason) => { dismissInCell(api, reason); } : undefined}
      onKeyDown={isCell ? (event) => { cancelOnEscape(event, api); } : undefined}
      onBlur={() => {
        api.commit('blur');
      }}
    />
  );
}
