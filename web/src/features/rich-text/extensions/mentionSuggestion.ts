import { PluginKey, type EditorState } from '@tiptap/pm/state';
import type { ResolvedPos } from '@tiptap/pm/model';
import type { Range } from '@tiptap/core';
import type { MentionNodeAttrs } from '@tiptap/extension-mention';
import type { SuggestionMatch, SuggestionOptions, SuggestionProps } from '@tiptap/suggestion';
import type { Member } from '../../../api/types';
import { filterMembers, findMentionTrigger } from '../../item-page/mentions/mentionText';

/** 選了成員之後交給 Mention 擴充插入 mention 節點的屬性（id 是人員編號，label 是名稱）。 */
export type MentionChoice = MentionNodeAttrs;

export type MentionSuggestionProps = SuggestionProps<Member, MentionChoice>;

/** 提及的建議清單和編輯器之間的橋：Tiptap 的 Suggestion 外掛呼叫它，它把狀態交給 React（useEditorMention）。 */
export interface MentionBridge {
  getMembers(): readonly Member[] | undefined;
  /** 送出中不開。 */
  isBlocked(): boolean;
  start(props: MentionSuggestionProps): void;
  update(props: MentionSuggestionProps): void;
  exit(): void;
  /** 清單開著時的按鍵；回傳 true 表示這個按鍵是清單的。 */
  keyDown(event: KeyboardEvent, range: Range): boolean;
}

/** 按 Esc 關掉的那個 @ 的位置：同一個 @ 不再打開，打別的 @ 才再開。 */
export interface DismissedMention {
  from: number | null;
}

export const mentionPluginKey = new PluginKey('mention');

/**
 * 游標前面是不是正在打提及：規矩和文字框一樣（mentionText.ts 的 findMentionTrigger：Email 不觸發、空白最多一個）。
 * 行內的非文字節點（換行、圖片、前一個提及）當成換行，查詢不會跨過去。
 */
function findMatch($position: ResolvedPos, dismissed: DismissedMention): SuggestionMatch {
  const { parent } = $position;
  const before = parent.isTextblock ? parent.textBetween(0, $position.parentOffset, undefined, '\n') : '';
  const trigger = findMentionTrigger(before, before.length);
  if (trigger === null) {
    dismissed.from = null;
    return null;
  }
  const from = $position.pos - trigger.query.length - 1;
  if (dismissed.from !== null && dismissed.from !== from) dismissed.from = null;
  return { range: { from, to: $position.pos }, query: trigger.query, text: `@${trigger.query}` };
}

/** 這個位置放得下提及節點，而且不是在行內程式碼裡。 */
function acceptsMention(state: EditorState, position: number): boolean {
  const $position = state.doc.resolve(position);
  const mention = state.schema.nodes.mention;
  if (mention === undefined || $position.parent.type.contentMatch.matchType(mention) === null) return false;
  return !$position.marks().some((mark) => mark.type.name === 'code');
}

/** Mention 擴充的 suggestion 選項：觸發的規矩沿用文字框，清單的畫面與按鍵由 bridge 交給 React。 */
export function createMentionSuggestion(bridge: MentionBridge, dismissed: DismissedMention): Omit<SuggestionOptions<Member, MentionChoice>, 'editor'> {
  return {
    pluginKey: mentionPluginKey,
    char: '@',
    allowedPrefixes: null,
    findSuggestionMatch: ({ $position }) => findMatch($position, dismissed),
    items: ({ query }) => [...filterMembers(bridge.getMembers() ?? [], query)],
    allow: ({ state, range }) => bridge.getMembers() !== undefined && !bridge.isBlocked() && acceptsMention(state, range.from),
    // 輸入法選字中不開（選字用的按鍵是輸入法的）；選字結束後 RichTextEditorContent 補一個空的交易，這裡才會再看一次。
    shouldShow: ({ editor, range }) => !editor.view.composing && range.from !== dismissed.from,
    render: () => ({
      onStart: (props) => {
        if (!props.loading) bridge.start(props);
      },
      onUpdate: (props) => {
        if (!props.loading) bridge.update(props);
      },
      onExit: () => {
        bridge.exit();
      },
      onKeyDown: ({ event, range }) => bridge.keyDown(event, range),
    }),
  };
}
