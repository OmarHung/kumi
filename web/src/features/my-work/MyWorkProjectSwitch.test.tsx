import { act, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveCrossProjectItems } from '../../test/crossProjectItems';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

afterEach(() => {
  vi.unstubAllGlobals();
});

const ME = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };
const ITEMS = [
  agyItem({ number: 1, title: '接案的需求', assignee: ME, dueDate: '2026-11-20' }),
  smkItem({ number: 2, title: '冒煙的任務', assignee: ME, dueDate: '2026-11-25' }),
];

// 網址上的 state=designing 是接案專案的狀態，冒煙專案沒有：專案的內容讀到之前不能用它送請求。
const SWITCHED = (layout: string) => `/my-work?view=${layout}&project=SMK&state=designing`;

describe('我的工作：換專案時，看板、行事曆、時間軸等專案的內容讀到才送請求', () => {
  it.each([
    ['board', '看板'],
    ['calendar', '行事曆'],
    ['timeline', '時間軸'],
  ])('%s：只送一次，條件是新專案的', async (layout) => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()], items: ITEMS });
    const requests = serveCrossProjectItems(ITEMS, exampleOwner.personId);
    const { router } = renderApp(`/my-work?view=${layout}&project=AGY&state=designing`);
    await waitFor(() => {
      expect(requests.length).toBeGreaterThan(0);
    });
    const before = requests.length;

    // Act
    await act(async () => {
      await router.navigate(SWITCHED(layout));
    });

    // Assert
    await waitFor(() => {
      expect(requests.slice(before).some((url) => url.searchParams.getAll('project').includes('SMK'))).toBe(true);
    });
    const toNewProject = requests.slice(before).filter((url) => url.searchParams.getAll('project').includes('SMK') && url.searchParams.get('noDue') !== 'true');
    expect(toNewProject).toHaveLength(1);
    expect(toNewProject[0]?.searchParams.getAll('state')).toEqual([]);
    expect(screen.queryByText(/載入失敗/)).not.toBeInTheDocument();
  });
});
