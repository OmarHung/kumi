import { createContext, use, type ReactNode } from 'react';

/** 打開一個項目（例如在清單旁邊開項目面板）。 */
export type OpenItem = (itemKey: string) => void;

const OpenItemContext = createContext<OpenItem | null>(null);

/**
 * 新增之後的「開啟」怎麼開。沒有提供時（預設）照連結換到整頁的項目頁；項目面板（U1）接上時，在清單外面提供
 * 開面板的函式，沒按修飾鍵的左鍵就交給它（components/SidePanel/plainClick.ts）。窄螢幕不開面板：那時提供 null。
 */
export function OpenItemProvider({ value, children }: { readonly value: OpenItem | null; readonly children: ReactNode }) {
  return <OpenItemContext value={value}>{children}</OpenItemContext>;
}

export function useOpenItem(): OpenItem | null {
  return use(OpenItemContext);
}
