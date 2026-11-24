import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { SegmentedControl, type SegmentedOption } from '../../components/SegmentedControl/SegmentedControl';
import { setToastMode, useToastMode, type ToastMode } from '../notification-toasts/toastMode';
import { ToastModePreview } from './ToastModePreview';
import styles from './AccountPage.module.css';

const OPTIONS: readonly SegmentedOption<ToastMode>[] = [
  { value: 'full', label: '完整' },
  { value: 'compact', label: '精簡' },
  { value: 'off', label: '不顯示' },
];

/**
 * 有新通知時在畫面角落跳出的提示要怎麼顯示：完整（含留言與提及的內容）、精簡（不顯示內容，開會投影、共用螢幕時用）、
 * 不顯示（只有側欄的數字）。按了就生效，記在這台電腦的瀏覽器：要隱藏內容的通常是某一台電腦，不是整個帳號。
 * 選項下面是跟著選的方式換的示意圖（2026-11-24，使用者要求：光看文字想像不出提示長什麼樣子）。
 */
export function NotificationToastsSection() {
  const mode = useToastMode();
  return (
    // 左欄說這是什麼、影響範圍；三個選項的差別寫在選項下面（右欄），兩邊不重複同一件事。
    <SettingsSection title="通知提示" description="有新通知時在畫面的角落跳出提示。只影響這台電腦的瀏覽器。">
      <div>
        <SegmentedControl label="提示的顯示方式" options={OPTIONS} value={mode} onChange={setToastMode} />
      </div>
      <ToastModePreview mode={mode} />
      <p className={styles.note}>完整會顯示留言與提及的內容；精簡只顯示誰做了什麼（開會投影、共用螢幕時用）；不顯示就只有側欄的數字。</p>
    </SettingsSection>
  );
}
