import type { ProposalStatus } from './aiTypes';

/** 通用動作建議（知識庫、人員、附件、存的檢視、說明格式）的一步：內容由伺服器在建立時產生，網頁照著畫。 */
export interface ActionStep {
  readonly label: string;
  /** 動作的對象（頁面標題、人員名稱…）。 */
  readonly target: string | null;
  readonly before: string | null;
  readonly after: string | null;
  readonly note: string | null;
  /** markdown 的 before／after 以說明差異的方式顯示。 */
  readonly format: 'text' | 'markdown';
  readonly done: boolean;
  readonly error: string | null;
  /** 套用之後可以點過去的網站內路徑。 */
  readonly link: string | null;
}

/** 卡片上要使用者填的輸入（新增人員的初始密碼）：只在按套用時送出，伺服器不存。 */
export interface ActionInput {
  /** 例如 `password:0`（第 0 步的初始密碼）。 */
  readonly key: string;
  readonly label: string;
  readonly kind: 'password' | 'text';
  readonly hint: string | null;
}

export type ActionKind = 'knowledge' | 'people' | 'attachments' | 'saved_views' | 'description_templates';

export interface ActionProposal {
  readonly id: string;
  readonly kind: ActionKind;
  /** 組織對話的建議沒有專案。 */
  readonly projectKey: string | null;
  readonly title: string;
  readonly reason: string | null;
  readonly steps: readonly ActionStep[];
  readonly inputs: readonly ActionInput[];
  readonly status: ProposalStatus;
  readonly createdAt: string;
  readonly decidedAt: string | null;
  readonly error: string | null;
}
