import { EditorContent } from '@tiptap/react';
import { useCallback, useEffect, useId, useImperativeHandle, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { attachmentContentUrl } from '../../api/attachments';
import { EditorFrame } from './EditorFrame';
import { dragHandlersOf, MentionOverlay } from './EditorOverlays';
import { EditorToolbar } from './EditorToolbar';
import { EditorToolbarBar } from './EditorToolbarBar';
import { handleToolbarKeyDown } from './editorInteractions';
import { EditorEnvironmentContext } from './editorContext';
import { countNoteOf, describedByIds, formatHintOf } from './fieldText';
import type { RichTextEditorContentProps } from './types';
import { useEditorMention } from './useEditorMention';
import { useRichTextEditor } from './useRichTextEditor';
import { useUploadTarget } from './uploadTarget';

/** Tiptap 的編輯器加上它的周邊：提及、連結的小框、上傳的目標、焦點的把手。 */
function useContentEditor(props: RichTextEditorContentProps, id: string, count: ReturnType<typeof countNoteOf>) {
  const { value, onChange, placeholder, onSubmit, onCancel, isBusy, error, autoFocus = false, isCollapsed = false, mentionMembers, uploads, takePendingFocus, takeSwitchFocus, ref } = props;
  const [isLinkOpen, setIsLinkOpen] = useState(false);
  const mention = useEditorMention({ members: mentionMembers, isBusy });
  const openLink = useCallback(() => {
    setIsLinkOpen(true);
  }, []);
  const editor = useRichTextEditor({
    value,
    onChange,
    placeholder,
    autoFocus,
    isBusy,
    mention,
    interactions: { onSubmit, onCancel, isBusy, isCollapsed, isMentionOpen: mention.isOpenNow, openLink, uploads },
    attributes: {
      labelId: `${id}-label`,
      describedBy: describedByIds(id, count !== null, error !== null),
      isInvalid: error !== null || count?.isOver === true,
      mentionAria: mention.aria,
    },
  });
  useUploadTarget(editor, uploads);
  // 開發模式的 StrictMode 會把 effect 跑兩次、Tiptap 也會重建編輯器：旗標只在掛上時問一次記起來，不在 effect 裡問（第一次問掉了，第二次的新編輯器就沒有焦點）。
  const [isSwitched] = useState(takeSwitchFocus);
  useEffect(() => {
    // StrictMode 的第一輪 effect 看到的編輯器已經被 Tiptap 銷毀了：等重建的那一個。
    if (editor === null || editor.isDestroyed) return;
    // 剛從 Markdown 原文切過來：游標在最前面（原文的游標位置換算不過來）；否則下載好之前有人要求過焦點才放到最後。
    // 編輯器的元素這時還沒掛進頁面（editor.view 還不能用），所以用 Tiptap 的 focus 指令，掛好之後（下一個畫面）才放上去。
    if (isSwitched) editor.commands.focus('start');
    else if (takePendingFocus()) editor.commands.focus('end');
  }, [editor, isSwitched, takePendingFocus]);
  useImperativeHandle(
    ref,
    () => ({
      // 同步放焦點：呼叫端在事件裡先把焦點搬回來，下一行才讓按鈕停用或消失；Tiptap 的 focus 指令會等到下一個畫面才動，那一瞬間焦點已經掉到頁面本體。
      focus: () => {
        if (editor === null || editor.isDestroyed) return;
        editor.view.focus();
      },
    }),
    [editor],
  );
  return { editor, mention, isLinkOpen, setIsLinkOpen };
}

/**
 * 所見即所得的 Markdown 編輯器本體（Tiptap；只經過 RichTextEditor.tsx 延後載入，主程式檔不帶它）。
 * 存的還是 Markdown：讀寫都走 ./markdown/markdownDoc（沒改回傳原文，見 useMarkdownSync）。
 */
export function RichTextEditorContent(props: RichTextEditorContentProps) {
  const { label, isLabelHidden = false, value, maxLength, rows, hint, onSubmit, onCancel, isBusy, error, isCollapsed = false, mentionMembers, uploads, hasAttachments = true, attachmentUrl = attachmentContentUrl, fillsViewport = false, modeControl } = props;
  const id = useId();
  // 值要穩定：節點畫面讀這個 Context，每次重畫都換新物件會讓圖片全部重畫。
  const environment = useMemo(() => ({ hasAttachments, attachmentUrl }), [hasAttachments, attachmentUrl]);
  const frameRef = useRef<HTMLDivElement>(null);
  const count = countNoteOf(value.length, maxLength);
  const { editor, mention, isLinkOpen, setIsLinkOpen } = useContentEditor(props, id, count);
  const dragHandlers = dragHandlersOf(uploads, isBusy);
  const onShortcutKey = (event: KeyboardEvent<HTMLElement>) => {
    handleToolbarKeyDown({ onSubmit, onCancel }, event);
  };
  return (
    <EditorEnvironmentContext value={environment}>
      <EditorFrame
        id={id}
        label={label}
        isLabelHidden={isLabelHidden}
        rows={rows}
        hint={hint}
        formatHint={formatHintOf(mentionMembers !== undefined, uploads?.binding.canUpload === true)}
        count={count}
        error={error}
        isCollapsed={isCollapsed}
        isDropping={uploads?.binding.isDragging === true}
        isBusy={isBusy}
        fillsViewport={fillsViewport}
        frameRef={frameRef}
        dragHandlers={dragHandlers}
        onLabelClick={() => editor?.commands.focus()}
        onScrollClick={() => editor?.commands.focus('end')}
        toolbar={
          <EditorToolbarBar control={modeControl} onShortcutKey={onShortcutKey}>
            {editor !== null && <EditorToolbar editor={editor} isBusy={isBusy} isLinkOpen={isLinkOpen} onLinkOpenChange={setIsLinkOpen} onShortcutKey={onShortcutKey} uploads={uploads} />}
          </EditorToolbarBar>
        }
        overlay={<MentionOverlay mention={mention} anchorRef={frameRef} />}
      >
        <EditorContent editor={editor} />
      </EditorFrame>
    </EditorEnvironmentContext>
  );
}
