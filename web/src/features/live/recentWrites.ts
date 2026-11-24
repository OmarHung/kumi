import type { QueryClient } from '@tanstack/react-query';

/** 這個分頁寫入過的項目編號記多久（毫秒）：即時更新推回來的「自己的修改」在這段時間內略過。 */
export const RECENT_WRITE_MS = 10_000;

const writes = new WeakMap<QueryClient, Map<string, number>>();

/** 這個分頁剛寫入成功這些項目（寫入的 onSuccess 已經照設計更新了快取）。 */
export function noteOwnWrites(queryClient: QueryClient, itemKeys: readonly string[]): void {
  const now = Date.now();
  const recent = writes.get(queryClient) ?? new Map<string, number>();
  recent.forEach((at, key) => {
    if (now - at > RECENT_WRITE_MS) recent.delete(key);
  });
  itemKeys.forEach((key) => {
    recent.set(key, now);
  });
  writes.set(queryClient, recent);
}

/** 這些項目是不是每一個都是這個分頁最近寫過的。 */
export function wereAllWrittenHere(queryClient: QueryClient, itemKeys: readonly string[]): boolean {
  const recent = writes.get(queryClient);
  const now = Date.now();
  return itemKeys.every((key) => {
    const at = recent?.get(key);
    return at !== undefined && now - at <= RECENT_WRITE_MS;
  });
}
