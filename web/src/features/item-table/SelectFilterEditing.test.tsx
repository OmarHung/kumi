import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Member } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { exampleOwner, ownerAsAdmin, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

/*
 * 格子裡選項多的下拉（負責人有 10 個以上的選項）：進入編輯時清單打開、焦點在篩選框（docs/DESIGN.md 7.3「選項很多的時候」）。
 * 格子的語意照舊（3A.3）：選了就存、焦點回到格子；Esc 一次就關上並取消；Tab 是離開格子。
 */

const crew: readonly Member[] = Array.from({ length: 11 }, (_, index) => ({
  personId: `0190a000-0000-7000-8000-0000000002${String(index).padStart(2, '0')}`,
  displayName: index === 4 ? '範例設計師' : `範例成員${String(index + 1)}`,
  role: 'editor',
}));

const task = smkItem({ number: 7, title: '整理素材', assignee: { personId: exampleOwner.personId, displayName: exampleOwner.displayName } });

async function openAssigneeEditor() {
  signedInAs(exampleOwner, { details: [smkDetail({ members: [ownerAsAdmin, ...crew] })] });
  serveItems('SMK', [task]);
  const bodies: unknown[] = [];
  server.use(
    http.patch(`/v1/items/${task.key}`, async ({ request }) => {
      const body = (await request.json()) as { assigneeId: string | null };
      bodies.push(body);
      const person = crew.find((member) => member.personId === body.assigneeId);
      return ok({ ...task, assignee: person === undefined ? null : { personId: person.personId, displayName: person.displayName } });
    }),
  );
  const view = renderApp('/projects/SMK');
  const row = within(await screen.findByRole('table', { name: '項目' })).getByRole('row', { name: /SMK-7/ });
  await view.user.click(within(row).getByRole('button', { name: '負責人：範例使用者' }));
  const filter = await within(row).findByRole('combobox', { name: '篩選 負責人' });
  return { ...view, row, filter, bodies };
}

describe('格子裡選項多的下拉', () => {
  it('進入編輯：清單打開、焦點在篩選框（不是按鈕）', async () => {
    // Arrange / Act
    const { filter } = await openAssigneeEditor();

    // Assert
    await waitFor(() => {
      expect(filter).toHaveFocus();
    });
  });

  it('打字篩選、Enter 選了就存，焦點回到格子', async () => {
    // Arrange
    const { user, row, filter, bodies } = await openAssigneeEditor();
    await waitFor(() => {
      expect(filter).toHaveFocus();
    });

    // Act
    await user.keyboard('設計{Enter}');

    // Assert
    expect(await within(row).findByRole('button', { name: '負責人：範例設計師' })).toHaveFocus();
    expect(bodies).toEqual([{ assigneeId: crew[4]?.personId }]);
  });

  it('Esc 一次就關上清單並取消編輯：不送出，焦點回到格子', async () => {
    // Arrange
    const { user, row, filter, bodies } = await openAssigneeEditor();
    await waitFor(() => {
      expect(filter).toHaveFocus();
    });

    // Act
    await user.keyboard('設計{Escape}');

    // Assert
    expect(await within(row).findByRole('button', { name: '負責人：範例使用者' })).toHaveFocus();
    expect(within(row).queryByRole('combobox')).not.toBeInTheDocument();
    await flushPending();
    expect(bodies).toHaveLength(0);
  });
});
