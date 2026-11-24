import type { RefObject } from 'react';
import type { Item } from '../../api/types';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { columnKeyOf, dropBlockedReason, emptyColumnName, NO_VALUE_KEY, optionTargets } from '../../domain/boardGroupColumns';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { useBoard } from './BoardContext';
// 按鈕的樣子在卡片的 CSS：和依狀態分欄的「移到…」同一顆。
import cardStyles from './BoardCard.module.css';

interface GroupMoveMenuProps {
  readonly item: Item;
  /** 送出中：按鈕還在（焦點不會掉），但打不開。 */
  readonly isBusy: boolean;
  readonly buttonRef: RefObject<HTMLButtonElement | null>;
}

/**
 * 依負責人、欄位分欄時卡片上的「移到…」：列出所有可以移去的人或選項（不只是看板上有欄的：沒有欄的選項靠這裡選），
 * 最後是「沒有負責人」「沒有填」；目前所在的那一個不列。類型沒有這個欄位的卡片，選項停用並寫出原因。
 * 和拖曳走同一條路（useGroupMoves）；選單的鍵盤與焦點行為在共用的 Menu。
 */
export function GroupMoveMenu({ item, isBusy, buttonRef }: GroupMoveMenuProps) {
  const { detail } = useProjectTable();
  const { group, move } = useBoard();
  const current = columnKeyOf(item, group);
  const targets = optionTargets(group, detail.blueprint, detail.members).filter((target) => target.key !== current);
  if (targets.length === 0) {
    return null;
  }
  const items: MenuItem[] = targets.map((target) => ({
    key: target.key,
    label: target.key === NO_VALUE_KEY ? emptyColumnName(group) : target.name,
    disabledReason: dropBlockedReason(item, group, target.key, detail.blueprint) ?? undefined,
    onSelect: () => {
      move(item, target.key, true);
    },
  }));
  return (
    <Menu
      menuLabel={`把 ${item.key} 移到`}
      items={items}
      align="end"
      triggerRef={buttonRef}
      isDisabled={isBusy}
      renderTrigger={(trigger) => (
        <IconButton {...trigger} icon="move" label={`${item.key} 移到…`} size="sm" className={cardStyles.moveButton} />
      )}
    />
  );
}
