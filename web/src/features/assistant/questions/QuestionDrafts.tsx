import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';

/** 一題作答到一半的內容。 */
export interface DraftAnswer {
  readonly labels: readonly string[];
  readonly isOtherOn: boolean;
  readonly otherText: string;
  readonly isSkipped: boolean;
}

export const EMPTY_DRAFT: DraftAnswer = { labels: [], isOtherOn: false, otherText: '', isSkipped: false };

/** 一張卡片作答到哪裡：每題的草稿與目前的題號。 */
export interface CardProgress {
  readonly drafts: readonly DraftAnswer[];
  readonly index: number;
}

interface Store {
  readonly progress: Readonly<Record<string, CardProgress>>;
  readonly save: (key: string, next: CardProgress) => void;
}

const DraftContext = createContext<Store | null>(null);

/**
 * 問題卡片作答到一半的內容放在卡片外面（依助理訊息的編號）：回覆被接受又失敗、
 * 卡片在唯讀與可作答之間來回時，卡片會整個換掉，寫到一半的答案不能跟著不見。
 */
export function QuestionDraftProvider({ children }: { readonly children: ReactNode }) {
  const [progress, setProgress] = useState<Readonly<Record<string, CardProgress>>>({});
  const store = useMemo<Store>(
    () => ({
      progress,
      save: (key, next) => {
        setProgress((current) => ({ ...current, [key]: next }));
      },
    }),
    [progress],
  );
  return <DraftContext.Provider value={store}>{children}</DraftContext.Provider>;
}

/** 沒有 Provider（或沒給 key）時退回卡片自己的狀態。 */
export function useCardProgress(key: string | undefined, questionCount: number): readonly [CardProgress, (next: CardProgress) => void] {
  const store = useContext(DraftContext);
  const [local, setLocal] = useState<CardProgress>({ drafts: [], index: 0 });
  const stored = store !== null && key !== undefined ? store.progress[key] : undefined;
  const current = stored ?? local;
  const isUsable = current.drafts.length === questionCount && current.index < questionCount;
  const progress = isUsable ? current : { drafts: Array.from({ length: questionCount }, () => EMPTY_DRAFT), index: 0 };
  const save = (next: CardProgress) => {
    if (store !== null && key !== undefined) store.save(key, next);
    else setLocal(next);
  };
  return [progress, save];
}
