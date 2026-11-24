import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type ChangeEvent, type CompositionEvent, type FocusEvent, type KeyboardEvent, type SyntheticEvent } from 'react';
import type { Member } from '../../../api/types';
import { mentionEmptyId, mentionOptionId } from './MentionList';
import type { MentionAria } from '../../rich-text/useEditorMention';
import { mentionKeyAction } from './mentionKeys';
import { filterMembers, findMentionTrigger, insertMention, type MentionTrigger } from './mentionText';

interface MentionInputOptions {
  readonly value: string;
  readonly onChange: (value: string) => void;
  /** 可以提及的人（專案的成員）；沒有給就沒有這個功能。 */
  readonly members: readonly Member[] | undefined;
  readonly maxLength: number;
}

export interface MentionHandlers {
  readonly onChange: (event: ChangeEvent<HTMLTextAreaElement>) => void;
  readonly onSelect: (event: SyntheticEvent<HTMLTextAreaElement>) => void;
  /** 回傳 true 表示這個按鍵是清單的，呼叫端不要再處理（Enter 不是換行、Esc 不是取消編輯）。 */
  readonly onKeyDown: (event: KeyboardEvent<HTMLTextAreaElement>) => boolean;
  readonly onCompositionStart: () => void;
  readonly onCompositionEnd: (event: CompositionEvent<HTMLTextAreaElement>) => void;
  readonly onFocus: (event: FocusEvent<HTMLTextAreaElement>) => void;
  readonly onBlur: () => void;
}

export interface MentionInput {
  readonly isOpen: boolean;
  readonly listId: string;
  readonly options: readonly Member[];
  readonly active: number;
  readonly aria: MentionAria;
  /** 給報讀軟體的一句話（打開、換了符合的人數時），放在一直掛著的 aria-live 裡。 */
  readonly announcement: string;
  readonly handlers: MentionHandlers;
  readonly point: (index: number) => void;
  readonly choose: (index: number) => void;
  readonly dismiss: () => void;
}

/** 文字框現在真的有焦點嗎（不靠 onFocus：程式化的 focus、重新掛上時焦點已經在、草稿還原，onFocus 都可能沒來）。 */
function isFocused(textarea: HTMLTextAreaElement): boolean {
  return textarea.ownerDocument.activeElement === textarea;
}

function caretOf(textarea: HTMLTextAreaElement): number | null {
  return textarea.selectionStart === textarea.selectionEnd ? textarea.selectionStart : null;
}

/** 游標前面正在打的提及（沒有、被 Esc 關掉的那個 @、文字框沒有焦點、輸入法選字中都是 null），與改變它的動作。 */
function useTrigger(value: string, hasMembers: boolean) {
  const [caret, setCaret] = useState<number | null>(null);
  const [hasFocus, setHasFocus] = useState(false);
  const [isComposing, setIsComposing] = useState(false);
  const [dismissedAt, setDismissedAt] = useState<number | null>(null);
  const found = hasMembers && hasFocus && !isComposing && caret !== null ? findMentionTrigger(value, caret) : null;
  const trigger = found !== null && found.start !== dismissedAt ? found : null;
  // dismiss 要是固定的函式（Floating 的 onAnchorHidden 在依賴變了時重新訂閱）：看 ref 裡最新的位置。
  const startRef = useRef<number | null>(null);
  useEffect(() => {
    startRef.current = trigger?.start ?? null;
  });
  const dismiss = useCallback(() => {
    if (startRef.current !== null) setDismissedAt(startRef.current);
  }, []);
  return { trigger, setCaret, setHasFocus, setIsComposing, setDismissedAt, dismiss };
}

function announcementFor(isOpen: boolean, count: number): string {
  if (!isOpen) return '';
  return count === 0 ? '沒有符合的成員' : `${String(count)} 位成員可以提及，用上下鍵選擇，Enter 確定`;
}

function ariaFor(isOpen: boolean, listId: string, activeId: string): MentionAria {
  if (!isOpen) return {};
  return { 'aria-controls': listId, 'aria-autocomplete': 'list', 'aria-activedescendant': activeId };
}

/**
 * 文字框裡打 @ 的提及（文字框加 listbox，docs/DESIGN.md 8.3「提及」）：游標前面是 @ 加幾個字、
 * 而且文字框有焦點、不在輸入法選字中，就列出名稱符合的成員。
 * ↑／↓ 選（繞回另一端）、Enter 或 Tab 插入 @[名稱](mention:編號)、Esc 關上（同一個 @ 不再打開，打別的 @ 才再開）。
 * 輸入法選字中不打開，也不攔任何按鍵（mentionKeys.ts）。
 */
export function useMentionInput({ value, onChange, members, maxLength }: MentionInputOptions): MentionInput {
  const listId = useId();
  const state = useTrigger(value, members !== undefined);
  const { trigger, setCaret, dismiss } = state;
  const [activeIndex, setActiveIndex] = useState(0);
  // 焦點進來時記下文字框：插入提及之後要把游標放回去。
  const [textarea, setTextarea] = useState<HTMLTextAreaElement | null>(null);
  // 最近一次插入提及之後游標該在的位置（每次插入是新的物件）。
  const [insertion, setInsertion] = useState<{ readonly caret: number } | null>(null);
  const options = trigger === null || members === undefined ? [] : filterMembers(members, trigger.query);
  const active = Math.min(activeIndex, Math.max(options.length - 1, 0));
  const isOpen = trigger !== null;

  // 插入之後游標放在提及後面：和新的值同一次更新，文字框畫好才放得上去。
  useLayoutEffect(() => {
    if (insertion !== null) textarea?.setSelectionRange(insertion.caret, insertion.caret);
  }, [insertion, textarea]);

  const choose = (index: number) => {
    const member = options[index];
    if (trigger === null || member === undefined) return;
    const inserted = insertMention(value, trigger, member);
    // 放不下（已經很接近字數上限）：不插入、不截斷，關上清單，使用者看得到剩幾個字。
    if (inserted.value.length > maxLength) {
      dismiss();
      return;
    }
    setInsertion({ caret: inserted.caret });
    setCaret(inserted.caret);
    setActiveIndex(0);
    onChange(inserted.value);
  };

  const handlers = buildHandlers({ state, options, active, setActiveIndex, choose, rememberTextarea: setTextarea });
  const activeId = options.length > 0 ? mentionOptionId(listId, active) : mentionEmptyId(listId);
  return {
    isOpen,
    listId,
    options,
    active,
    aria: ariaFor(isOpen, listId, activeId),
    announcement: announcementFor(isOpen, options.length),
    handlers,
    point: setActiveIndex,
    choose,
    dismiss,
  };
}

interface HandlerInputs {
  readonly state: ReturnType<typeof useTrigger>;
  readonly options: readonly Member[];
  readonly active: number;
  readonly setActiveIndex: (index: number) => void;
  readonly choose: (index: number) => void;
  /** 焦點進來時記下文字框：插入提及之後要把游標放回去。 */
  readonly rememberTextarea: (element: HTMLTextAreaElement) => void;
}

function buildHandlers({ state, options, active, setActiveIndex, choose, rememberTextarea }: HandlerInputs): MentionHandlers {
  const { trigger, setCaret, setHasFocus, setIsComposing, setDismissedAt, dismiss } = state;
  return {
    onChange: (event) => {
      const next = caretOf(event.target);
      // 打字就是有焦點：onFocus 沒來也要補上，不然打了 @ 清單不開。
      if (isFocused(event.target)) {
        rememberTextarea(event.target);
        setHasFocus(true);
      }
      setCaret(next);
      setActiveIndex(0);
      // 游標前面不再是提及：之前按 Esc 關掉的那個 @ 也忘掉，之後再打 @ 才會打開。
      if (next === null || findMentionTrigger(event.target.value, next) === null) setDismissedAt(null);
    },
    onSelect: (event) => {
      if (isFocused(event.currentTarget)) {
        rememberTextarea(event.currentTarget);
        setHasFocus(true);
      }
      setCaret(caretOf(event.currentTarget));
    },
    onKeyDown: (event) => {
      const action = trigger === null ? null : mentionKeyAction(event.nativeEvent, options.length > 0);
      if (action === null) return false;
      // Esc 只是關清單：說明的編輯器在 Esc 時取消編輯，所以不能再往外傳。
      event.preventDefault();
      if (action === 'dismiss') event.stopPropagation();
      if (action === 'dismiss') dismiss();
      else if (action === 'select') choose(active);
      else setActiveIndex((active + (action === 'next' ? 1 : -1) + options.length) % options.length);
      return true;
    },
    onCompositionStart: () => {
      setIsComposing(true);
    },
    onCompositionEnd: (event) => {
      setIsComposing(false);
      setCaret(caretOf(event.currentTarget));
    },
    onFocus: (event) => {
      rememberTextarea(event.currentTarget);
      setHasFocus(true);
    },
    onBlur: () => {
      setHasFocus(false);
    },
  };
}

export type { MentionTrigger };
