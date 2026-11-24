import type { SavedView, SavedViewLayout } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { Menu } from '../../components/Menu/Menu';
import type { useViewActionsFor } from './useViewActions';
import { ViewDialogs } from './ViewDialogs';
import { viewMenuItems } from './viewMenuItems';

interface ViewMenuShellProps {
  readonly views: readonly SavedView[];
  readonly currentLayout: SavedViewLayout;
  /** 目前的版面與條件是不是這個檢視。 */
  readonly matches: (view: SavedView) => boolean;
  readonly hasConditions: boolean;
  /** 另存時名稱的預設。 */
  readonly suggestedName: string;
  readonly actions: ReturnType<typeof useViewActionsFor>;
}

/**
 * 「檢視」按鈕、選單與對話框，專案與「我的工作」共用：兩邊只差檢視從哪來、怎麼比對、怎麼套用，那些由呼叫端算好傳進來。
 * 按鈕看得到的字永遠是「檢視」：寬度不隨狀態變，工具列上它後面的控制項不會被推著動；目前是哪一個檢視，
 * 在選單裡打勾（menuitemradio），報讀軟體在按鈕上也聽得到。
 */
export function ViewMenuShell({ views, currentLayout, matches, hasConditions, suggestedName, actions }: ViewMenuShellProps) {
  const matched = views.find(matches);
  const stale = matched === undefined ? views.find((view) => view.id === actions.appliedId) : undefined;
  const items = viewMenuItems({
    views,
    matched,
    stale,
    currentLayout,
    hasConditions,
    onApply: actions.apply,
    onUpdate: actions.updateToCurrent,
    onSave: () => { actions.setDialog({ kind: 'save' }); },
    onRename: (view) => { actions.setDialog({ kind: 'rename', view }); },
    onDelete: (view) => { actions.setDialog({ kind: 'delete', view }); },
  });
  return (
    <>
      <Menu
        menuLabel="檢視"
        renderTrigger={(trigger) => (
          <Button {...trigger} size="sm">
            <span>檢視</span>
            {matched !== undefined && <span className="visually-hidden">{`，目前是「${matched.name}」`}</span>}
            <Icon name="chevronDown" />
          </Button>
        )}
        items={items}
      />
      <ViewDialogs
        dialog={actions.dialog}
        mutations={actions.mutations}
        suggestedName={suggestedName}
        onSave={actions.save}
        onRename={actions.rename}
        onDelete={actions.remove}
        onRetryUpdate={actions.updateToCurrent}
        onClose={actions.close}
      />
    </>
  );
}
