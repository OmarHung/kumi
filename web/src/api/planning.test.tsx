import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import {
  createCycle,
  createMilestone,
  deleteCycle,
  deleteMilestone,
  listCycles,
  listMilestones,
  replaceCycle,
  replaceMilestone,
  usePlanningMutations,
} from './planning';
import { queryKeys } from './queryKeys';

interface Captured {
  readonly method: string;
  readonly path: string;
  readonly body: unknown;
}

function captureRequests(): Captured[] {
  const captured: Captured[] = [];
  server.use(
    http.all('/v1/*', async ({ request }) => {
      const text = await request.text();
      captured.push({ method: request.method, path: new URL(request.url).pathname, body: text === '' ? undefined : (JSON.parse(text) as unknown) });
      return HttpResponse.json({ success: true, data: { ok: true }, error: null });
    }),
  );
  return captured;
}

describe('週期與里程碑的端點', () => {
  it('週期：讀、新增、整個換掉、刪除', async () => {
    // Arrange
    const captured = captureRequests();
    const draft = { name: '衝刺 1', startDate: '2026-11-18', endDate: '2026-12-01' };

    // Act
    await listCycles('WEB');
    await createCycle('WEB', draft);
    await replaceCycle('WEB', 'c/1', draft);
    await deleteCycle('WEB', 'c/1');

    // Assert
    expect(captured).toEqual([
      { method: 'GET', path: '/v1/projects/WEB/cycles', body: undefined },
      { method: 'POST', path: '/v1/projects/WEB/cycles', body: draft },
      { method: 'PUT', path: '/v1/projects/WEB/cycles/c%2F1', body: draft },
      { method: 'DELETE', path: '/v1/projects/WEB/cycles/c%2F1', body: undefined },
    ]);
  });

  it('里程碑：讀、新增、整個換掉、刪除', async () => {
    // Arrange
    const captured = captureRequests();
    const draft = { name: 'v1.0 上線', dueDate: '2026-12-16', description: null };

    // Act
    await listMilestones('WEB');
    await createMilestone('WEB', draft);
    await replaceMilestone('WEB', 'm1', draft);
    await deleteMilestone('WEB', 'm1');

    // Assert
    expect(captured.map((request) => `${request.method} ${request.path}`)).toEqual([
      'GET /v1/projects/WEB/milestones',
      'POST /v1/projects/WEB/milestones',
      'PUT /v1/projects/WEB/milestones/m1',
      'DELETE /v1/projects/WEB/milestones/m1',
    ]);
    expect(captured[1]?.body).toEqual(draft);
  });
});

describe('週期與里程碑的快取', () => {
  function setup() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const wrapper = ({ children }: { readonly children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    const { result } = renderHook(() => usePlanningMutations('WEB'), { wrapper });
    const invalidated = () => invalidate.mock.calls.map(([filters]) => filters?.queryKey);
    return { result, invalidated };
  }

  it('新增與改名只讓週期、里程碑的清單失效：項目的歸屬沒變，不必重抓項目清單', async () => {
    // Arrange
    captureRequests();
    const { result, invalidated } = setup();

    // Act
    await result.current.createCycle.mutateAsync({ name: '衝刺 1', startDate: '2026-11-18', endDate: '2026-12-01' });
    await result.current.replaceMilestone.mutateAsync({ id: 'm1', draft: { name: 'v1', dueDate: '2026-12-16', description: null } });

    // Assert
    await waitFor(() => {
      expect(invalidated()).toEqual([queryKeys.cycles('WEB'), queryKeys.milestones('WEB')]);
    });
  });

  it.each([
    ['週期', (m: ReturnType<typeof setup>['result']['current']) => m.deleteCycle.mutateAsync('c1'), queryKeys.cycles('WEB')],
    ['里程碑', (m: ReturnType<typeof setup>['result']['current']) => m.deleteMilestone.mutateAsync('m1'), queryKeys.milestones('WEB')],
  ])('刪除%s：清單、專案的項目清單與所有單一項目的查詢都失效', async (_name, remove, listKey) => {
    // Arrange
    captureRequests();
    const { result, invalidated } = setup();

    // Act
    await remove(result.current);

    // Assert
    await waitFor(() => {
      expect(invalidated()).toEqual([listKey, queryKeys.items('WEB'), ['item']]);
    });
  });
});
