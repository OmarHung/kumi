import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';

export type EmailProvider = 'gmail' | 'smtp';
export type SmtpSecurity = 'start_tls' | 'tls' | 'none';

/** 寄送的狀況：兩個時間比大小決定畫哪一個（lastFailedAt 比 lastSentAt 新才是失敗）。 */
export interface EmailDeliveryStatus {
  readonly lastSentAt: string | null;
  readonly lastFailedAt: string | null;
  readonly lastError: string | null;
}

/** 組織的寄信設定。密碼不會讀出來，只有 hasPassword；沒存過時 updatedAt 與 updatedBy 是 null。 */
export interface EmailSettings {
  readonly enabled: boolean;
  readonly provider: EmailProvider;
  readonly host: string | null;
  readonly port: number | null;
  readonly security: SmtpSecurity | null;
  readonly username: string | null;
  readonly hasPassword: boolean;
  readonly fromAddress: string | null;
  readonly fromName: string | null;
  readonly siteUrl: string | null;
  readonly status: EmailDeliveryStatus;
  readonly updatedAt: string | null;
  readonly updatedBy: { readonly personId: string; readonly displayName: string } | null;
}

/** 要存的設定。password：null 是沿用已經存的，空字串是清掉。 */
export interface EmailSettingsInput {
  readonly enabled: boolean;
  readonly provider: EmailProvider;
  readonly host: string | null;
  readonly port: number | null;
  readonly security: SmtpSecurity | null;
  readonly username: string | null;
  readonly password: string | null;
  readonly fromAddress: string | null;
  readonly fromName: string | null;
  readonly siteUrl: string | null;
}

export interface EmailNotificationPreference {
  readonly enabled: boolean;
  /** 組織有沒有開寄信：沒開時個人的開關照樣能改，只是還不會寄。 */
  readonly organizationEnabled: boolean;
}

/** 只有組織的擁有者與管理員，也不能用權杖。403 email.forbidden。 */
export function getEmailSettings(signal?: AbortSignal): Promise<EmailSettings> {
  return apiRequest<EmailSettings>('/v1/email-settings', { signal });
}

/** 403 email.forbidden；422 email.settings_invalid（細項路徑是欄位名稱，camelCase）。 */
export function saveEmailSettings(input: EmailSettingsInput): Promise<EmailSettings> {
  return apiRequest<EmailSettings>('/v1/email-settings', { method: 'PUT', body: input });
}

/**
 * 用還沒存的設定寄一封測試信到登入的人自己的 Email。
 * 403 email.forbidden；422 email.settings_invalid、email.send_failed（訊息是原因）；429 每人每分鐘最多 5 次。
 */
export function sendTestEmail(input: EmailSettingsInput): Promise<{ readonly sentTo: string }> {
  return apiRequest<{ readonly sentTo: string }>('/v1/email-settings/test', { method: 'POST', body: input });
}

export function getEmailNotificationPreference(signal?: AbortSignal): Promise<EmailNotificationPreference> {
  return apiRequest<EmailNotificationPreference>('/v1/account/email-notifications', { signal });
}

export function setEmailNotificationPreference(enabled: boolean): Promise<EmailNotificationPreference> {
  return apiRequest<EmailNotificationPreference>('/v1/account/email-notifications', { method: 'PUT', body: { enabled } });
}

export function useEmailSettings() {
  return useQuery({
    queryKey: queryKeys.emailSettings,
    queryFn: ({ signal }) => getEmailSettings(signal),
    // 沒權限（403）重試也一樣；打開頁面時一定要拿最新的，不用快取的舊設定當表單的起點。
    retry: false,
    staleTime: 0,
    gcTime: 0,
  });
}

export function useSaveEmailSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: saveEmailSettings,
    onSuccess: (saved) => {
      queryClient.setQueryData(queryKeys.emailSettings, saved);
      // 組織有沒有開寄信影響「我的帳號」的說明。
      void queryClient.invalidateQueries({ queryKey: queryKeys.emailPreference });
    },
  });
}

export function useSendTestEmail() {
  return useMutation({ mutationFn: sendTestEmail });
}

export function useEmailNotificationPreference() {
  return useQuery({
    queryKey: queryKeys.emailPreference,
    queryFn: ({ signal }) => getEmailNotificationPreference(signal),
  });
}

/** 按了就存：先把畫面改成新的值，失敗退回原來的（呼叫端用 onError 說明原因）。 */
export function useSetEmailNotificationPreference() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: setEmailNotificationPreference,
    onMutate: async (enabled) => {
      await queryClient.cancelQueries({ queryKey: queryKeys.emailPreference });
      const previous = queryClient.getQueryData<EmailNotificationPreference>(queryKeys.emailPreference);
      if (previous !== undefined) queryClient.setQueryData(queryKeys.emailPreference, { ...previous, enabled });
      return { previous };
    },
    onError: (_error, _enabled, context) => {
      if (context?.previous !== undefined) queryClient.setQueryData(queryKeys.emailPreference, context.previous);
    },
    onSuccess: (saved) => {
      queryClient.setQueryData(queryKeys.emailPreference, saved);
    },
    // 成功或失敗都以伺服器上的為準：失敗時已經退回，再讀一次確認（別的分頁可能也改過）。
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.emailPreference }),
  });
}
