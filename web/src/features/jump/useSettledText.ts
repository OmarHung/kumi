import { useEffect, useState } from 'react';

/** 和表格的搜尋框（item-table/FilterFields.tsx 的 SearchField）一樣：停下來 300ms 才送。 */
const SEARCH_DELAY_MS = 300;

/** 打字停下來 SEARCH_DELAY_MS 之後才換成新的字；清空時馬上清空（內部的值也是）。 */
export function useSettledText(text: string): string {
  const [settled, setSettled] = useState(text);
  // 清空時內部的值也清掉：不然打 abc、清空、再打 a，停下來之前會回到 abc 而對它發請求。
  if (text === '' && settled !== '') {
    setSettled('');
  }
  useEffect(() => {
    if (text === settled) return undefined;
    const timer = window.setTimeout(() => {
      setSettled(text);
    }, SEARCH_DELAY_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [text, settled]);
  return text === '' ? '' : settled;
}
