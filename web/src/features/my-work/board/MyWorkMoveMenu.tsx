import { useEffect, useRef, type RefObject } from 'react';
import type { Item } from '../../../api/types';
import { IconButton } from '../../../components/IconButton/IconButton';
import { Menu, type MenuItem, type MenuTriggerProps } from '../../../components/Menu/Menu';
import { workflowForType } from '../../../domain/blueprint';
import { blockedReason } from '../../board/moveLabels';
import { groupedMoveTargets } from './boardColumns';
import { useMyWorkBoard, type StatePicker } from './MyWorkBoardContext';
// 按鈕的樣子在卡片的 CSS：滑鼠滑過卡片或焦點在卡片裡才出現，要和卡片的選擇器寫在一起。
import cardStyles from '../../board/BoardCard.module.css';

interface MoveMenuProps {
  readonly item: Item;
  /** 送出中：按鈕還在（焦點不會掉），但打不開。 */
  readonly isBusy: boolean;
  readonly buttonRef: RefObject<HTMLButtonElement | null>;
}

/**
 * 卡片上的「移到…」：鍵盤與螢幕閱讀器不必拖曳也能改狀態。列出那個項目自己專案的流程裡其他的狀態，
 * 走得到的在前、依狀態類別分組；走不到的（流程沒有這條路、角色不對）停用並寫出原因。和拖曳走同一條路。
 */
export function MyWorkMoveMenu({ item, isBusy, buttonRef }: MoveMenuProps) {
  const { projects, move } = useMyWorkBoard();
  const detail = projects.get(item.projectKey);
  const workflow = detail === undefined ? undefined : workflowForType(detail.blueprint, item.type);
  if (detail === undefined || workflow === undefined) {
    return null;
  }
  const items: MenuItem[] = groupedMoveTargets(workflow, item, detail.project.myRole).map(({ target, startsGroup }) => ({
    key: target.state.key,
    label: target.state.name,
    disabledReason: blockedReason(target.availability, detail.blueprint) ?? undefined,
    startsGroup,
    onSelect: () => {
      move(item, target.state.key, true);
    },
  }));
  if (items.length === 0) {
    return null;
  }
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

/** 掛上時打開選單一次（StrictMode 的 effect 會跑兩次：用 ref 擋掉第二次，不然開了又關）。 */
function OpenOnMount({ trigger }: { readonly trigger: MenuTriggerProps }) {
  const hasOpenedRef = useRef(false);
  useEffect(() => {
    if (hasOpenedRef.current) return;
    hasOpenedRef.current = true;
    trigger.onClick();
  }, [trigger]);
  return null;
}

interface PickerProps {
  readonly picker: StatePicker;
  readonly buttonRef: RefObject<HTMLButtonElement | null>;
}

/**
 * 拖到某個類別、那個類別有好幾個走得到的狀態：在這張卡片旁邊開選單「移到哪一個狀態？」。
 * 用卡片上「移到…」的按鈕當錨點（選單浮在它旁邊，關上之後焦點回到它）：Esc 取消、卡片留在原欄。
 * 列出那個類別全部的狀態，走不到的停用並寫出原因。
 */
export function StatePickerMenu({ picker, buttonRef }: PickerProps) {
  const { projects, move } = useMyWorkBoard();
  const detail = projects.get(picker.item.projectKey);
  if (detail === undefined) {
    return null;
  }
  const items: MenuItem[] = picker.targets.map((target) => ({
    key: target.state.key,
    label: target.state.name,
    disabledReason: blockedReason(target.availability, detail.blueprint) ?? undefined,
    onSelect: () => {
      move(picker.item, target.state.key, true);
    },
  }));
  return (
    <Menu
      key={picker.id}
      menuLabel="移到哪一個狀態？"
      items={items}
      align="end"
      triggerRef={buttonRef}
      renderTrigger={(trigger) => <OpenOnMount trigger={trigger} />}
    />
  );
}
