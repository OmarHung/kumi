import { useState } from 'react';
import type { ItemDetail } from '../../api/types';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { ChangeTypeDialog } from './ChangeTypeDialog';
import { DeleteItemDialog } from './DeleteItemDialog';
import { useKnownChildren } from './knownChildren';
import { useDeleteItem } from './useDeleteItem';

type ItemDialog = 'changeType' | 'delete';

interface Abilities {
  /** 有 item.update、而且專案有兩種以上的類型。 */
  readonly canChangeType: boolean;
  /** 有 item.delete。 */
  readonly canDelete: boolean;
}

/** 選單的項目：危險的刪除放最後、自成一組（docs/DESIGN.md 第 7 節的 Menu）。 */
function actionItems({ canChangeType, canDelete }: Abilities, open: (dialog: ItemDialog) => void): MenuItem[] {
  const changeType: MenuItem = { key: 'changeType', label: '改類型', icon: 'edit', onSelect: () => { open('changeType'); } };
  const remove: MenuItem = {
    key: 'delete',
    label: '刪除',
    icon: 'delete',
    tone: 'danger',
    startsGroup: true,
    onSelect: () => { open('delete'); },
  };
  return [...(canChangeType ? [changeType] : []), ...(canDelete ? [remove] : [])];
}

/**
 * 頁列右邊的「更多動作」（docs/DESIGN.md 6.1）：改類型與刪除，一件都不能做時沒有這顆按鈕。
 * 選了之後選單先關上、焦點回到按鈕，才開對話框，所以對話框關上後焦點回到這顆按鈕。
 * 對話框放在選單外面：選單關上時卸下的是浮層，對話框不受影響。
 * 「刪除」不開確認框（可以復原）：項目頁已經知道有下層就開說明的對話框，否則直接送出；
 * 成功後離開、提示上有「復原」（useDeleteItem），伺服器擋下（還有下層、權限）時才開對話框說明。
 */
export function ItemActions({ item }: { readonly item: ItemDetail }) {
  const { detail, can } = useProjectTable();
  const [openDialog, setOpenDialog] = useState<ItemDialog | null>(null);
  const remove = useDeleteItem(item);
  const { children } = useKnownChildren(item, detail.blueprint);
  const hasKnownChildren = children.length > 0;
  const abilities = { canChangeType: can.update && detail.blueprint.itemTypes.length > 1, canDelete: can.delete };
  const items = actionItems(abilities, (dialog) => {
    if (dialog !== 'delete') setOpenDialog(dialog);
    else if (hasKnownChildren) setOpenDialog('delete');
    else remove.mutate();
  });
  if (items.length === 0) {
    return null;
  }
  const label = `${item.key} 的更多動作`;
  const close = () => {
    setOpenDialog(null);
    remove.reset();
  };
  return (
    <>
      <Menu
        menuLabel={label}
        align="end"
        items={items}
        renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={label} />}
      />
      {openDialog === 'changeType' && <ChangeTypeDialog item={item} onClose={close} />}
      {(openDialog === 'delete' || remove.isError) && (
        <DeleteItemDialog item={item} onClose={close} hasKnownChildren={hasKnownChildren} error={remove.error} />
      )}
    </>
  );
}
