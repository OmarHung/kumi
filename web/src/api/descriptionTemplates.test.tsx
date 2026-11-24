import { QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { http } from 'msw';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { createQueryClient } from '../app/queryClient';
import { failure } from '../test/api';
import { server } from '../test/server';
import { useDescriptionTemplates } from './projectDescription';

describe('說明格式的查詢', () => {
  it('讀不到時立刻是失敗（不重試）：新增專案的「建立」不會被重試的間隔卡住', async () => {
    // Arrange：用正式的設定（其他查詢最多重試兩次、間隔一秒起跳），不是測試用的「不重試」。
    let requests = 0;
    server.use(
      http.get('/v1/description-templates', () => {
        requests += 1;
        return failure(500, 'server.error', '伺服器出錯了。');
      }),
    );
    const client = createQueryClient();
    const wrapper = ({ children }: { readonly children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;

    // Act
    const { result } = renderHook(() => useDescriptionTemplates(), { wrapper });

    // Assert
    await waitFor(() => { expect(result.current.isError).toBe(true); }, { timeout: 800 });
    expect(requests).toBe(1);
  });
});
