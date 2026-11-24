import { createContext, use, useCallback, useEffect, useState } from 'react';
import type { ProjectBlueprint } from '../../api/blueprint';
import type { Member } from '../../api/types';
import type { SettingsDraft } from '../../domain/settingsDraft';
import { issuesAt, targetId, type SettingsIssue, type SettingsTarget } from '../../domain/settingsIssues';

export interface SettingsEditorValue {
  /** 開始編輯時的藍圖：草稿和它比，才知道哪些是還沒送出的修改。 */
  readonly base: ProjectBlueprint;
  readonly draft: SettingsDraft;
  /** 有 blueprint.propose 才能改；沒有時整頁唯讀。 */
  readonly canEdit: boolean;
  /** 目前要顯示的問題：送出前的檢查（按過「預覽變更」之後）與伺服器上次擋下的原因。 */
  readonly issues: readonly SettingsIssue[];
  readonly update: (change: (draft: SettingsDraft) => SettingsDraft) => void;
  /** 這一頁元素 id 的前綴（useId），同一頁裡的每個狀態、欄位用 key 組出不重複的 id。 */
  readonly idPrefix: string;
  /** 專案的成員：刪除角色時說明還有幾位成員是這個角色。 */
  readonly members: readonly Member[];
  /** 對話框打開時登記、回傳的函式在關上時取消（useSettingsDialog 用）。 */
  readonly trackDialog: () => () => void;
}

const SettingsEditorContext = createContext<SettingsEditorValue | null>(null);

export const SettingsEditorProvider = SettingsEditorContext.Provider;

export function useSettingsEditor(): SettingsEditorValue {
  const value = use(SettingsEditorContext);
  if (value === null) {
    throw new Error('useSettingsEditor 只能用在設定頁裡。');
  }
  return value;
}

/**
 * 設定頁現在有沒有開著的對話框（新增狀態、欄位、類型、角色，轉換的限制）。對話框裡打到一半的內容還沒進草稿：
 * 這時連「換到別的區塊」（例如瀏覽器的上一頁）也要先問，否則對話框跟著區塊卸下、內容不見。
 */
export function useOpenDialogs() {
  const [count, setCount] = useState(0);
  const trackDialog = useCallback(() => {
    setCount((current) => current + 1);
    return () => {
      setCount((current) => current - 1);
    };
  }, []);
  return { hasOpenDialog: count > 0, trackDialog };
}

/** 設定頁的對話框掛上時登記、卸下時取消（對話框是條件渲染：掛著就是開著）。 */
export function useSettingsDialog(): void {
  const { trackDialog } = useSettingsEditor();
  useEffect(() => trackDialog(), [trackDialog]);
}

/** 某個位置的元素 id，例如 state-task_flow-todo-name。 */
export function elementId(idPrefix: string, target: SettingsTarget, part: string): string {
  return `${idPrefix}-${targetId(target)}-${part}`;
}

export interface IssueBinding {
  readonly messages: readonly string[];
  readonly errorId: string;
  /** 給輸入框的 aria-describedby；沒有問題時是 undefined。 */
  readonly describedBy: string | undefined;
  readonly isInvalid: boolean;
}

/**
 * 這個位置的問題，以及把它和輸入框連起來的 id。同一個位置在好幾列都顯示時（共用的搬移），
 * 每一列給自己的 errorId，id 才不會重複。
 */
export function issueBinding(
  editor: SettingsEditorValue,
  target: SettingsTarget,
  errorId: string = elementId(editor.idPrefix, target, 'error'),
): IssueBinding {
  const messages = issuesAt(editor.issues, target);
  const isInvalid = messages.length > 0;
  return { messages, errorId, describedBy: isInvalid ? errorId : undefined, isInvalid };
}
