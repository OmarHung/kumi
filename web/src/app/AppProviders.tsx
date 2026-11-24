import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { useEffect, type ReactNode } from 'react';
import { onAuthRequired } from '../api/http';
import { markSignedOut } from '../auth/session';
import { LiveConnectionFactoryContext } from '../features/live/LiveUpdates';
import type { LiveConnectionFactory } from '../features/live/liveTypes';

interface AppProvidersProps {
  readonly queryClient: QueryClient;
  /** 即時連線；沒給就不連（測試）。 */
  readonly liveConnection?: LiveConnectionFactory;
  readonly children: ReactNode;
}

export function AppProviders({ queryClient, liveConnection, children }: AppProvidersProps) {
  // 任何請求發現工作階段失效，就把登入狀態標成「沒登入」；登入檢查會接手導到登入頁。
  useEffect(() => onAuthRequired(() => {
    markSignedOut(queryClient);
  }), [queryClient]);

  return (
    <QueryClientProvider client={queryClient}>
      <LiveConnectionFactoryContext value={liveConnection}>{children}</LiveConnectionFactoryContext>
    </QueryClientProvider>
  );
}
