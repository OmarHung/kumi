import { useMemo } from 'react';
import { useProjectTable } from '../item-table/ProjectTableContext';

/**
 * 人員編號對應的名稱：這個專案目前的成員（提及畫成「@名稱」時用；不在裡面的用連結本來的文字）。
 * 專案的內容每次重抓都是新的陣列：用內容（編號與名稱）當依賴，內容沒變就回傳同一個 Map，Markdown 才不必跟著重新渲染。
 */
export function useMentionNames(): ReadonlyMap<string, string> {
  const { detail } = useProjectTable();
  const signature = JSON.stringify(detail.members.map((member) => [member.personId, member.displayName]));
  return useMemo(() => new Map((JSON.parse(signature) as [string, string][]).map(([id, name]) => [id, name])), [signature]);
}
