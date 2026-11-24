import { useEffect, useRef, useState, type RefObject } from 'react';
import { useDraft } from '../../lib/useDraft';
import type { OutdatedDraft } from './RestoredDraftNotice';

const OUTDATED_DRAFT: OutdatedDraft = {
  message: '說明在你離開之後被別人改過；這是你上次沒送出的內容，儲存會蓋掉新的說明。',
  discardText: '看伺服器上的內容',
};

/**
 * 說明的草稿（項目的說明、專案的說明共用）：只在和伺服器上的內容不同時才存，連同打草稿時伺服器上的說明（base）一起存（lib/useDraft.ts 的 withBase）。
 * 還原時伺服器上的說明已經和 base 不同，就是別人在你離開之後改過：照樣還原自己的版本，但說明儲存會蓋掉新的（outdated）。
 * key 是 lib/draftStorage.ts 的 draftKey；server 是伺服器現在的說明（沒有說明是空字串）。
 * 項目的說明還帶說明的版本（serverRevision）：開始編輯時記下，儲存時送出（expectedDescriptionRevision），別人改過就被伺服器擋下。
 * 還原的草稿即使已經過時（outdated），版本也是現在的：使用者看過提示、照提示蓋掉。專案的說明沒有版本。
 */
export function useServerBasedDraft(key: string, server: string, serverRevision?: string) {
  const stored = useDraft(key, { isWorthKeeping: (value) => value !== server, withBase: true });
  // 這一次編輯是在哪一個伺服器內容上打的：還原時是草稿記下的（舊的純文字草稿不知道，當成現在的），開始編輯或捨棄時是當時的內容。
  const [base, setBase] = useState(stored.restoredBase ?? server);
  const [revision, setRevision] = useState(serverRevision);
  const isOutdated = stored.isRestored && stored.restoredBase !== undefined && stored.restoredBase !== server;
  return {
    restored: stored.restored,
    isRestored: stored.isRestored,
    outdated: isOutdated ? OUTDATED_DRAFT : null,
    save: (value: string) => {
      stored.save(value, base);
    },
    clear: stored.clear,
    /** 這一次編輯開始時說明的版本（沒有版本的說明是 undefined）。 */
    revision,
    /**
     * 開始編輯、捨棄草稿：之後打的是在伺服器現在的內容上。
     * latest：伺服器現在的內容與版本，還沒傳到這個元件時（剛重抓回來）由呼叫端給；沒給就用這次渲染拿到的。
     */
    rebase: (latest?: { readonly text: string; readonly revision: string }) => {
      setBase(latest?.text ?? server);
      setRevision(latest?.revision ?? serverRevision);
    },
  };
}

/**
 * 結束編輯（draft 變回 null）之後焦點回到入口，只在呼叫了回傳的函式時（還原、捨棄不搬焦點）。
 * 入口照結束之後有沒有說明：有說明是「編輯」，沒有（存成空白、本來就沒有）是「新增」那一塊；
 * 兩個不會同時出現，同一個 ref 指到畫出來的那一個。存好之後內容可能比「結束編輯」晚一輪才換（快取的通知是排程的），
 * 所以記下該回到哪一種入口、等畫面換成它才放焦點：先放到舊的入口的話，它跟著被換掉，焦點會掉到頁面本體。
 */
export function useRefocusAfterClose(draft: string | null, hasDescription: boolean, entryRef: RefObject<HTMLButtonElement | null>) {
  const expected = useRef<boolean | null>(null);
  useEffect(() => {
    if (draft === null && expected.current === hasDescription) {
      expected.current = null;
      entryRef.current?.focus();
    }
  }, [draft, hasDescription, entryRef]);
  return (expectsDescription: boolean) => {
    expected.current = expectsDescription;
  };
}

/** 整段空白等於清掉；其他的照使用者打的存，換行與縮排都保留。 */
export function descriptionValue(draft: string): string | null {
  return draft.trim() === '' ? null : draft;
}

/**
 * 儲存草稿（項目與專案的說明共用）：沒改（和現在的說明一樣）就直接結束編輯、不送；否則交給 submit 送出，
 * 成功後用伺服器回來的說明呼叫 done（就是 close，決定焦點回到哪一個入口）。
 */
export function submitDescription(
  draft: string,
  current: string | null,
  submit: (next: string | null, done: (saved: string | null) => void) => void,
  close: (saved: string | null) => void,
): void {
  const next = descriptionValue(draft);
  if (next === current) {
    close(current);
    return;
  }
  submit(next, close);
}
