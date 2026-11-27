import { SubNav, SubNavSelect, type SubNavGroup } from '../../components/SubNav/SubNav';
import {
  SETTINGS_HUB_GROUP_LABELS,
  type SettingsHubGroup,
  type SettingsHubPage,
} from './settingsHubPages';

const NAV_LABEL = '設定';
const GROUP_ORDER: readonly SettingsHubGroup[] = ['personal', 'organization'];

interface SettingsHubNavProps {
  readonly pages: readonly SettingsHubPage[];
  readonly current: SettingsHubPage | undefined;
}

function navGroups(pages: readonly SettingsHubPage[]): readonly SubNavGroup[] {
  return GROUP_ORDER.map((group) => ({
    key: group,
    label: SETTINGS_HUB_GROUP_LABELS[group],
    items: pages.filter((page) => page.group === group),
  }));
}

/**
 * 寬螢幕（64rem 以上）的次導覽：分「個人」「組織」兩組，只列這個人看得到的頁；只有一組時照樣寫組名。
 * 長相在共用的 SubNav（使用說明也用它）。
 */
export function SettingsHubNav({ pages, current }: SettingsHubNavProps) {
  return <SubNav label={NAV_LABEL} groups={navGroups(pages)} currentKey={current?.key} />;
}

/** 64rem 以下換成工具列上的一個下拉。 */
export function SettingsHubSelect({ pages, current }: SettingsHubNavProps) {
  return (
    <SubNavSelect
      label={NAV_LABEL}
      selectLabel="設定的頁面"
      groups={navGroups(pages)}
      currentKey={current?.key}
      // 打了看不到的頁（成員打 /email-settings）：選項裡沒有它，按鈕上照樣寫頁名，不是一顆空白的下拉。
      placeholder={current?.label}
    />
  );
}
