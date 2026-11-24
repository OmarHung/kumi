import type { Editor } from '@tiptap/core';
import { useEditorState } from '@tiptap/react';
import { useRef, useState, type KeyboardEvent } from 'react';
import { IconButton } from '../../components/IconButton/IconButton';
import type { IconName } from '../../components/Icon/Icon';
import { cx } from '../../lib/cx';
import { InsertFileButton } from '../attachments/EditorUploadControls';
import type { EditorUploads } from '../attachments/useEditorUploads';
import { LinkControl } from './LinkControl';
import styles from './EditorToolbar.module.css';

type FormatId = 'bold' | 'italic' | 'strike' | 'code' | 'heading' | 'bulletList' | 'orderedList' | 'taskList' | 'blockquote' | 'codeBlock';

interface FormatAction {
  readonly id: FormatId;
  readonly label: string;
  /** 滑鼠滑過時的說明（含快速鍵）。 */
  readonly description: string;
  readonly icon: IconName;
  readonly isActive: (editor: Editor) => boolean;
  readonly run: (editor: Editor) => void;
}

const ACTIONS: readonly (readonly FormatAction[])[] = [
  [
    { id: 'bold', label: '粗體', description: '粗體（Ctrl+B 或 ⌘+B）', icon: 'bold', isActive: (e) => e.isActive('bold'), run: (e) => e.chain().focus().toggleBold().run() },
    { id: 'italic', label: '斜體', description: '斜體（Ctrl+I 或 ⌘+I）', icon: 'italic', isActive: (e) => e.isActive('italic'), run: (e) => e.chain().focus().toggleItalic().run() },
    { id: 'strike', label: '刪除線', description: '刪除線', icon: 'strike', isActive: (e) => e.isActive('strike'), run: (e) => e.chain().focus().toggleStrike().run() },
    { id: 'code', label: '行內程式碼', description: '行內程式碼', icon: 'inlineCode', isActive: (e) => e.isActive('code'), run: (e) => e.chain().focus().toggleCode().run() },
  ],
  [
    { id: 'heading', label: '標題', description: '標題（再按一次改回段落）', icon: 'heading', isActive: (e) => e.isActive('heading', { level: 2 }), run: (e) => e.chain().focus().toggleHeading({ level: 2 }).run() },
    { id: 'bulletList', label: '項目清單', description: '項目清單', icon: 'bulletList', isActive: (e) => e.isActive('bulletList'), run: (e) => e.chain().focus().toggleBulletList().run() },
    { id: 'orderedList', label: '編號清單', description: '編號清單', icon: 'orderedList', isActive: (e) => e.isActive('orderedList'), run: (e) => e.chain().focus().toggleOrderedList().run() },
    { id: 'taskList', label: '待辦清單', description: '待辦清單', icon: 'taskList', isActive: (e) => e.isActive('taskList'), run: (e) => e.chain().focus().toggleTaskList().run() },
    { id: 'blockquote', label: '引用', description: '引用', icon: 'quote', isActive: (e) => e.isActive('blockquote'), run: (e) => e.chain().focus().toggleBlockquote().run() },
    { id: 'codeBlock', label: '程式碼區塊', description: '程式碼區塊', icon: 'codeBlock', isActive: (e) => e.isActive('codeBlock'), run: (e) => e.chain().focus().toggleCodeBlock().run() },
  ],
];

const ITEM_IDS: readonly string[] = [...ACTIONS.flat().map((action) => action.id), 'link'];

type ActiveFormats = Readonly<Record<string, boolean>>;

function activeFormatsOf(editor: Editor | null): ActiveFormats {
  if (editor === null) return {};
  return { ...Object.fromEntries(ACTIONS.flat().map((action) => [action.id, action.isActive(editor)])), link: editor.isActive('link') };
}

/** 左右鍵、Home、End 在工具列的按鈕之間移動（只有一個停駐點，roving tabindex）；回傳要移到的那一個。 */
function nextItem(event: KeyboardEvent<HTMLElement>, items: readonly HTMLElement[]): HTMLElement | undefined {
  const index = items.findIndex((item) => item === document.activeElement);
  // 小框裡的輸入欄（Home、End 是它自己的）不算：焦點要在工具列的按鈕上。
  if (index < 0) return undefined;
  if (event.key === 'Home') return items[0];
  if (event.key === 'End') return items[items.length - 1];
  if (event.key === 'ArrowRight') return items[(index + 1) % items.length];
  if (event.key === 'ArrowLeft') return items[(index - 1 + items.length) % items.length];
  return undefined;
}

/** 工具列裡的按鍵：左右鍵、Home、End 移動停駐點，其他的（Esc、Ctrl／⌘+Enter）交給 onShortcutKey。 */
function handleToolbarKeys(event: KeyboardEvent<HTMLDivElement>, toolbar: HTMLElement | null, onShortcutKey: (event: KeyboardEvent<HTMLElement>) => void) {
  const items = Array.from(toolbar?.querySelectorAll<HTMLElement>('[data-toolbar-item]') ?? []);
  const target = nextItem(event, items);
  if (target === undefined) {
    onShortcutKey(event);
    return;
  }
  event.preventDefault();
  target.focus();
}

interface EditorToolbarProps {
  readonly editor: Editor;
  readonly isBusy: boolean;
  readonly isLinkOpen: boolean;
  readonly onLinkOpenChange: (isOpen: boolean) => void;
  /** 焦點在工具列時的 Esc 與 Ctrl／⌘+Enter（在內容裡由 ProseMirror 的 handleKeyDown 接）。 */
  readonly onShortcutKey: (event: KeyboardEvent<HTMLElement>) => void;
  /** 能上傳（uploads.canUpload）時最後一組是「插入圖片或檔案」。 */
  readonly uploads?: EditorUploads;
}

/**
 * 格式的工具列（role="toolbar"）：只有一個停駐點，左右鍵、Home、End 移動；按鈕按下去不搶走編輯器的焦點與選取
 * （onMouseDown 擋預設），切換類的有 aria-pressed。分組只用間距。能上傳時最後一組是「插入圖片或檔案」。
 */
export function EditorToolbar({ editor, isBusy, isLinkOpen, onLinkOpenChange, onShortcutKey, uploads }: EditorToolbarProps) {
  const [stop, setStop] = useState<string>(ITEM_IDS[0] ?? 'bold');
  const toolbarRef = useRef<HTMLDivElement>(null);
  const active = useEditorState({ editor, selector: ({ editor: current }) => activeFormatsOf(current) });
  const itemProps = (id: string) => ({
    tabIndex: id === stop ? 0 : -1,
    onFocus: () => {
      setStop(id);
    },
  });
  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    handleToolbarKeys(event, toolbarRef.current, onShortcutKey);
  };
  return (
    <div ref={toolbarRef} role="toolbar" aria-label="格式" aria-orientation="horizontal" className={styles.toolbar} onKeyDown={handleKeyDown}>
      {ACTIONS.map((group, index) => (
        <div key={index} className={styles.group}>
          {group.map((action) => (
            <IconButton
              key={action.id}
              icon={action.icon}
              size="sm"
              label={action.label}
              description={action.description}
              variant={active[action.id] === true ? 'secondary' : 'ghost'}
              data-toolbar-item=""
              aria-pressed={active[action.id] === true}
              aria-disabled={isBusy || undefined}
              {...itemProps(action.id)}
              onMouseDown={(event) => {
                event.preventDefault();
              }}
              onClick={() => {
                if (!isBusy) action.run(editor);
              }}
            />
          ))}
        </div>
      ))}
      <div className={styles.group}>
        <LinkControl editor={editor} isOpen={isLinkOpen} onOpenChange={onLinkOpenChange} isBusy={isBusy} isActive={active.link === true} isDisabled={active.codeBlock === true} itemProps={itemProps('link')} />
      </div>
      {uploads?.canUpload === true && (
        <div className={cx(styles.group, styles.insertGroup)}>
          <InsertFileButton uploads={uploads} isBusy={isBusy} toolbarItem={itemProps('insertFile')} />
        </div>
      )}
    </div>
  );
}
