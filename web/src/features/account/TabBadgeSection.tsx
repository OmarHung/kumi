import { SegmentedControl, type SegmentedOption } from '../../components/SegmentedControl/SegmentedControl';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { badgedFaviconUrl, badgeFill, type TabBadgePosition, type TabBadgeSize } from '../inbox/faviconBadge';
import { updateTabBadge, useTabBadge } from '../inbox/tabBadgePreference';
import { BadgeColorPicker } from './BadgeColorPicker';
import { TabBadgePreview } from './TabBadgePreview';
import accountStyles from './AccountPage.module.css';
import styles from './TabBadgeSection.module.css';

const POSITIONS: readonly SegmentedOption<TabBadgePosition>[] = [
  { value: 'top-right', label: '右上' },
  { value: 'bottom-right', label: '右下' },
  { value: 'top-left', label: '左上' },
  { value: 'bottom-left', label: '左下' },
];

const SIZES: readonly SegmentedOption<TabBadgeSize>[] = [
  { value: 'small', label: '小' },
  { value: 'medium', label: '中' },
  { value: 'large', label: '大' },
];

/**
 * 有未讀通知時，分頁圖示上的圓點要畫在哪裡、多大、什麼顏色（2026-11-24 使用者要求能自己調，之後又要能自訂色碼）。
 * 按了就生效（有未讀時這個分頁的圖示馬上跟著換），記在這台電腦的瀏覽器，和「通知提示」同一套。
 * 選項下面是跟著換的示意圖（和同一頁的「通知提示」同一個順序）：分頁上的圖示只有 16px，光看選項想像不出來，示意圖另外放一個放大的。
 */
export function TabBadgeSection() {
  const badge = useTabBadge();
  return (
    <SettingsSection
      title="分頁的未讀提示"
      description="有未讀通知時，分頁的標題前面是未讀數、圖示上有一個圓點，看別的分頁時也知道。只影響這台電腦的瀏覽器。"
    >
      {/* 每一列左邊是看得到的名稱，和群組的名稱（aria-label）同一個字。 */}
      <div className={styles.controls}>
        <span className={styles.label} aria-hidden="true">位置</span>
        <div>
          <SegmentedControl label="位置" options={POSITIONS} value={badge.position} onChange={(position) => { updateTabBadge({ position }); }} />
        </div>
        <span className={styles.label} aria-hidden="true">大小</span>
        <div>
          <SegmentedControl label="大小" options={SIZES} value={badge.size} onChange={(size) => { updateTabBadge({ size }); }} />
        </div>
        <span className={styles.label} aria-hidden="true">顏色</span>
        <div>
          <BadgeColorPicker
            value={badge.color}
            customColor={badge.customColor}
            onChange={(color) => { updateTabBadge({ color }); }}
            onSelectCustom={() => { updateTabBadge({ color: 'custom', customColor: badge.customColor ?? badgeFill(badge) }); }}
            onCustomColorChange={(customColor) => { updateTabBadge({ color: 'custom', customColor }); }}
            customPreview={<img src={badgedFaviconUrl(badge)} alt="" className={styles.pickerPreview} />}
          />
        </div>
      </div>
      <TabBadgePreview badge={badge} />
      <p className={accountStyles.note}>沒有未讀時分頁照原本的樣子。換了馬上套用：現在有未讀的話，這個分頁的圖示會跟著變。</p>
    </SettingsSection>
  );
}
