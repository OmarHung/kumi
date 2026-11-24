import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { created, serveActivity } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const CONTRIBUTOR = ['item.read_own', 'item.create', 'item.update', 'item.transition', 'comment.create', 'blueprint.read'] as const;
const child = detailOf(agyItem({ number: 5, type: 'task', title: '首頁切版', state: 'doing', stateCategory: 'in_progress', parentKey: 'AGY-1' }));

function openChild(permissions: readonly (typeof CONTRIBUTOR)[number][] | undefined, parentReads: string[], parent: 'hidden' | 'visible') {
  signedInAs(exampleOwner, { details: [permissions === undefined ? agyDetail() : agyDetail({ myPermissions: permissions })] });
  serveItem(child);
  serveActivity(child.key, [created()]);
  server.use(
    http.get('/v1/items/AGY-1', () => {
      parentReads.push('get');
      return parent === 'hidden' ? failure(404, 'item.not_found', '找不到項目。') : ok(detailOf(agyItem({ number: 1, title: '官網需求' })));
    }),
  );
  return renderApp('/projects/AGY/items/AGY-5');
}

describe('欄位區：上層項目', () => {
  it('只看自己的人，上層讀不到（404）：編號旁寫「（你看不到這個項目）」，沒有連結與「開啟」', async () => {
    // Arrange
    const reads: string[] = [];

    // Act
    openChild(CONTRIBUTOR, reads, 'hidden');
    const fields = await screen.findByRole('region', { name: '欄位' });

    // Assert
    expect(await within(fields).findByText('（你看不到這個項目）')).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '上層項目：AGY-1' })).toBeInTheDocument();
    expect(within(fields).queryByRole('link', { name: '開啟上層項目 AGY-1' })).not.toBeInTheDocument();
    expect(reads).toHaveLength(1);
  });

  it('只看自己的人，上層讀得到：照舊給「開啟」', async () => {
    // Arrange
    const reads: string[] = [];

    // Act
    openChild(CONTRIBUTOR, reads, 'visible');
    const fields = await screen.findByRole('region', { name: '欄位' });

    // Assert
    expect(await within(fields).findByRole('link', { name: '開啟上層項目 AGY-1' })).toBeInTheDocument();
    expect(within(fields).queryByText('（你看不到這個項目）')).not.toBeInTheDocument();
  });

  it('看得到全部的人：不多讀一次上層，照舊給「開啟」', async () => {
    // Arrange
    const reads: string[] = [];

    // Act
    openChild(undefined, reads, 'visible');
    const fields = await screen.findByRole('region', { name: '欄位' });

    // Assert
    expect(await within(fields).findByRole('link', { name: '開啟上層項目 AGY-1' })).toBeInTheDocument();
    expect(reads).toHaveLength(0);
  });
});
