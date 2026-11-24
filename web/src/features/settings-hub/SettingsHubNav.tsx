import { useId } from 'react';
import { Link, useNavigate } from 'react-router';
import { Select, type SelectOption } from '../../components/Select/Select';
import {
  SETTINGS_HUB_GROUP_LABELS,
  type SettingsHubGroup,
  type SettingsHubPage,
} from './settingsHubPages';
import styles from './SettingsHub.module.css';

const NAV_LABEL = '設定';
const GROUP_ORDER: readonly SettingsHubGroup[] = ['personal', 'organization'];

interface SettingsHubNavProps {
  readonly pages: readonly SettingsHubPage[];
  readonly current: SettingsHubPage | undefined;
}

function NavGroup({ group, pages, current }: { readonly group: SettingsHubGroup; readonly pages: readonly SettingsHubPage[]; readonly current: SettingsHubPage | undefined }) {
  const labelId = useId();
  return (
    <div className={styles.navGroup}>
      <span id={labelId} className={styles.navGroupLabel}>
        {SETTINGS_HUB_GROUP_LABELS[group]}
      </span>
      <ul className={styles.navList} aria-labelledby={labelId}>
        {pages.map((page) => (
          <li key={page.key}>
            <Link to={page.path} className={styles.navLink} aria-current={page.key === current?.key ? 'page' : undefined}>
              {page.label}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * 寬螢幕（64rem 以上）的次導覽：內容區左邊一欄，分「個人」「組織」兩組，目前所在的那一項標 aria-current
 * （選取的底、強調色的字、semibold，和側欄同一套，不畫色條）。只列這個人看得到的頁；只有一組時照樣寫組名。
 */
export function SettingsHubNav({ pages, current }: SettingsHubNavProps) {
  return (
    <nav aria-label={NAV_LABEL} className={styles.nav}>
      {GROUP_ORDER.map((group) => {
        const inGroup = pages.filter((page) => page.group === group);
        return inGroup.length === 0 ? null : <NavGroup key={group} group={group} pages={inGroup} current={current} />;
      })}
    </nav>
  );
}

function selectOptions(pages: readonly SettingsHubPage[]): readonly SelectOption[] {
  return GROUP_ORDER.flatMap((group) =>
    pages
      .filter((page) => page.group === group)
      .map((page, index) => ({
        value: page.key,
        label: page.label,
        groupLabel: index === 0 ? SETTINGS_HUB_GROUP_LABELS[group] : undefined,
      })),
  );
}

/**
 * 64rem 以下換成工具列上的一個下拉（和專案頁窄螢幕的「版面」同一種做法）：側欄加次導覽放不下，
 * 內容會被擠到剩三百多 px。選了就換頁；標籤只給報讀軟體，意思由選項（目前的頁名）帶出來。
 */
export function SettingsHubSelect({ pages, current }: SettingsHubNavProps) {
  const navigate = useNavigate();
  return (
    <nav aria-label={NAV_LABEL} className={styles.compactNav}>
      <Select
        aria-label="設定的頁面"
        size="sm"
        width="content"
        value={current?.key ?? ''}
        // 打了看不到的頁（成員打 /email-settings）：選項裡沒有它，按鈕上照樣寫頁名，不是一顆空白的下拉。
        placeholder={current?.label}
        options={selectOptions(pages)}
        onChange={(key) => {
          const page = pages.find((candidate) => candidate.key === key);
          if (page !== undefined) void navigate(page.path);
        }}
      />
    </nav>
  );
}
