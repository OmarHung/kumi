import { useId, useRef, type PointerEvent, type ReactNode, type RefObject } from 'react';
import type { FloatingAlign } from '../../lib/useAnchoredPosition';
import { Floating } from '../Floating/Floating';
import { Icon, type IconName } from '../Icon/Icon';
import { useMenu, type MenuTriggerProps } from './useMenu';
import styles from './Menu.module.css';

export type { MenuTriggerProps } from './useMenu';

export interface MenuItem {
  readonly key: string;
  readonly label: string;
  readonly icon?: IconName;
  /** 不可回復的動作用 danger，放在最後（docs/DESIGN.md 第 7 節）。 */
  readonly tone?: 'default' | 'danger';
  /** 有值：aria-disabled、停得到、原因寫在選項裡並用 aria-describedby 連過去。 */
  readonly disabledReason?: string;
  /** 從這一項開始是新的一組：前面畫一條分隔線（第一項不畫）。 */
  readonly startsGroup?: boolean;
  /**
   * 這一項是一組的第一項、組有名字（浮動面板的對話依專案分組）：前面畫一行組的名字（不是選項、方向鍵跳過）。
   * 後面的組前面多一條細線；不要和 startsGroup 一起給。報讀軟體在這一組每一項的說明裡唸出組名（選單裡的 group 不好用方向鍵走）。
   */
  readonly groupLabel?: string;
  /**
   * 有值（true 或 false）：這是「選一個」的項目（目前的主題、目前的檢視），渲染成 menuitemradio 並帶 aria-checked，
   * 報讀軟體才聽得到哪一項是目前的；勾選的那一項自己畫打勾圖示（不必給 icon）。同一個選單裡要選一個的項目都要給。
   */
  readonly isChecked?: boolean;
  readonly onSelect: () => void;
}

export interface MenuProps {
  /** role="menu" 的名稱，例如「把 AGY-3 移到」。 */
  readonly menuLabel: string;
  /** 畫出觸發的按鈕：把 MenuTriggerProps 展開到 <button>（或 Button、IconButton）上。 */
  readonly renderTrigger: (props: MenuTriggerProps) => ReactNode;
  readonly items: readonly MenuItem[];
  readonly align?: FloatingAlign;
  /** 呼叫端也要用到按鈕時給（例如移動之後把焦點放回去）；沒給就用自己的。 */
  readonly triggerRef?: RefObject<HTMLButtonElement | null>;
  /** 送出中這類暫時不能打開的時候：按鈕標 aria-disabled，按了沒反應。 */
  readonly isDisabled?: boolean;
}

interface MenuEntryProps {
  readonly item: MenuItem;
  readonly id: string;
  readonly hasSeparator: boolean;
  /** 這一項屬於哪一組的名字的元素編號（沒有分組是 undefined）。 */
  readonly groupLabelId: string | undefined;
  readonly onSelect: () => void;
}

/**
 * 滑過的項目取得焦點：反白（滑過或焦點所在）與 Enter 作用的永遠是同一個。用滑鼠打開時焦點在第一項，
 * 不這樣做的話滑過第三項、按 Enter 執行的卻是第一項。不能用的項目也一樣（方向鍵停得到，滑鼠也停得到）。
 */
function focusOnPoint(event: PointerEvent<HTMLButtonElement>) {
  if (document.activeElement !== event.currentTarget) event.currentTarget.focus({ preventScroll: true });
}

function MenuEntry({ item, id, hasSeparator, groupLabelId, onSelect }: MenuEntryProps) {
  const reason = item.disabledReason;
  const isBlocked = reason !== undefined;
  const icon = item.icon ?? (item.isChecked === true ? 'check' : undefined);
  const describedBy = [groupLabelId, isBlocked ? `${id}-reason` : undefined].filter((part) => part !== undefined).join(' ');
  return (
    <>
      {hasSeparator && <li role="separator" className={styles.separator} />}
      {item.groupLabel !== undefined && (
        <li role="presentation" id={groupLabelId} className={styles.groupLabel}>
          {item.groupLabel}
        </li>
      )}
      <li role="none">
        <button
          type="button"
          role={item.isChecked === undefined ? 'menuitem' : 'menuitemradio'}
          aria-checked={item.isChecked}
          tabIndex={-1}
          className={styles.item}
          data-tone={item.tone ?? 'default'}
          aria-disabled={isBlocked || undefined}
          aria-labelledby={`${id}-name`}
          aria-describedby={describedBy === '' ? undefined : describedBy}
          onPointerMove={focusOnPoint}
          onClick={() => {
            if (!isBlocked) onSelect();
          }}
        >
          {icon !== undefined && <Icon name={icon} className={styles.icon} />}
          <span className={styles.text}>
            <span id={`${id}-name`}>{item.label}</span>
            {isBlocked && (
              <span id={`${id}-reason`} className={styles.reason}>
                {reason}
              </span>
            )}
          </span>
        </button>
      </li>
    </>
  );
}

/** 第 index 項所在的那一組的名字的元素編號：往前找最近一個帶組名的項目；前面都沒有是 undefined。 */
function groupLabelIdOf(items: readonly MenuItem[], index: number, menuId: string): string | undefined {
  for (let at = index; at >= 0; at -= 1) {
    if (items[at]?.groupLabel !== undefined) return `${menuId}-group-${String(at)}`;
  }
  return undefined;
}

/**
 * 一串動作的選單（ARIA 的 menu button，從看板的「移到…」抽出來）。選單渲染在按鈕旁邊的 DOM 裡、
 * 畫面上浮在 top layer（components/Floating），不會被捲動容器裁掉。選了之後先關上、焦點回到按鈕，再執行動作：
 * 動作會開對話框時，對話框關上後焦點才回得到這顆按鈕。
 */
export function Menu({ menuLabel, renderTrigger, items, align = 'start', triggerRef, isDisabled = false }: MenuProps) {
  const ownTriggerRef = useRef<HTMLButtonElement>(null);
  const anchorRef = triggerRef ?? ownTriggerRef;
  const menuId = useId();
  const { isOpen, rootRef, listRef, triggerProps, handleListKeyDown, handleBlur, closeAndFocus, closeWhenAnchorHidden } =
    useMenu(anchorRef, isDisabled, menuId);
  return (
    <div ref={rootRef} className={styles.root} onBlur={handleBlur}>
      {renderTrigger(triggerProps)}
      {isOpen && (
        <Floating anchorRef={anchorRef} align={align} onAnchorHidden={closeWhenAnchorHidden}>
          <ul
            ref={listRef}
            id={menuId}
            role="menu"
            aria-label={menuLabel}
            className={styles.menu}
            data-has-icons={items.some((item) => item.icon !== undefined || item.isChecked !== undefined) || undefined}
            onKeyDown={handleListKeyDown}
          >
            {items.map((item, index) => (
              <MenuEntry
                key={item.key}
                item={item}
                id={`${menuId}-${String(index)}`}
                hasSeparator={item.startsGroup === true && index > 0}
                groupLabelId={groupLabelIdOf(items, index, menuId)}
                onSelect={() => {
                  closeAndFocus();
                  item.onSelect();
                }}
              />
            ))}
          </ul>
        </Floating>
      )}
    </div>
  );
}
