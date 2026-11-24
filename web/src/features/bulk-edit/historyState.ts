/**
 * 復原成功之後，把這一筆瀏覽紀錄裡的提示與 undo 拿掉：重新整理（F5）後 history.state 還在，
 * 提示與「復原」會又出現；之後又刪了同一筆的話，舊的「復原」還會把它救回來。
 * 「已刪除 …」的提示文字也一起拿掉（事情已經被復原，再說一次已刪除是錯的）。
 *
 * 不用 navigate(replace)：那會換一個 location.key，換頁提示（lib/useArrivalNotice.ts 以 key 記「說過了」）會再說一次、焦點再拉走。
 * 直接改 history.state 的 usr（React Router 放 location.state 的地方），key 不變；這一頁記憶體裡的 state 不動，畫面照舊。
 * history.state 不是 React Router 寫的（key 對不上、沒有 usr，例如測試的記憶體路由）就不動它。
 */
export function removeUndoFromHistory(locationKey: string): void {
  const entry: unknown = window.history.state;
  if (typeof entry !== 'object' || entry === null || !('key' in entry) || entry.key !== locationKey || !('usr' in entry)) return;
  const usr: unknown = entry.usr;
  if (typeof usr !== 'object' || usr === null) return;
  const rest = Object.fromEntries(Object.entries(usr).filter(([name]) => name !== 'notice' && name !== 'undo'));
  window.history.replaceState({ ...entry, usr: rest }, '');
}
