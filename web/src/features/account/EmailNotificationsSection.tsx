import { useState } from 'react';
import { useEmailNotificationPreference, useSetEmailNotificationPreference } from '../../api/emailSettings';
import { errorMessage } from '../../api/http';
import { useCurrentUser } from '../../auth/CurrentUser';
import { CheckboxField } from '../../components/CheckboxField/CheckboxField';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { ErrorState, LoadingState } from '../../components/StatusViews/StatusViews';

/**
 * 有新通知時要不要寄 Email 到自己的信箱：按了就存（不另外按儲存），失敗時退回原來的並說明原因。
 * 組織還沒開寄信時勾選框照樣能改（開了之後才會寄），下面說明。
 */
export function EmailNotificationsSection() {
  const user = useCurrentUser();
  const preference = useEmailNotificationPreference();
  const update = useSetEmailNotificationPreference();
  const [failure, setFailure] = useState<string | null>(null);

  function toggle(enabled: boolean) {
    // 送出中再按不送第二次（兩個請求的回應可能亂序，後到的舊值會蓋掉新的）。勾選框只標 aria-disabled：焦點留得住。
    if (update.isPending) return;
    setFailure(null);
    update.mutate(enabled, {
      onError: (error) => {
        setFailure(errorMessage(error));
      },
    });
  }

  return (
    <SettingsSection title="Email 通知" description="通知 5 分鐘後還沒讀，才會寄到你的信箱；同一時段的合併成一封。" isContentStart>
      {preference.isPending && <LoadingState label="正在載入 Email 通知的設定…" />}
      {preference.isError && (
        <ErrorState
          error={preference.error}
          title="Email 通知的設定載入失敗"
          onRetry={() => {
            void preference.refetch();
          }}
        />
      )}
      {preference.isSuccess && (
        <>
          <CheckboxField
            label={`有新通知時寄 Email 到 ${user.email}`}
            checked={preference.data.enabled}
            aria-disabled={update.isPending || undefined}
            hint={preference.data.organizationEnabled ? undefined : '組織還沒開啟寄信，開啟之後才會寄。'}
            onChange={(event) => {
              toggle(event.target.checked);
            }}
          />
          <FormAlert message={failure} />
        </>
      )}
    </SettingsSection>
  );
}
