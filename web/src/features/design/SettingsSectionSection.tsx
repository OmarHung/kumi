import { Button } from '../../components/Button/Button';
import { TextField } from '../../components/Field/Field';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { Demo, DemoSection } from './DemoSection';
import styles from './Design.module.css';

/**
 * 設定頁的區塊（components/SettingsSection）：左欄標題與說明、右欄整塊內容；內容欄 46rem 以上左右、以下上下。
 * 設定中心的內容欄是容器（settings-hub），這裡自己包一個，拉寬或縮窄視窗就看得到兩種排法。
 */
export function SettingsSectionSection() {
  return (
    <DemoSection title="設定頁的區塊">
      <Demo name="SettingsSection">
        <div className={styles.settingsFrame}>
          <SettingsSection title="寄件人" description="信上顯示的寄件人名稱。" isContentStart>
            <form
              className={styles.settingsForm}
              onSubmit={(event) => {
                event.preventDefault();
              }}
            >
              <TextField label="寄件人名稱" defaultValue="範例組織" />
              <div>
                <Button variant="primary" type="submit">
                  儲存
                </Button>
              </div>
            </form>
          </SettingsSection>
          <SettingsSection title="登出其他裝置" description="把這個帳號在別的裝置上的登入全部結束。">
            <div>
              <Button>登出其他裝置</Button>
            </div>
          </SettingsSection>
        </div>
      </Demo>
    </DemoSection>
  );
}
