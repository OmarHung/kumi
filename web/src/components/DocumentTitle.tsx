import { createContext, use } from 'react';
import { PRODUCT_NAME } from '../config/product';

/** 分頁標題前面的數字超過這個就寫「99+」：分頁很窄，要留給頁名。 */
const MAX_TITLE_COUNT = 99;

/**
 * 分頁標題前面的未讀數，由登入後的外框提供；登入頁這類外框以外的頁面沒有（undefined）。
 * 不直接改 document.title：<title> 歸 React 管，直接改會在下一次畫的時候被蓋掉。
 */
export const DocumentTitleCountContext = createContext<number | undefined>(undefined);

/** 「(3) 我的工作 · Kumi」：沒有未讀（0 或還不知道）時不加括號。 */
export function formatDocumentTitle(title: string, count: number | undefined): string {
  const base = `${title} · ${PRODUCT_NAME}`;
  if (count === undefined || count <= 0) return base;
  const shown = count > MAX_TITLE_COUNT ? `${String(MAX_TITLE_COUNT)}+` : String(count);
  return `(${shown}) ${base}`;
}

/** React 19 會把 <title> 提到 <head>；分頁多開時看得出是哪一頁，看別的分頁時也看得到有沒有未讀。 */
export function DocumentTitle({ title }: { readonly title: string }) {
  const count = use(DocumentTitleCountContext);
  return <title>{formatDocumentTitle(title, count)}</title>;
}
