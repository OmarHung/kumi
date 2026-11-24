import type { MouseEvent } from 'react';
import { Link } from 'react-router';
import { isPlainLeftClick } from '../../components/SidePanel/plainClick';
import { cx } from '../../lib/cx';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import { useOpenItem } from './OpenItemContext';
import { useProjectTable } from './ProjectTableContext';
import styles from './QuickAddItem.module.css';

export const OUTSIDE_CONDITIONS_TEXT = '它不符合目前的條件，清除條件之後可以看到。';

/** 剛新增的項目：編號，以及它符不符合清單目前的條件（不符合時重抓之後就不在這份清單裡了）。 */
export interface CreatedItem {
  readonly key: string;
  readonly isOutsideConditions: boolean;
}

function OpenLink({ itemKey }: { readonly itemKey: string }) {
  const { detail } = useProjectTable();
  const entry = useItemPageEntry();
  const openItem = useOpenItem();
  function handleClick(event: MouseEvent<HTMLAnchorElement>) {
    // 有提供開面板的方式（U1）時，一般的點擊交給它；按著 ⌘／Ctrl、Shift 或中鍵照連結走。
    if (openItem === null || !isPlainLeftClick(event)) return;
    event.preventDefault();
    openItem(itemKey);
  }
  return (
    // 名稱以看得到的「開啟」開頭（說得出看到的字就按得到），後面帶上編號。
    <Link to={itemPagePath(detail.project.key, itemKey)} state={entry} aria-label={`開啟 ${itemKey}`} className={styles.openLink} onClick={handleClick}>
      開啟
    </Link>
  );
}

interface CreatedStatusProps {
  readonly created?: CreatedItem | null;
  /** 只知道編號時（下層項目的快速新增：新的一定在那份清單裡）。 */
  readonly createdKey?: string | null;
  readonly className?: string;
}

/**
 * 「已新增 AGY-25。」加「開啟」（docs/DESIGN.md 8.3「新增」）。訊息是一直掛著的 status：沒有訊息時是空的，
 * 之後出現的訊息報讀軟體才唸得到。「開啟」放在 status 外面，報讀軟體不會把「開啟」也唸成訊息；
 * 名稱帶上編號（「開啟 AGY-25」），連續新增時分得出是哪一個。
 */
export function CreatedStatus({ created, createdKey, className }: CreatedStatusProps) {
  const key = created?.key ?? createdKey ?? null;
  const isOutside = created?.isOutsideConditions === true;
  // 「它不符合目前的條件…」是提醒，不是成功：同一個 status 裡（一起唸），但用一般的灰字，不用成功的綠色。
  return (
    <div className={cx(styles.created, className)} data-created={key !== null}>
      <p role="status">
        {key !== null && `已新增 ${key}。`}
        {key !== null && isOutside && <span className={styles.outside}>{OUTSIDE_CONDITIONS_TEXT}</span>}
      </p>
      {key !== null && <OpenLink itemKey={key} />}
    </div>
  );
}
