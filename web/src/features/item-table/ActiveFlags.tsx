import { IconButton } from '../../components/IconButton/IconButton';
import type { ItemFilters } from '../../domain/itemFilters';
import { STALE_DAYS } from '../../domain/overviewRules';
import { updateThenFocus } from '../../lib/updateThenFocus';
import type { ItemTableState } from './useItemTableState';
import styles from './ActiveFlags.module.css';

interface Flag {
  readonly key: 'open' | 'stale';
  readonly label: string;
}

/**
 * 停滯已經只算還沒結束的，所以它開著時「只看還沒結束的」不另外列（兩個標記講同一件事，拿掉一個另一個還在會讓人困惑）；
 * 拿掉停滯時 open 一起拿掉。
 *
 * 沒有下拉可選、只有「開」或「關」的條件（專案總覽的連結帶進來的）。它們不在篩選列的欄位裡，
 * 但篩掉了項目，不能看不見：有的時候列在表格上方，每個一個可以拿掉的標記。
 */
function activeFlags(filters: ItemFilters): readonly Flag[] {
  if (filters.stale) return [{ key: 'stale', label: `${String(STALE_DAYS)} 天沒有更新（只算還沒結束的）` }];
  return filters.open ? [{ key: 'open', label: '只看還沒結束的' }] : [];
}

/** 表格上方一列：目前開著的「只看還沒結束的」或「14 天沒有更新（只算還沒結束的）」。都沒開時不畫（不佔高度）。 */
export function ActiveFlags({ table }: { readonly table: ItemTableState }) {
  const { filters, setFilters, searchRef, filterButtonId } = table;
  const flags = activeFlags(filters);
  if (flags.length === 0) return null;
  return (
    <ul className={styles.flags} aria-label="其他條件">
      {flags.map((flag) => (
        <li key={flag.key} className={styles.flag}>
          <span>{flag.label}</span>
          <IconButton
            icon="close"
            size="sm"
            label={`拿掉「${flag.label}」`}
            onClick={() => {
              // 按的按鈕跟著標記不見：焦點接手到搜尋框（窄螢幕是「篩選」），不掉回頁面本體。
              updateThenFocus(() => {
                setFilters(flag.key === 'stale' ? { ...filters, stale: false, open: false } : { ...filters, open: false });
              }, [searchRef, filterButtonId]);
            }}
          />
        </li>
      ))}
    </ul>
  );
}
