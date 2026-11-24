import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Person } from '../../api/types';
import { signedInAs } from '../../test/api';
import { agyDetail, exampleOwner, ownerAsAdmin } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { serveMembers } from '../../test/members';
import { ownerPerson } from '../../test/people';
import { renderApp } from '../../test/renderApp';
import { openOptions } from '../../test/select';

const active: Person = { personId: '0190a000-0000-7000-8000-000000000004', displayName: '範例新人', email: 'new@example.com', role: 'member', disabled: false };
const disabled: Person = { personId: '0190a000-0000-7000-8000-000000000007', displayName: '範例離職者', email: 'left@example.com', role: 'member', disabled: true };

describe('加入成員：候選的人', () => {
  it('停用的人不在候選裡（他不能登入，加進來也用不了）', async () => {
    // Arrange
    const detail = agyDetail({ members: [ownerAsAdmin] });
    signedInAs(exampleOwner, { details: [detail] });
    serveItems('AGY', []);
    serveMembers(detail, { people: [ownerPerson, active, disabled] });
    const { user } = renderApp('/projects/AGY');
    await user.click(await screen.findByRole('button', { name: '成員' }));
    const dialog = await screen.findByRole('dialog', { name: '成員' });
    const personSelect = within(within(dialog).getByRole('form', { name: '加入成員' })).getByRole('combobox', { name: '人員' });
    await waitFor(() => {
      expect(personSelect).toHaveTextContent('選擇人員');
    });

    // Act
    const options = await openOptions(user, personSelect);

    // Assert
    expect(options.map((option) => option.dataset.value)).toEqual([active.personId]);
  });
});
