import { useCallback, useEffect, useRef, useSyncExternalStore } from 'react';
import { isFilteredView, switchViewParams, type ProjectView } from '../../domain/projectView';
import { projectPagePath } from '../work-items/itemLinks';

/**
 * 每個專案上次用的版面：表格、看板、行事曆或時間軸（docs/DESIGN.md 8.1 第 5 點「系統記得每個專案上次用的檢視」）。
 * 個人的顯示偏好，記在這個瀏覽器；側欄與麵包屑的專案連結照它開，網址上有 view 時以網址為準。
 */
export const PROJECT_VIEW_STORAGE_PREFIX = 'project.view.';

/** 同一個分頁裡記下新的檢視時通知（側欄的連結跟著換）；別的分頁改的靠 storage 事件。 */
const listeners = new Set<() => void>();

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成表格（預設的檢視）。 */
export function readProjectView(projectKey: string): ProjectView {
  try {
    const stored = localStorage.getItem(PROJECT_VIEW_STORAGE_PREFIX + projectKey);
    return isFilteredView(stored) ? stored : 'table';
  } catch {
    return 'table';
  }
}

/** 表格是預設：換回表格就拿掉記錄。寫不進去時這次照樣用，下次回到表格，不打斷使用者。 */
export function saveProjectView(projectKey: string, view: ProjectView): void {
  try {
    if (view === 'table') {
      localStorage.removeItem(PROJECT_VIEW_STORAGE_PREFIX + projectKey);
    } else {
      localStorage.setItem(PROJECT_VIEW_STORAGE_PREFIX + projectKey, view);
    }
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
  }
  listeners.forEach((listener) => {
    listener();
  });
}

/** 連到專案頁、照上次的版面開（只帶 view，條件不記）。 */
export function projectPageLink(projectKey: string, view: ProjectView = readProjectView(projectKey)): string {
  const query = switchViewParams(new URLSearchParams(), view).toString();
  return query === '' ? projectPagePath(projectKey) : `${projectPagePath(projectKey)}?${query}`;
}

function subscribe(onChange: () => void): () => void {
  listeners.add(onChange);
  window.addEventListener('storage', onChange);
  return () => {
    listeners.delete(onChange);
    window.removeEventListener('storage', onChange);
  };
}

/** 專案連結（側欄、麵包屑）：記下的檢視換了就跟著換。 */
export function useProjectPageLink(projectKey: string): string {
  const read = useCallback(() => projectPageLink(projectKey), [projectKey]);
  return useSyncExternalStore(subscribe, read, () => projectPagePath(projectKey));
}

/**
 * 專案頁記下使用者選的版面：網址明寫了 view（?view=board、?view=calendar、?view=table），或在這一頁切換了檢視才算。
 * 不帶 view 的連結（別人貼來的網址、舊的書籤）開出來的表格不算，不會把記住的看板洗掉。
 */
export function useRememberProjectView(projectKey: string, view: ProjectView, isExplicit: boolean): void {
  const shown = useRef<ProjectView | null>(null);
  useEffect(() => {
    const isSwitched = shown.current !== null && shown.current !== view;
    shown.current = view;
    if (isSwitched || isExplicit) saveProjectView(projectKey, view);
  }, [projectKey, view, isExplicit]);
}
