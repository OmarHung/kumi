import { createContext } from 'react';

export interface EditorEnvironment {
  /** false：附件不載入（專案說明）。 */
  readonly hasAttachments: boolean;
}

/** 編輯器裡的節點畫面（圖片、檔案的小卡）要知道的事。節點畫面在 EditorContent 的子樹裡，Context 傳得到。 */
export const EditorEnvironmentContext = createContext<EditorEnvironment>({ hasAttachments: true });
