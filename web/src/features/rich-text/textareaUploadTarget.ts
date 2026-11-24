import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react';
import { insertText, replaceWithCaret } from '../attachments/editorUploadText';
import type { EditorUploads, UploadPlaceholderSpec, UploadTarget } from '../attachments/useEditorUploads';
import { useLatest } from './useLatest';

interface Caret {
  readonly start: number;
  readonly end: number;
}

interface TargetEnvironment {
  readonly textarea: () => HTMLTextAreaElement | null;
  readonly value: () => string;
  /** 換字、插入之後的新文字；游標之後在畫面更新時放回去。 */
  readonly commit: (value: string, caret: Caret | null) => void;
  /** 輸入法組字中先不做的事。 */
  readonly defer: (job: () => void) => void;
}

/**
 * 有圖片時記號自成一行（和所見即所得的編輯器一樣）：游標前面同一行有字就先換行，後面還有字就再換一行；
 * 圖片和字擠在同一行時，顯示出來是一張大圖接在句子後面。檔案只有連結，留在行內。
 */
function textToInsert(value: string, start: number, end: number, specs: readonly UploadPlaceholderSpec[]): string {
  const text = specs.map((spec) => spec.placeholder).join('\n');
  if (!specs.some((spec) => spec.isImage)) return text;
  const needsBefore = start > 0 && value[start - 1] !== '\n';
  const needsAfter = end < value.length && value[end] !== '\n';
  return `${needsBefore ? '\n' : ''}${text}${needsAfter ? '\n' : ''}`;
}

function createTarget(environment: TargetEnvironment): UploadTarget {
  const caretOf = (textarea: HTMLTextAreaElement | null): Caret => ({ start: textarea?.selectionStart ?? 0, end: textarea?.selectionEnd ?? 0 });
  const replaceNow = (placeholder: string, replacement: string) => {
    const textarea = environment.textarea();
    const caret = caretOf(textarea);
    const result = replaceWithCaret(environment.value(), placeholder, replacement, caret.start, caret.end);
    if (result === null) return;
    // 沒有焦點時不動游標：有些瀏覽器的 setSelectionRange 會順便把焦點搶過來。
    const hasFocus = textarea !== null && document.activeElement === textarea;
    environment.commit(result.value, hasFocus ? { start: result.start, end: result.end } : null);
  };
  return {
    insertPlaceholders: (specs) => {
      const caret = caretOf(environment.textarea());
      const value = environment.value();
      const start = Math.min(caret.start, caret.end);
      const end = Math.max(caret.start, caret.end);
      const inserted = insertText(value, start, end, textToInsert(value, start, end, specs));
      environment.commit(inserted.value, { start: inserted.caret, end: inserted.caret });
    },
    // 換字會中斷輸入法選字，組字中先不換；換的時候再找一次記號（文字可能已經變了）。使用者已經刪掉的記號不補回去。
    replacePlaceholder: (placeholder, replacement) => {
      if (!environment.value().includes(placeholder)) return false;
      environment.defer(() => {
        replaceNow(placeholder, replacement);
      });
      return true;
    },
  };
}

/** 輸入法組字中（compositionstart 到 compositionend）先不做的事：組字結束才跑；沒有在組字就馬上跑。 */
function useDeferWhileComposing(textareaRef: RefObject<HTMLTextAreaElement | null>): (job: () => void) => void {
  const isComposing = useRef(false);
  const deferred = useRef<readonly (() => void)[]>([]);
  useEffect(() => {
    const textarea = textareaRef.current;
    if (textarea === null) return undefined;
    const start = () => {
      isComposing.current = true;
    };
    const end = () => {
      isComposing.current = false;
      // 有的瀏覽器 compositionend 比最後一次 input 早：等文字進了狀態再跑。
      setTimeout(() => {
        const jobs = deferred.current;
        deferred.current = [];
        jobs.forEach((job) => {
          job();
        });
      }, 0);
    };
    textarea.addEventListener('compositionstart', start);
    textarea.addEventListener('compositionend', end);
    return () => {
      textarea.removeEventListener('compositionstart', start);
      textarea.removeEventListener('compositionend', end);
    };
  }, [textareaRef]);
  return (job) => {
    if (isComposing.current) deferred.current = [...deferred.current, job];
    else job();
  };
}

interface Options {
  readonly textareaRef: RefObject<HTMLTextAreaElement | null>;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly uploads: EditorUploads | undefined;
}

/**
 * Markdown 原文的文字框掛上時把自己註冊成上傳的目標（記號放在游標處、傳完換成參照），卸下時拿掉。
 * 換字用最新的文字（傳完時使用者可能已經又打了字），游標跟著平移：整串替換會讓瀏覽器把游標丟到最後面。
 */
export function useTextareaUploadTarget({ textareaRef, value, onChange, uploads }: Options): void {
  const attach = uploads?.binding.attach;
  const latestValue = useRef(value);
  const pendingCaret = useRef<Caret | null>(null);
  const onChangeLatest = useLatest(onChange);
  const defer = useDeferWhileComposing(textareaRef);
  const deferLatest = useLatest(defer);
  useLayoutEffect(() => {
    latestValue.current = value;
    // 文字換掉、畫面更新之後（瀏覽器這時已經把游標移到最後）放回去；不用 requestAnimationFrame：晚到的那一次會蓋掉使用者之後移動的游標。
    const caret = pendingCaret.current;
    if (caret === null) return;
    pendingCaret.current = null;
    textareaRef.current?.setSelectionRange(caret.start, caret.end);
  });
  useEffect(() => {
    if (attach === undefined) return undefined;
    attach(
      createTarget({
        textarea: () => textareaRef.current,
        // 連著兩次換字時，第二次要看得到第一次的結果（畫面還沒更新）。
        value: () => latestValue.current,
        commit: (next, caret) => {
          latestValue.current = next;
          pendingCaret.current = caret;
          onChangeLatest.get()(next);
        },
        defer: (job) => {
          deferLatest.get()(job);
        },
      }),
    );
    return () => {
      attach(null);
    };
  }, [attach, textareaRef, onChangeLatest, deferLatest]);
}
