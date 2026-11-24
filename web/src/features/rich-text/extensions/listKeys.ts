import type { Extension } from '@tiptap/core';
import { ListItem, TaskItem } from '@tiptap/extension-list';
import type { EditorState } from '@tiptap/pm/state';

const LIST_ITEM_NAMES: readonly string[] = ['listItem', 'taskItem'];

/** 游標在巢狀的清單項目裡（外面還有一層項目）：這時 Shift+Tab 才有「往外提一層」可做。 */
export function isNestedListItem(state: EditorState): boolean {
  const { $from } = state.selection;
  let count = 0;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if (LIST_ITEM_NAMES.includes($from.node(depth).type.name)) count += 1;
  }
  return count > 1;
}

/** Tiptap 的鍵盤快速鍵表。 */
type Shortcuts = ReturnType<NonNullable<Extension['config']['addKeyboardShortcuts']>>;

/**
 * Tiptap 的 Shift+Tab 會把第一層的項目提出清單、變回一般段落，鍵盤使用者就此離不開編輯器
 * （留言框沒有 Esc 可以離開，WCAG 2.1.2）。只有巢狀的項目才處理（往外提一層），第一層的放行：
 * 瀏覽器把焦點移到上一個控制項。其他的快速鍵照 Tiptap。
 */
function withSafeShiftTab(parent: Shortcuts | undefined, state: () => EditorState, lift: () => boolean): Shortcuts {
  return { ...parent, 'Shift-Tab': () => isNestedListItem(state()) && lift() };
}

export const EditorListItem = ListItem.extend({
  addKeyboardShortcuts() {
    return withSafeShiftTab(
      this.parent?.(),
      () => this.editor.state,
      () => this.editor.commands.liftListItem(this.name),
    );
  },
});

export const EditorTaskItem = TaskItem.extend({
  addKeyboardShortcuts() {
    return withSafeShiftTab(
      this.parent?.(),
      () => this.editor.state,
      () => this.editor.commands.liftListItem(this.name),
    );
  },
});
