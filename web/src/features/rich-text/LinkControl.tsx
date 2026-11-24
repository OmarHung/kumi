import type { Editor } from '@tiptap/core';
import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent, type RefObject } from 'react';
import { Button } from '../../components/Button/Button';
import { TextField } from '../../components/Field/Field';
import { Floating } from '../../components/Floating/Floating';
import { useFloatingDismiss } from '../../components/Floating/useFloatingDismiss';
import { IconButton } from '../../components/IconButton/IconButton';
import { isExternalUrl } from '../../components/Markdown/markdownUrls';
import { isComposingKey } from '../../lib/keyboard';
import styles from './EditorToolbar.module.css';

const LINK_SCHEME_HINT = '只能連到 http、https 或 mailto 的網址';
const LINK_EMPTY = '請輸入網址';

/** 套用連結：選了字就把選取的字變成連結；游標已經在連結裡就改那個連結；什麼都沒選就插入網址本身當連結文字。 */
function applyLink(editor: Editor, href: string): void {
  const chain = editor.chain().focus();
  if (editor.isActive('link')) chain.extendMarkRange('link').setLink({ href }).run();
  else if (editor.state.selection.empty) chain.insertContent({ type: 'text', text: href, marks: [{ type: 'link', attrs: { href } }] }).run();
  else chain.setLink({ href }).run();
}

interface LinkPanelProps {
  readonly editor: Editor;
  readonly onClose: (restoreFocus: boolean) => void;
}

function LinkActions({ isRemovable, onRemove, onSubmit }: { readonly isRemovable: boolean; readonly onRemove: () => void; readonly onSubmit: () => void }) {
  return (
    <div className={styles.actions}>
      {isRemovable && (
        <Button variant="ghost" size="sm" onClick={onRemove}>
          移除連結
        </Button>
      )}
      <Button variant="primary" size="sm" onClick={onSubmit}>
        套用
      </Button>
    </div>
  );
}

/**
 * 不用 <form>：小框掛在呼叫端的 <form>（說明、留言、專案說明）裡面，巢狀的 form 在瀏覽器裡 submit 不會冒泡，
 * 按 Enter 或「套用」會讓整頁照原生方式送出、重新載入。
 */
function LinkPanel({ editor, onClose }: LinkPanelProps) {
  const current = editor.getAttributes('link').href as string | undefined;
  const [href, setHref] = useState(current ?? '');
  const [error, setError] = useState<string | undefined>(undefined);
  const fieldRef = useRef<HTMLInputElement>(null);
  // 在 effect 裡放焦點：Floating 在自己的 layout effect 裡 showPopover，之後才輪到 effect；
  // 更早放（autoFocus）時元素還是 display: none，焦點放不進去，Ctrl+K 之後打的字會進文件。
  useEffect(() => {
    fieldRef.current?.focus({ preventScroll: true });
  }, []);
  const submit = () => {
    const trimmed = href.trim();
    const problem = trimmed === '' ? LINK_EMPTY : isExternalUrl(trimmed) ? undefined : LINK_SCHEME_HINT;
    setError(problem);
    if (problem !== undefined) return;
    applyLink(editor, trimmed);
    onClose(false);
  };
  const remove = () => {
    editor.chain().focus().extendMarkRange('link').unsetLink().run();
    onClose(false);
  };
  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== 'Enter' || isComposingKey(event)) return;
    event.preventDefault();
    event.stopPropagation();
    submit();
  };
  return (
    <div className={styles.form}>
      <TextField
        ref={fieldRef}
        label="網址"
        size="sm"
        value={href}
        inputMode="url"
        autoComplete="off"
        spellCheck={false}
        error={error}
        onKeyDown={handleKeyDown}
        onChange={(event) => {
          setHref(event.target.value);
          setError(undefined);
        }}
      />
      <LinkActions isRemovable={current !== undefined} onRemove={remove} onSubmit={submit} />
    </div>
  );
}

/**
 * 窄螢幕時工具列可以左右捲，連結鈕可能在看不到的地方：小框掛上前先把它捲進來，
 * 不然 Floating 一量就發現錨點看不見、馬上把小框關掉。要排在 Floating 前面（同一次提交裡 layout effect 照順序跑）。
 */
function ScrollAnchorIntoView({ anchorRef }: { readonly anchorRef: RefObject<HTMLElement | null> }) {
  useLayoutEffect(() => {
    // jsdom 沒有 scrollIntoView：用只有這個方法的（可有可無的）型別讀。
    const anchor: { readonly scrollIntoView?: (options: ScrollIntoViewOptions) => void } | null = anchorRef.current;
    anchor?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
  }, [anchorRef]);
  return null;
}

interface LinkControlProps {
  readonly editor: Editor;
  readonly isOpen: boolean;
  readonly onOpenChange: (isOpen: boolean) => void;
  readonly isBusy: boolean;
  readonly isActive: boolean;
  /** 游標在程式碼區塊裡：連結加不上去（程式碼區塊不收任何標記）。 */
  readonly isDisabled: boolean;
  /** 工具列的 roving tabindex 給的屬性。 */
  readonly itemProps: { readonly tabIndex: number; readonly onFocus: () => void };
}

/**
 * 工具列的「連結」與它的浮動小框：網址欄、套用、已經是連結時多「移除連結」。只收 http、https、mailto。
 * 按鈕與小框在同一個外層裡（點按鈕不算點在外面）：Esc 關上、焦點回編輯器；點外面或焦點走掉只關上。
 */
export function LinkControl({ editor, isOpen, onOpenChange, isBusy, isActive, isDisabled, itemProps }: LinkControlProps) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelId = useId();
  const close = useCallback(() => {
    onOpenChange(false);
  }, [onOpenChange]);
  const { rootRef, handleBlur, closeWhenAnchorHidden } = useFloatingDismiss(isOpen, close, triggerRef);
  const closeAndFocus = (restoreFocus: boolean) => {
    close();
    if (restoreFocus) editor.chain().focus().run();
  };
  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Escape' || !isOpen) return;
    event.stopPropagation();
    closeAndFocus(true);
  };
  return (
    <div ref={rootRef} className={styles.link} onKeyDown={handleKeyDown} onBlur={handleBlur}>
      <IconButton
        ref={triggerRef}
        icon="link"
        size="sm"
        label="連結"
        description="連結（Ctrl+K 或 ⌘+K）"
        variant={isActive || isOpen ? 'secondary' : 'ghost'}
        data-toolbar-item=""
        aria-haspopup="dialog"
        aria-expanded={isOpen}
        aria-controls={isOpen ? panelId : undefined}
        aria-pressed={isActive}
        aria-disabled={isBusy || isDisabled || undefined}
        {...itemProps}
        onMouseDown={(event) => {
          event.preventDefault();
        }}
        onClick={() => {
          if (!isBusy && !isDisabled) onOpenChange(!isOpen);
        }}
      />
      {isOpen && <ScrollAnchorIntoView anchorRef={triggerRef} />}
      {isOpen && (
        <Floating anchorRef={triggerRef} align="start" onAnchorHidden={closeWhenAnchorHidden} className={styles.surface}>
          <div id={panelId} role="dialog" aria-label="連結">
            <LinkPanel editor={editor} onClose={closeAndFocus} />
          </div>
        </Floating>
      )}
    </div>
  );
}
