import { useMutation } from '@tanstack/react-query';
import { useId } from 'react';
import { signOutOtherSessions } from '../../api/account';
import { errorMessage } from '../../api/http';
import { Button } from '../../components/Button/Button';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import styles from './AccountPage.module.css';

function resultMessage(signedOut: number): string {
  return signedOut === 0 ? '沒有其他登入中的裝置。' : `已登出其他 ${String(signedOut)} 個裝置。`;
}

/**
 * 登出其他裝置：同一個帳號的其他登入全部作廢，這一個留著。可以回復（再登入一次就好），所以不用確認。
 * 伺服器不記裝置，沒有「登入中的裝置」清單，只說登出了幾個。
 */
export function OtherSessionsSection() {
  const descriptionId = useId();
  const mutation = useMutation({ mutationFn: signOutOtherSessions });
  const result = mutation.isSuccess ? resultMessage(mutation.data.signedOutSessions) : null;
  return (
    <SettingsSection title="其他裝置" description="在別的電腦或瀏覽器登入過、忘了登出時用。" descriptionId={descriptionId}>
      <div className={styles.actions}>
        {/* 送出中停用但停得到（aria-disabled）：用 disabled 的話，按下的按鈕一停用焦點就掉回頁面本體。 */}
        <Button
          aria-describedby={descriptionId}
          aria-disabled={mutation.isPending || undefined}
          onClick={() => {
            if (!mutation.isPending) mutation.mutate();
          }}
        >
          {mutation.isPending ? '登出中…' : '登出其他裝置'}
        </Button>
      </div>
      <Notice tone={mutation.data?.signedOutSessions === 0 ? 'info' : 'success'} role="status">
        {result}
      </Notice>
      <FormAlert message={mutation.isError ? errorMessage(mutation.error) : null} />
    </SettingsSection>
  );
}
