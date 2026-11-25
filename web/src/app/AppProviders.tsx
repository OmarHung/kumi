import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { useEffect, type ReactNode } from 'react';
import { onAuthRequired } from '../api/http';
import { handleUnauthorized } from '../auth/session';
import { LiveConnectionFactoryContext } from '../features/live/LiveUpdates';
import type { LiveConnectionFactory } from '../features/live/liveTypes';

interface AppProvidersProps {
  readonly queryClient: QueryClient;
  /** 即時連線；沒給就不連（測試）。 */
  readonly liveConnection?: LiveConnectionFactory;
  readonly children: ReactNode;
}

export function AppProviders({ queryClient, liveConnection, children }: AppProvidersProps) {
  // 任何請求收到 401：先重問一次登入狀態，還是 401 才標成「沒登入」；登入檢查會接手導到登入頁。
  useEffect(() => onAuthRequired(() => {
    void handleUnauthorized(queryClient);
  }), [queryClient]);

  return (
    <QueryClientProvider client={queryClient}>
      <LiveConnectionFactoryContext value={liveConnection}>{children}</LiveConnectionFactoryContext>
    </QueryClientProvider>
  );
}
