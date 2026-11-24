import type { RefObject } from 'react';
import type { Item } from '../../api/types';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { moveTargets } from '../../domain/workflow';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { useBoard } from './BoardContext';
import { blockedReason } from './moveLabels';
// 按鈕的樣子在卡片的 CSS：滑鼠滑過卡片或焦點在卡片裡才出現，要和卡片的選擇器寫在一起。
import cardStyles from './BoardCard.module.css';

interface MoveMenuProps {
  readonly item: Item;
  /** 送出中：按鈕還在（焦點不會掉），但打不開。 */
  readonly isBusy: boolean;
  readonly buttonRef: RefObject<HTMLButtonElement | null>;
}

/**
 * 卡片上的「移到…」：鍵盤與螢幕閱讀器不必拖曳也能改狀態。列出這個流程的其他狀態，
 * 走得到的在前；走不到的（流程沒有這條路、角色不對）停用並寫出原因。和拖曳走同一條路（useBoardMoves）。
 * 選單的鍵盤與焦點行為在共用的 Menu；buttonRef 給卡片移到別的欄之後把焦點放回這顆按鈕。
 * 按鈕是圖示按鈕（名稱與滑過的說明都是「AGY-3 移到…」），平常只在滑鼠滑過卡片、焦點在卡片裡時出現，觸控時一直顯示。
 */
export function MoveMenu({ item, isBusy, buttonRef }: MoveMenuProps) {
  const { detail } = useProjectTable();
  const { workflow, move } = useBoard();
  const targets = workflow === null ? [] : moveTargets(workflow, item.state, detail.project.myRole);
  if (targets.length === 0) {
    return null;
  }
  const firstBlocked = targets.findIndex((target) => target.availability.kind !== 'open');
  const items: MenuItem[] = targets.map((target, index) => ({
    key: target.state.key,
    label: target.state.name,
    disabledReason: blockedReason(target.availability, detail.blueprint) ?? undefined,
    startsGroup: index === firstBlocked,
    onSelect: () => {
      move(item, target.state.key, true);
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
