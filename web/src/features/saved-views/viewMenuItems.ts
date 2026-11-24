import type { MenuItem } from '../../components/Menu/Menu';
import type { SavedView, SavedViewLayout } from '../../api/types';
import { LAYOUT_NAMES } from '../../domain/savedViews';

interface ViewMenuItemsInput {
  readonly views: readonly SavedView[];
  /** 目前的條件剛好就是這個檢視。 */
  readonly matched: SavedView | undefined;
  /** 套用過、之後又改了條件的那個檢視（沒有 matched 時才有）。 */
  readonly stale: SavedView | undefined;
  /** 目前畫面的版面：和要更新的檢視不同時，「更新」註明現在的版面（更新後版面也會換）。 */
  readonly currentLayout: SavedViewLayout;
  /** 目前有沒有任何條件（篩選、搜尋、非預設的排序）。沒有時不給「更新」：不要用一次不相干的清除把檢視清成空的。 */
  readonly hasConditions: boolean;
  readonly onApply: (view: SavedView) => void;
  readonly onUpdate: (view: SavedView) => void;
  readonly onSave: () => void;
  readonly onRename: (view: SavedView) => void;
  readonly onDelete: (view: SavedView) => void;
}

function layoutNote(saved: SavedViewLayout, current: SavedViewLayout): string {
  return saved === current ? '' : `（${LAYOUT_NAMES[current]}）`;
}

/**
 * 選單的項目：先是自己的檢視；再來是改動它們的動作（更新、另存、重新命名）；危險的「刪除」放最後（規範第 13 節）。
 * 重新命名與刪除只在目前的條件剛好是某個檢視時才有：選單上的動作針對的是「現在看著的這一個」，不用另外選。
 */
export function viewMenuItems(input: ViewMenuItemsInput): MenuItem[] {
  const { views, matched, stale } = input;
  const applyItems = views.map((view): MenuItem => ({
    key: `apply-${view.id}`,
    label: view.name,
    isChecked: view.id === matched?.id,
    onSelect: () => {
      input.onApply(view);
    },
  }));
  const update: MenuItem[] =
    stale === undefined || !input.hasConditions
      ? []
      : [
          {
            key: 'update',
            label: `更新「${stale.name}」成目前的條件${layoutNote(stale.layout, input.currentLayout)}`,
            startsGroup: true,
            onSelect: () => {
              input.onUpdate(stale);
            },
          },
        ];
  const save: MenuItem = { key: 'save', label: '另存成檢視…', icon: 'add', startsGroup: update.length === 0, onSelect: input.onSave };
  const manage: MenuItem[] =
    matched === undefined
      ? []
      : [
          { key: 'rename', label: '重新命名…', icon: 'edit', onSelect: () => { input.onRename(matched); } },
          { key: 'delete', label: `刪除「${matched.name}」`, icon: 'delete', tone: 'danger', startsGroup: true, onSelect: () => { input.onDelete(matched); } },
        ];
  return [...applyItems, ...update, save, ...manage];
}
