import { useEffect, useRef, useState, type RefObject } from 'react';

/** 例子按下之後要填進輸入框的字；換了 nonce 就是新的一次。 */
export type DraftFill = { readonly text: string; readonly nonce: number } | null;

const NEW_DRAFT_KEY = '';

interface DraftState {
  readonly key: string;
  readonly drafts: Readonly<Record<string, string>>;
  readonly fillNonce: number;
}

/** 送出的那段字從草稿拿掉：草稿還是那段字就清空；等待時又接著打的留下；改過了就整份留著。 */
function withoutSent(draft: string, sent: string): string {
  if (draft === sent) return '';
  return draft.startsWith(sent) ? draft.slice(sent.length) : draft;
}

export interface Drafts {
  readonly key: string;
  readonly text: string;
  readonly setText: (text: string) => void;
  /** 伺服器收下了：清掉送出當下的那段字（sentKey 是送出時的草稿）。 */
  readonly clearSent: (sentKey: string, sent: string) => void;
}

/** 換了對話：從「新的對話」送出而開出來的那一則，草稿跟著過去；其他的各自一份。 */
function switchedTo(state: DraftState, key: string, createdId: string | null): DraftState {
  const carried = state.key === NEW_DRAFT_KEY && key === createdId ? (state.drafts[NEW_DRAFT_KEY] ?? '') : null;
  const drafts = carried === null ? state.drafts : { ...state.drafts, [NEW_DRAFT_KEY]: '', [key]: carried };
  return { ...state, key, drafts };
}

/**
 * 輸入框的字，每個對話一份。例子填進來：換了 nonce 就用它的文字。
 * 在渲染時調整狀態（不必 effect 再跑一輪）。從「新的對話」送出時網址換成剛開的對話（createdId）：
 * 草稿跟著過去，送出失敗字才留在眼前。
 */
export function useDrafts(conversationId: string | undefined, fill: DraftFill, createdId: string | null): Drafts {
  const key = conversationId ?? NEW_DRAFT_KEY;
  const [state, setState] = useState<DraftState>({ key, drafts: {}, fillNonce: fill?.nonce ?? 0 });
  if (state.key !== key) {
    setState(switchedTo(state, key, createdId));
  } else if (fill !== null && fill.nonce !== state.fillNonce) {
    setState({ ...state, drafts: { ...state.drafts, [key]: fill.text }, fillNonce: fill.nonce });
  }
  return {
    key,
    text: state.drafts[key] ?? '',
    setText: (text) => {
      setState((current) => ({ ...current, drafts: { ...current.drafts, [current.key]: text } }));
    },
    clearSent: (sentKey, sent) => {
      setState((current) => {
        // 從「新的對話」送出、網址已經換過去：字在現在這一則的草稿裡。
        const keys = sentKey === NEW_DRAFT_KEY ? [sentKey, current.key] : [sentKey];
        const drafts = { ...current.drafts };
        keys.forEach((name) => {
          drafts[name] = withoutSent(drafts[name] ?? '', sent);
        });
        return { ...current, drafts };
      });
    },
  };
}

/** 回覆結束或停止：焦點原本在送出、停止鈕（換掉就掉到頁面本體）或頁面本體，就回到輸入框；在別處（連結、對話框）不動。 */
export function useRestoreFocus(
  isStreaming: boolean,
  formRef: RefObject<HTMLFormElement | null>,
  inputRef: RefObject<HTMLTextAreaElement | null>,
): void {
  const wasStreaming = useRef(false);
  useEffect(() => {
    if (wasStreaming.current && !isStreaming) {
      const active = document.activeElement;
      const isLost = active === null || active === document.body || (formRef.current?.contains(active) === true && active !== inputRef.current);
      if (isLost) inputRef.current?.focus();
    }
    wasStreaming.current = isStreaming;
  }, [formRef, inputRef, isStreaming]);
}
