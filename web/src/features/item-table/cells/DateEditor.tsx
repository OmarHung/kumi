import type { KeyboardEvent } from 'react';
import { DatePicker, type DatePickerDismissReason } from '../../../components/DatePicker/DatePicker';
import { isCommitEnter } from '../../../lib/keyboard';
import type { EditorApi } from './EditableCell';
import { dismissInCell, handleKeys, isInDialog } from './editors';
import styles from './Cells.module.css';

/**
 * 格子裡日曆沒選就關上：和下拉一樣，Esc 是取消編輯、點外面當成離開；不一樣的是輸入框被捲出畫面（scrolled）——
 * 下拉的按鈕捲走了就沒得操作，日期的輸入框還在、打的字也還在（焦點已經放回輸入框），只關日曆、照舊編輯中，
 * 和文字格一樣。不然用滾輪捲走會丟掉打的字，拖捲軸捲走（焦點離開）卻會存，兩種捲法不一致。
 */
function dismissDateInCell(api: EditorApi<string>, reason: DatePickerDismissReason): void {
  if (reason !== 'scrolled') dismissInCell(api, reason);
}

/** 對話框裡只有 Enter 是送出；Esc 照常交給對話框（關上），不在這裡攔。 */
function submitOnEnter(event: KeyboardEvent, api: EditorApi<string>): void {
  if (!isCommitEnter(event)) return;
  event.preventDefault();
  api.commit('enter');
}

/**
 * 日期（components/DatePicker，docs/DESIGN.md 7.3「日期」、8.3「就地編輯」）。值是 'YYYY-MM-DD'、''（沒有值），
 * 打了解析不了的字時是那串字（toCommand 判斷不合法，格子裡說明）。
 * 格子裡：bare、矮一號；用滑鼠點開的（api.isStartedByPointer）直接打開日曆，用鍵盤進入的不打開（照樣可以打字，↓ 再打開）。
 * 從日曆選了（onPick：日期、「今天」、「清除」）就存並結束編輯，和下拉「選了就存」一樣；打字照舊 Enter 或離開才存。
 * 日曆沒選就關上照 SelectEditor 的對應（dismissInCell）：Esc 是取消，點外面是離開；被捲出畫面只關日曆（dismissDateInCell）。
 * 對話框裡（補填）：一般的長相、不自己打開，選了只是填好，Enter 和其他欄位一樣送出。
 * 送出中不能打字（readOnly），選了也不收（aria-busy）：送出的是按下時的內容。
 */
export function DateEditor({ api }: { readonly api: EditorApi<string> }) {
  const isCell = !isInDialog(api);
  const picker = (
    <DatePicker
      id={api.id}
      aria-label={api.label}
      aria-invalid={api.isInvalid || undefined}
      aria-describedby={api.describedBy}
      aria-busy={api.isSaving || undefined}
      readOnly={api.isSaving}
      size={isCell ? 'sm' : 'md'}
      variant={isCell ? 'bare' : 'default'}
      value={api.draft}
      isOpenInitially={isCell && api.isStartedByPointer === true}
      onChange={api.setDraft}
      onPick={isCell ? (value) => { api.commit('enter', value); } : undefined}
      onDismiss={isCell ? (reason) => { dismissDateInCell(api, reason); } : undefined}
      onKeyDown={(event) => {
        if (isCell) handleKeys(event, api);
        else submitOnEnter(event, api);
      }}
      onBlur={() => {
        api.commit('blur');
      }}
    />
  );
  // 格子裡：浮起來的那一層寬度照「年月日加日曆圖示」，不照輸入框自己的寬度（size 的 20 個字）。
  return isCell ? <span className={styles.dateEditor}>{picker}</span> : picker;
}
