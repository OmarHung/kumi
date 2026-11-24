import { Component, lazy, Suspense, useId, useImperativeHandle, useRef, useState, type ReactNode } from 'react';
import { Button } from '../../components/Button/Button';
import { setEditorMode, useEditorMode, type EditorMode } from '../../lib/editorMode';
import { UPLOADING_REASON } from '../attachments/useEditorUploads';
import { EditorFrame } from './EditorFrame';
import { EditorToolbarBar, type EditorModeControl } from './EditorToolbarBar';
import { countNoteOf, formatHintOf } from './fieldText';
import { MarkdownSourceEditor } from './MarkdownSourceEditor';
import type { RichTextEditorHandle, RichTextEditorProps } from './types';
import styles from './RichTextEditor.module.css';

export type { RichTextEditorHandle, RichTextEditorProps } from './types';

export const SAVING_REASON = '送出中，暫時不能切換';

export const SUBMIT_SHORTCUT = 'Ctrl+Enter 或 ⌘+Enter';

// Tiptap（ProseMirror）那一包只在打開編輯器時下載：主程式檔不變大。
const RichTextEditorContent = lazy(() => import('./RichTextEditorContent').then((module) => ({ default: module.RichTextEditorContent })));

function IGNORE_KEY(): void {
  // 下載好之前沒有提交與取消的處理：按鍵照常。
}

/**
 * 備用畫面（下載好之前、下載失敗）：同一個外框、同樣的最少行高與工具列的位置，編輯器出現時版面不跳。
 * 提示、字數、錯誤照常顯示（呼叫端的測試與使用者在下載的瞬間也看得到同樣的東西）。
 */
function FallbackFrame({ props, control, children }: { readonly props: RichTextEditorProps; readonly control: EditorModeControl; readonly children: ReactNode }) {
  const id = useId();
  return (
    <EditorFrame
      id={id}
      label={props.label}
      isLabelHidden={props.isLabelHidden ?? false}
      rows={props.rows}
      hint={props.hint}
      formatHint={formatHintOf(props.mentionMembers !== undefined, props.uploads?.binding.canUpload === true)}
      count={countNoteOf(props.value.length, props.maxLength)}
      error={props.error}
      isCollapsed={props.isCollapsed ?? false}
      isDropping={false}
      isBusy={props.isBusy}
      fillsViewport={props.fillsViewport}
      toolbar={<EditorToolbarBar control={control} onShortcutKey={IGNORE_KEY} />}
    >
      {children}
    </EditorFrame>
  );
}

/** 下載好之前：原文的純文字，唯讀（和 Markdown 的備用畫面同一個做法）。 */
function LoadingFrame({ props, control }: { readonly props: RichTextEditorProps; readonly control: EditorModeControl }) {
  return (
    <FallbackFrame props={props} control={control}>
      <div className={styles.plain}>{props.value}</div>
    </FallbackFrame>
  );
}

/** 下載失敗（重新部署之後，舊分頁要的檔案已經不在了）：草稿已經存在分頁裡，重新整理不會不見。 */
function FailureFrame({ props, control }: { readonly props: RichTextEditorProps; readonly control: EditorModeControl }) {
  return (
    <FallbackFrame props={props} control={control}>
      <div className={styles.failure} role="alert">
        <span>編輯器載入失敗，重新整理頁面再試一次。</span>
        <Button
          size="sm"
          variant="secondary"
          onClick={() => {
            window.location.reload();
          }}
        >
          重新整理
        </Button>
      </div>
    </FallbackFrame>
  );
}

interface LoadBoundaryProps {
  readonly fallback: ReactNode;
  readonly children: ReactNode;
}

class LoadBoundary extends Component<LoadBoundaryProps, { readonly hasFailed: boolean }> {
  override state = { hasFailed: false };

  static getDerivedStateFromError() {
    return { hasFailed: true };
  }

  override render() {
    return this.state.hasFailed ? this.props.fallback : this.props.children;
  }
}

/** 暫時不能切換編輯方式的原因：上傳中的記號在所見即所得裡是節點，切過去再切回來就換不掉了；送出中內容不能動。 */
function switchDisabledReason(props: RichTextEditorProps): string | null {
  if (props.uploads?.isUploading === true) return UPLOADING_REASON;
  return props.isBusy ? SAVING_REASON : null;
}

/** 一個只認得「剛切換過」的旗標：新的編輯器掛上時問一次，問了就清掉。 */
function useSwitchFocus() {
  const isSwitchedRef = useRef(false);
  const [take] = useState(() => () => {
    const isSwitched = isSwitchedRef.current;
    isSwitchedRef.current = false;
    return isSwitched;
  });
  const [markSwitched] = useState(() => () => {
    isSwitchedRef.current = true;
  });
  return { markSwitched, take };
}

function useModeControl(props: RichTextEditorProps, markSwitched: () => void): EditorModeControl {
  const mode = useEditorMode();
  return {
    mode,
    disabledReason: switchDisabledReason(props),
    onChange: (next: EditorMode) => {
      markSwitched();
      setEditorMode(next);
    },
  };
}

/**
 * 說明與留言的 Markdown 編輯器（docs/DESIGN.md 7.4）：存的還是 Markdown。編輯方式是這個瀏覽器的偏好（lib/editorMode.ts）：
 * 所見即所得（預設）：真正的編輯器延後載入（RichTextEditorContent）；下載好之前顯示同樣大小的外框與原文，失敗顯示說明與「重新整理」。
 * Markdown 原文：直接畫文字框，不下載 Tiptap。
 * 下載好之前呼叫 focus() 的話，編輯器一出現就取得焦點。
 */
export function RichTextEditor(props: RichTextEditorProps) {
  const { ref, ...rest } = props;
  const innerRef = useRef<RichTextEditorHandle>(null);
  const wantsFocusRef = useRef(false);
  useImperativeHandle(
    ref,
    () => ({
      focus: () => {
        if (innerRef.current === null) wantsFocusRef.current = true;
        else innerRef.current.focus();
      },
    }),
    [],
  );
  const [takePendingFocus] = useState(() => () => {
    const wants = wantsFocusRef.current;
    wantsFocusRef.current = false;
    return wants;
  });
  const switched = useSwitchFocus();
  const modeControl = useModeControl(props, switched.markSwitched);
  const inner = { ...rest, takePendingFocus, takeSwitchFocus: switched.take, modeControl };
  if (modeControl.mode === 'markdown') return <MarkdownSourceEditor {...inner} ref={innerRef} />;
  return (
    <LoadBoundary fallback={<FailureFrame props={props} control={modeControl} />}>
      <Suspense fallback={<LoadingFrame props={props} control={modeControl} />}>
        <RichTextEditorContent {...inner} ref={innerRef} />
      </Suspense>
    </LoadBoundary>
  );
}
