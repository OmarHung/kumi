import { IconButton } from '../../components/IconButton/IconButton';
import type { IconName } from '../../components/Icon/Icon';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { useThemeChoice, type ThemeChoice } from '../../lib/theme';
import { ControlsSection } from './ControlsSection';
import { EditorSection } from './EditorSection';
import { FeedbackSection, LabelsSection } from './FeedbackSection';
import { NavigationSection } from './NavigationSection';
import { IconsSection, OverlaySection } from './OverlaySection';
import { SettingsSectionSection } from './SettingsSectionSection';
import styles from './Design.module.css';

const THEMES: readonly { readonly choice: ThemeChoice; readonly label: string; readonly icon: IconName }[] = [
  { choice: 'system', label: '跟著系統', icon: 'themeSystem' },
  { choice: 'light', label: '淺色', icon: 'themeLight' },
  { choice: 'dark', label: '深色', icon: 'themeDark' },
];

function ThemePicker() {
  const [theme, setTheme] = useThemeChoice();
  return (
    <div role="group" aria-label="主題" className={styles.themes}>
      {THEMES.map(({ choice, label, icon }) => (
        <IconButton
          key={choice}
          icon={icon}
          label={label}
          size="sm"
          variant={theme === choice ? 'secondary' : 'ghost'}
          aria-pressed={theme === choice}
          onClick={() => {
            setTheme(choice);
          }}
        />
      ))}
    </div>
  );
}

/**
 * 只在開發模式有的展示頁（app/devRoutes.tsx）：每個共用元件的每種狀態各一個，給審查的人淺色與深色各看一遍。
 * 這一頁本身就是 PageLayout 的 reading 版面。
 */
export function DesignPage() {
  return (
    <div className={styles.page}>
      <PageLayout variant="reading" bar={<PageBar title="設計元件" crumbs={[{ label: '開發用' }]} actions={<ThemePicker />} />}>
        <div className={styles.sections}>
          <p className={styles.lead}>
            docs/DESIGN.md 第 7 節的共用元件，每種狀態各一個。正式建置沒有這一頁；淺色與深色都要看過，觸控（pointer: coarse）時控制項會變高。
          </p>
          <ControlsSection />
          <FeedbackSection />
          <LabelsSection />
          <OverlaySection />
          <EditorSection />
          <NavigationSection />
          <SettingsSectionSection />
          <IconsSection />
        </div>
      </PageLayout>
    </div>
  );
}
