import type { ProjectDetail } from '../../api/types';
import type { MenuItem } from '../../components/Menu/Menu';

export interface ProjectMenuHandlers {
  readonly goTo: (path: string) => void;
  readonly onRename: () => void;
  readonly onArchive: () => void;
  readonly onUnarchive: () => void;
}

/** 組的第一項前面畫分隔線（選單的第一項不畫，所以放在開頭的組不會多一條線）。 */
function startGroup(items: readonly MenuItem[]): readonly MenuItem[] {
  const [first, ...rest] = items;
  return first === undefined ? [] : [{ ...first, startsGroup: true }, ...rest];
}

/**
 * 專案頁的「更多」（寬螢幕）與「專案的動作」（窄螢幕）後半的項目，依權限與封存狀態（docs/design/08-interaction.md 8.3「專案頁的更多動作」）：
 * 「匯入項目…」「已刪除的項目」一組，「重新命名…」「封存專案」或「取消封存」一組。沒有任何一項時是空陣列，呼叫端不畫選單。
 * 封存的專案權限只剩讀取與 project.manage，所以匯入與已刪除的入口自然消失。
 */
export function projectMoreItems(
  detail: ProjectDetail,
  paths: { readonly import: string; readonly deleted: string },
  handlers: ProjectMenuHandlers,
): readonly MenuItem[] {
  const has = (permission: ProjectDetail['myPermissions'][number]) => detail.myPermissions.includes(permission);
  const isArchived = detail.project.archivedAt !== null;
  const work: readonly MenuItem[] = [
    ...(has('item.create') ? [{ key: 'import', label: '匯入項目…', onSelect: () => { handlers.goTo(paths.import); } } satisfies MenuItem] : []),
    ...(has('item.delete')
      ? [{ key: 'deleted', label: '已刪除的項目', icon: 'restore', onSelect: () => { handlers.goTo(paths.deleted); } } satisfies MenuItem]
      : []),
  ];
  const manage: readonly MenuItem[] = has('project.manage')
    ? [
        { key: 'rename', label: '重新命名…', icon: 'edit', onSelect: handlers.onRename },
        isArchived
          ? { key: 'unarchive', label: '取消封存', onSelect: handlers.onUnarchive }
          : { key: 'archive', label: '封存專案', onSelect: handlers.onArchive },
      ]
    : [];
  return [...startGroup(work), ...startGroup(manage)];
}
