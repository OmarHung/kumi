import { screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { formatInstant } from '../../domain/time';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { memberRef } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { makeChangeSet, serveChangeSets } from '../../test/changesets';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';

describe('變更集清單：窄螢幕', () => {
  beforeEach(() => {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('不是橫向捲動的表格，是一筆兩行的清單：編號與摘要、狀態與提出的人與時間；編號連到預覽頁', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', [
      makeChangeSet({ number: 3, summary: '把驗收拆成兩段', proposedBy: memberRef, proposedAt: '2026-11-15T04:00:00Z' }),
      makeChangeSet({ number: 2, status: 'applied', appliedVersion: 2 }),
    ]);
    const { user, router } = renderApp('/projects/SMK/changesets');

    // Act
    const list = await screen.findByRole('list', { name: '變更集' });

    // Assert
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    const [first, second] = within(list).getAllByRole('listitem');
    expect(first).toHaveTextContent('#3');
    expect(first).toHaveTextContent('把驗收拆成兩段');
    expect(first).toHaveTextContent('等待核准');
    expect(first).toHaveTextContent('範例成員');
    expect(first).toHaveTextContent(formatInstant('2026-11-15T04:00:00Z'));
    expect(second).toHaveTextContent('沒有摘要');
    expect(second).toHaveTextContent('已套用（第 2 版）');
    // 整筆是一個連結（components/List）：名稱是編號、摘要，再接第二行（狀態、提出的人與時間）。
    await user.click(within(list).getByRole('link', { name: /^#3 把驗收拆成兩段 狀態：等待核准 提出的人：範例成員 提出時間：/ }));
    expect(currentPath(router)).toBe('/projects/SMK/changesets/3');
  });
});
