import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { http } from 'msw';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { queryKeys } from '../../api/queryKeys';
import { ok } from '../../test/api';
import { agyDetail } from '../../test/fixtures';
import { server } from '../../test/server';
import { useSetMemberRole } from '../members/memberRoles';
import { useArchiveProject, useRenameProject, useUnarchiveProject } from './useProjectLifecycle';

/** 這些寫入會讓「專案總覽」列的專案或數字變動：沒有失效的話，回到總覽要等下一次進來才換。 */
function setup() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(queryKeys.overview('managed'), []);
  const wrapper = ({ children }: { readonly children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  server.use(
    http.post('/v1/projects/AGY/archive', () => ok(agyDetail().project)),
    http.post('/v1/projects/AGY/unarchive', () => ok(agyDetail().project)),
    http.put('/v1/projects/AGY/name', () => ok(agyDetail().project)),
    http.put('/v1/projects/AGY/members/:personId', () => ok({})),
  );
  const isOverviewInvalidated = () => queryClient.getQueryState(queryKeys.overview('managed'))?.isInvalidated === true;
  return { wrapper, isOverviewInvalidated };
}

describe('寫入之後讓專案總覽失效', () => {
  it('封存成功後', async () => {
    // Arrange
    const { wrapper, isOverviewInvalidated } = setup();
    const { result } = renderHook(() => useArchiveProject('AGY'), { wrapper });

    // Act
    await act(async () => {
      await result.current.mutateAsync();
    });

    // Assert
    await waitFor(() => { expect(isOverviewInvalidated()).toBe(true); });
  });

  it('取消封存成功後', async () => {
    // Arrange
    const { wrapper, isOverviewInvalidated } = setup();
    const { result } = renderHook(() => useUnarchiveProject('AGY'), { wrapper });

    // Act
    await act(async () => {
      await result.current.mutateAsync();
    });

    // Assert
    await waitFor(() => { expect(isOverviewInvalidated()).toBe(true); });
  });

  it('改名成功後', async () => {
    // Arrange
    const { wrapper, isOverviewInvalidated } = setup();
    const { result } = renderHook(() => useRenameProject('AGY'), { wrapper });

    // Act
    await act(async () => {
      await result.current.mutateAsync('新名稱');
    });

    // Assert
    await waitFor(() => { expect(isOverviewInvalidated()).toBe(true); });
  });

  it('變更成員的角色成功後', async () => {
    // Arrange
    const { wrapper, isOverviewInvalidated } = setup();
    const { result } = renderHook(() => useSetMemberRole('AGY'), { wrapper });

    // Act
    await act(async () => {
      await result.current.mutateAsync({ personId: 'person-1', role: 'admin' });
    });

    // Assert
    await waitFor(() => { expect(isOverviewInvalidated()).toBe(true); });
  });
});
