import { useQueryClient } from '@tanstack/react-query';
import { createContext, use, useEffect } from 'react';
import { queryKeys } from '../../api/queryKeys';
import type { SessionUser } from '../../api/types';
import { startLiveUpdates } from './startLiveUpdates';
import type { LiveConnectionFactory, LiveNotificationHooks } from './liveTypes';

/** 沒有提供（undefined）就不連線：測試與沒有即時更新的環境。正式環境由 main.tsx 提供真的連線。 */
export const LiveConnectionFactoryContext = createContext<LiveConnectionFactory | undefined>(undefined);

/** 登入後的外框裡呼叫：掛上時連線，離開（登出、工作階段失效）時關掉。 */
export function useLiveUpdates(notifications?: LiveNotificationHooks): void {
  const factory = use(LiveConnectionFactoryContext);
  const queryClient = useQueryClient();
  useEffect(() => {
    if (factory === undefined) return;
    const selfId = () => queryClient.getQueryData<SessionUser | null>(queryKeys.session)?.personId;
    return startLiveUpdates(queryClient, factory(), { selfId, notifications });
  }, [factory, queryClient, notifications]);
}
