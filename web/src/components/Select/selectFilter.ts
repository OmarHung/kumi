import { useEffect, useRef, useState } from 'react';
import type { SelectOption } from './Select';

/*
 * 選項多的時候清單最上面的篩選框（docs/DESIGN.md 7.3「選項很多的時候」）：10 個以上自動出現，不用逐處開關。
 * 比對選項的文字與 keywords（人員的 Email），用「包含」不是「開頭」：中文名字沒辦法靠打字跳到開頭，
 * 要打名字裡的任何一段都找得到。英文不分大小寫。
 */

/** 選項到這麼多就有篩選框。 */
export const FILTER_THRESHOLD = 10;
/** 打字停下來這麼久才唸結果數（和快速跳轉送出搜尋同一個 300ms）：每打一個字就唸，會和字元的回音搶著唸。 */
export const FILTER_SETTLE_MS = 300;

export function isFilterable(options: readonly SelectOption[]): boolean {
  return options.length >= FILTER_THRESHOLD;
}

/** 比對前先正規化：NFKC 讓全形與半形一樣（「ＡＧＹ－１２」就是「AGY-12」），英文不分大小寫。 */
function normalized(text: string): string {
  return text.normalize('NFKC').trim().toLocaleLowerCase();
}

export function matchesQuery(option: SelectOption, query: string): boolean {
  const needle = normalized(query);
  if (needle === '') return true;
  return [option.label, ...(option.keywords ?? [])].some((text) => normalized(text).includes(needle));
}

/** 篩選後的一個選項：index 是它在完整清單裡的位置（選項的 id 照它編，篩選時同一個選項 id 不變）。 */
export interface FilteredOption {
  readonly option: SelectOption;
  readonly index: number;
}

/** 篩選後的選項與它們在完整清單裡的位置，照原本的順序；空的篩選是全部。 */
export function filterEntries(options: readonly SelectOption[], query: string): readonly FilteredOption[] {
  const isAll = normalized(query) === '';
  return options.flatMap((option, index) => (isAll || matchesQuery(option, query) ? [{ option, index }] : []));
}

/** 篩選後的選項，照原本的順序；空的篩選是全部。 */
export function filterOptions(options: readonly SelectOption[], query: string): readonly SelectOption[] {
  return filterEntries(options, query).map((entry) => entry.option);
}

/** 結果數的說法（role="status"）：沒在篩選時不說話。 */
export function filterStatus(options: readonly SelectOption[], query: string): string {
  if (normalized(query) === '') return '';
  const count = filterOptions(options, query).length;
  return count === 0 ? '沒有符合的選項' : `${String(count)} 個符合`;
}

export interface FilterQuery {
  /** 篩選框裡現在的字（輸入法選字中也跟著變）。 */
  readonly text: string;
  /** 拿來篩選的字：輸入法選字中不換（注音還沒選好字，不篩）。 */
  readonly query: string;
  /** 篩選框的字變了；回傳新的篩選字，選字中沒有換時回 null。 */
  readonly change: (text: string) => string | null;
  readonly startComposition: () => void;
  /** 選好字了：回傳新的篩選字。 */
  readonly endComposition: (text: string) => string;
  /** 打開時從頭開始（或從關著時打的那個字開始）。 */
  readonly reset: (seed?: string) => void;
}

/** 篩選框的字與篩選字。選字中的狀態用 ref 記著：change 與 compositionend 在同一輪裡前後發生。 */
export function useFilterQuery(): FilterQuery {
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  const isComposing = useRef(false);
  return {
    text,
    query,
    change: (next) => {
      setText(next);
      if (isComposing.current) return null;
      setQuery(next);
      return next;
    },
    startComposition: () => {
      isComposing.current = true;
    },
    endComposition: (next) => {
      isComposing.current = false;
      setText(next);
      setQuery(next);
      return next;
    },
    reset: (seed = '') => {
      isComposing.current = false;
      setText(seed);
      setQuery(seed);
    },
  };
}

/**
 * 停下來 FILTER_SETTLE_MS 之後的篩選字：結果數照它說，打字中不換。一開始是空的（不是拿到的篩選字）：
 * 關著時打字打開的那一次，篩選字一開始就有，區域掛上時就有內容的話報讀軟體不唸。
 */
export function useSettledQuery(query: string): string {
  const [settled, setSettled] = useState('');
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSettled(query);
    }, FILTER_SETTLE_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [query]);
  return settled;
}
