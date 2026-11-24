import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems, servePatch } from '../../test/items';
import { createFakeLiveConnection } from '../../test/liveConnection';
import { renderApp } from '../../test/renderApp';

const task = smkItem({ number: 7, title: '首頁設計稿', estimate: 2 });

describe('即時更新：表格的儲存格正在編輯', () => {
  it('輸入開著時先記著，不重抓；關掉輸入之後才失效、重抓', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveItems('SMK', [task]);
    const live = createFakeLiveConnection();
    const { user } = renderApp('/projects/SMK', { liveConnection: () => live });
    const table = await screen.findByRole('table', { name: '項目' });
    const row = within(table).getByRole('row', { name: /SMK-7/ });
    await user.click(within(row).getByRole('button', { name: '估計：2' }));
    const input = within(row).getByRole('textbox', { name: '估計' });
    await user.clear(input);
    await user.type(input, '9');
    const before = requests.length;

    // Act：別人改了東西
    serveItems('SMK', [smkItem({ number: 7, title: '別人改的標題', estimate: 2 })]);
    live.emitItems({ projectKey: 'SMK', itemKeys: ['SMK-7'], actorId: 'someone-else' });
    await new Promise((resolve) => setTimeout(resolve, 600));

    // Assert：沒送出的輸入還在，清單沒有被換掉
    expect(within(row).getByRole('textbox', { name: '估計' })).toHaveValue('9');
    expect(screen.queryByText('別人改的標題')).not.toBeInTheDocument();
    expect(requests.length).toBe(before);

    // Act：取消輸入
    await user.keyboard('{Escape}');

    // Assert
    expect(await screen.findByText('別人改的標題')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('textbox', { name: '估計' })).not.toBeInTheDocument();
    });
  });

  it('自己剛儲存的修改被推回來：不重抓，列停在原位；別人的修改照舊重抓', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const requests = serveItems('SMK', [task]);
    servePatch(task);
    const live = createFakeLiveConnection();
    const { user } = renderApp('/projects/SMK', { liveConnection: () => live });
    const table = await screen.findByRole('table', { name: '項目' });
    const row = within(table).getByRole('row', { name: /SMK-7/ });
    await user.click(within(row).getByRole('button', { name: '估計：2' }));
    const input = within(row).getByRole('textbox', { name: '估計' });
    await user.clear(input);
    await user.type(input, '5{Enter}');
    await within(row).findByRole('button', { name: '估計：5' });
    const before = requests.length;

    // Act
    live.emitItems({ projectKey: 'SMK', itemKeys: ['SMK-7'], actorId: exampleOwner.personId });
    await new Promise((resolve) => setTimeout(resolve, 600));

    // Assert
    expect(requests.length).toBe(before);

    // Act：別人改的
    live.emitItems({ projectKey: 'SMK', itemKeys: ['SMK-7'], actorId: 'someone-else' });

    // Assert
    await waitFor(() => {
      expect(requests.length).toBeGreaterThan(before);
    });
  });
});
