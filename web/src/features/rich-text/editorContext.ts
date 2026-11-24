import { createContext } from 'react';
import { attachmentContentUrl } from '../../api/attachments';

export interface EditorEnvironment {
  /** false：附件不載入（專案說明）。 */
  readonly hasAttachments: boolean;
  /** 附件編號對應的下載網址（知識庫的頁面換成自己的）。 */
  readonly attachmentUrl: (attachmentId: string) => string;
}

/** 編輯器裡的節點畫面（圖片、檔案的小卡）要知道的事。節點畫面在 EditorContent 的子樹裡，Context 傳得到。 */
export const EditorEnvironmentContext = createContext<EditorEnvironment>({ hasAttachments: true, attachmentUrl: attachmentContentUrl });
