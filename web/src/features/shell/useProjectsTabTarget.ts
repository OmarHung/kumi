import { useState } from 'react';

const PROJECT_LIST = '/projects';

/**
 * 底部分頁列「專案」要去哪裡（docs/DESIGN.md 6.1「底部分頁列」），照手機 App 分頁的慣例：
 * - 已經在專案底下：回到專案列表（原本是連回這一頁，按了沒反應，使用者 2026-11-28 問「為什麼回不去專案列表」）。
 * - 在別的分頁：回到上次看的專案頁（看過的那一頁還在原處），沒看過時是專案列表。
 * 記住的位置只在這個分頁的記憶體裡：重新整理之後從專案列表開始，不必寫進瀏覽器。
 */
export function useProjectsTabTarget(isProjects: boolean, pathname: string, search: string): string {
  const [lastProjectPage, setLastProjectPage] = useState<string | null>(null);
  const here = `${pathname}${search}`;
  const isProjectPage = isProjects && pathname !== PROJECT_LIST;
  // 換頁時跟著記下（渲染中依新的網址調整 state，React 會在這一輪就重新渲染，不必等 effect）。
  if (isProjectPage && lastProjectPage !== here) setLastProjectPage(here);
  if (isProjects) return PROJECT_LIST;
  return lastProjectPage ?? PROJECT_LIST;
}
