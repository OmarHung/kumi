import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { memberRef, ownerRef } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { makeChangeSet, serveChangeSets } from '../../test/changesets';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { formatInstant } from '../../domain/time';

describe('變更集清單', () => {
  it('編號、摘要、狀態、提出的人、時間，新的在前；編號連到預覽頁', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', [
      makeChangeSet({ number: 3, summary: '把驗收拆成兩段', proposedBy: memberRef, proposedAt: '2026-11-15T04:00:00Z' }),
      makeChangeSet({ number: 2, status: 'applied', appliedVersion: 2, proposedBy: ownerRef }),
      makeChangeSet({ number: 1, status: 'rejected', summary: '試試看' }),
    ]);
    const { user, router } = renderApp('/projects/SMK/changesets');

    // Act
    const table = await screen.findByRole('table', { name: '變更集' });

    // Assert
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell').map((cell) => cell.textContent))).toEqual([
      ['把驗收拆成兩段', '等待核准', '範例成員', formatInstant('2026-11-15T04:00:00Z')],
      ['沒有摘要', '已套用（第 2 版）', '範例使用者', formatInstant('2026-11-15T02:00:00Z')],
      ['試試看', '已放棄', '範例使用者', formatInstant('2026-11-15T02:00:00Z')],
    ]);
    await user.click(within(table).getByRole('link', { name: '#3' }));
    expect(currentPath(router)).toBe('/projects/SMK/changesets/3');
  });

  it('還沒有變更集時說明怎麼產生，連到設定頁', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', []);

    // Act
    renderApp('/projects/SMK/changesets');

    // Assert
    expect(await screen.findByText('還沒有變更集')).toBeInTheDocument();
    const links = screen.getAllByRole('link', { name: '到設定頁修改' });
    expect(links[0]).toHaveAttribute('href', '/projects/SMK/settings');
  });

  it('讀不到時可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', [makeChangeSet({ number: 1 })]);
    server.use(http.get('/v1/projects/SMK/changesets', () => failure(503, 'server.not_ready', 'starting'), { once: true }));
    const { user } = renderApp('/projects/SMK/changesets');

    // Act
    const alert = await screen.findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(await screen.findByRole('link', { name: '#1' })).toBeInTheDocument();
  });

  it('沒有 blueprint.read 時說沒有權限，也不讀清單', async () => {
    // Arrange（沒有宣告清單的端點：讀了測試就會失敗）
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: ['item.read'] })] });

    // Act
    renderApp('/projects/SMK/changesets');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '沒有權限看這個專案的變更集' })).toBeInTheDocument();
  });
});

describe('變更集清單：AI 助理提出的', () => {
  it('提出的人旁邊有「AI 助理提出」，直接提出的沒有', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', [makeChangeSet({ number: 2, origin: 'assistant' }), makeChangeSet({ number: 1 })]);

    // Act
    renderApp('/projects/SMK/changesets');

    // Assert
    const table = await screen.findByRole('table', { name: '變更集' });
    expect(within(table).getAllByText('AI 助理提出')).toHaveLength(1);
  });

  it('外部的 AI 代理提出的標「AI 代理提出」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', [makeChangeSet({ number: 2, origin: 'agent' }), makeChangeSet({ number: 1 })]);

    // Act
    renderApp('/projects/SMK/changesets');

    // Assert
    const table = await screen.findByRole('table', { name: '變更集' });
    expect(within(table).getAllByText('AI 代理提出')).toHaveLength(1);
  });
});
