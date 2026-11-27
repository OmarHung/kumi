import { useId } from 'react';
import { Link, useNavigate } from 'react-router';
import { Select, type SelectOption } from '../Select/Select';
import styles from './SubNav.module.css';

export interface SubNavItem {
  readonly key: string;
  readonly label: string;
  readonly path: string;
}

export interface SubNavGroup {
  readonly key: string;
  /** 組名：小灰字，不是標題（頁面的 h1 之後是內容的 h2，組名不插進標題的層級）。 */
  readonly label: string;
  readonly items: readonly SubNavItem[];
}

interface SubNavProps {
  /** `<nav>` 的名稱：設定中心是「設定」，使用說明是「使用說明」。 */
  readonly label: string;
  /** 只列這個人看得到的；空的組不畫。 */
  readonly groups: readonly SubNavGroup[];
  readonly currentKey: string | undefined;
}

function NavGroup({ group, currentKey }: { readonly group: SubNavGroup; readonly currentKey: string | undefined }) {
  const labelId = useId();
  return (
    <div className={styles.navGroup}>
      <span id={labelId} className={styles.navGroupLabel}>
        {group.label}
      </span>
      <ul className={styles.navList} aria-labelledby={labelId}>
        {group.items.map((item) => (
          <li key={item.key}>
            <Link to={item.path} className={styles.navLink} aria-current={item.key === currentKey ? 'page' : undefined}>
              {item.label}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * 內容區左邊一欄的次導覽（64rem 以上；docs/DESIGN.md 6.2「設定中心」）：分組列出這個區塊的頁，目前所在的那一項標 aria-current
 * （選取的底、強調色的字、semibold，和側欄同一套，不畫色條）。設定中心與使用說明共用，同一件事只有一種長相（第 1 節第 7 條）。
 */
export function SubNav({ label, groups, currentKey }: SubNavProps) {
  return (
    <nav aria-label={label} className={styles.nav}>
      {groups
        .filter((group) => group.items.length > 0)
        .map((group) => (
          <NavGroup key={group.key} group={group} currentKey={currentKey} />
        ))}
    </nav>
  );
}

interface SubNavSelectProps extends SubNavProps {
  /** 下拉的名稱（只給報讀軟體，意思由選項帶出來），例如「設定的頁面」。 */
  readonly selectLabel: string;
  /** 目前的頁不在選項裡時（打了看不到的頁的網址）按鈕上照樣寫的名稱。 */
  readonly placeholder?: string;
}

function selectOptions(groups: readonly SubNavGroup[]): readonly SelectOption[] {
  return groups.flatMap((group) =>
    group.items.map((item, index) => ({ value: item.key, label: item.label, groupLabel: index === 0 ? group.label : undefined })),
  );
}

/**
 * 64rem 以下換成工具列上的一個下拉（和專案頁窄螢幕的「版面」同一種做法）：側欄加次導覽放不下，
 * 內容會被擠到剩三百多 px。選了就換頁。
 */
export function SubNavSelect({ label, groups, currentKey, selectLabel, placeholder }: SubNavSelectProps) {
  const navigate = useNavigate();
  const items = groups.flatMap((group) => group.items);
  return (
    <nav aria-label={label} className={styles.compactNav}>
      <Select
        aria-label={selectLabel}
        size="sm"
        width="content"
        value={currentKey ?? ''}
        placeholder={placeholder}
        options={selectOptions(groups)}
        onChange={(key) => {
          const item = items.find((candidate) => candidate.key === key);
          if (item !== undefined) void navigate(item.path);
        }}
      />
    </nav>
  );
}
