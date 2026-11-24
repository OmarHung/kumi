import type { Ref } from 'react';
import type { Member } from '../../api/types';
import type { EditorModeControl } from './EditorToolbarBar';
import type { EditorUploads } from '../attachments/useEditorUploads';

export interface RichTextEditorHandle {
  focus(): void;
}

/** 所見即所得的 Markdown 編輯器（說明、留言、專案說明的輸入）。存的還是 Markdown，編輯器只是換一種打字的方式。 */
export interface RichTextEditorProps {
  readonly label: string;
  /** 區塊的標題已經寫了同樣的字時，標籤只給輔助科技。 */
  readonly isLabelHidden?: boolean;
  /** Markdown。 */
  readonly value: string;
  /** Markdown；沒改（或改回來）時回傳載入時的原文。 */
  readonly onChange: (value: string) => void;
  /** Markdown 的長度上限。 */
  readonly maxLength: number;
  /** 最少幾行高。 */
  readonly rows: number;
  /** 專案說明、說明格式：高度填滿畫面（捲動的那一層有 data-editor-scroll）。 */
  readonly fillsViewport?: boolean;
  readonly placeholder?: string;
  /** 平常的說明，例如快速鍵。 */
  readonly hint: string;
  /** Ctrl 或 ⌘ + Enter。 */
  readonly onSubmit: () => void;
  /** Esc；提及清單、連結的浮動框開著時先關它們。沒有給就不處理 Esc。 */
  readonly onCancel?: () => void;
  /** 送出中：內容不能改，焦點留得住。 */
  readonly isBusy: boolean;
  readonly error: string | null;
  /** true：打開時游標在最前面；'end'：在最後面（編輯既有的內容）。 */
  readonly autoFocus?: boolean | 'end';
  /** 收起來的樣子（留言框平常是一行）：工具列與提示看不到，提示仍用 aria-describedby 連著。 */
  readonly isCollapsed?: boolean;
  /** 可以用 @ 提及的人（專案的成員）；沒有給就沒有提及。 */
  readonly mentionMembers?: readonly Member[];
  /** 給了才接貼上、拖進來的檔案。 */
  readonly uploads?: EditorUploads;
  /** false：附件不載入（專案說明）。預設 true。 */
  readonly hasAttachments?: boolean;
  readonly ref?: Ref<RichTextEditorHandle>;
}

/** 外殼與真正的編輯器（所見即所得、Markdown 原文）之間多傳的東西。 */
export interface EditorInnerProps extends RichTextEditorProps {
  /** 編輯器建好時問一次：下載好之前有人要求過焦點嗎（問了就清掉）；下載好之前呼叫了 focus() 的話，編輯器一出現就取得焦點。 */
  readonly takePendingFocus: () => boolean;
  /** 編輯器建好時問一次：這是剛切換編輯方式來的嗎（問了就清掉）；是的話焦點放到這一個、游標在最前面。 */
  readonly takeSwitchFocus: () => boolean;
  /** 工具列右邊的編輯方式切換。 */
  readonly modeControl: EditorModeControl;
}

export type RichTextEditorContentProps = EditorInnerProps;
