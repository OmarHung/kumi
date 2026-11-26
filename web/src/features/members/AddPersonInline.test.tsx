import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Person, ProjectDetail, SessionUser } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { agyDetail, exampleMember, exampleOwner, memberAsEditor, ownerAsAdmin } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { serveMembers } from '../../test/members';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const owner: Person = { ...ownerAsAdmin, email: exampleOwner.email, role: 'owner', disabled: false, jobTitle: null, responsibilities: null };
const member: Person = { ...memberAsEditor, email: exampleMember.email, role: 'member', disabled: false, jobTitle: null, responsibilities: null };
const created: Person = { personId: '0190a000-0000-7000-8000-000000000009', displayName: '範例新人', email: 'new@example.com', role: 'member', disabled: false, jobTitle: null, responsibilities: null };

/** 組織的人員：POST 之後清單多一個人（和真的後端一樣，之後讀到的清單有他）。 */
function servePeople(initial: readonly Person[]) {
  let people = initial;
  const posted: unknown[] = [];
  server.use(
    http.get('/v1/people', () => ok(people)),
    http.post('/v1/people', async ({ request }) => {
      posted.push(await request.json());
      people = [...people, created];
      return ok(created, 201);
    }),
  );
  return posted;
}

async function openMembers(viewer: SessionUser = exampleOwner, detail: ProjectDetail = agyDetail()) {
  signedInAs(viewer, { details: [detail] });
  serveItems('AGY', []);
  const backend = serveMembers(detail, { people: [owner, member] });
  const posted = servePeople([owner, member]);
  const view = renderApp('/projects/AGY');
  await view.user.click(await screen.findByRole('button', { name: '成員' }));
  const dialog = await screen.findByRole('dialog', { name: '成員' });
  const form = within(dialog).getByRole('form', { name: '加入成員' });
  await waitFor(() => {
    expect(within(form).getByRole('combobox', { name: '人員' })).not.toHaveTextContent('載入中…');
  });
  return { ...view, dialog, form, backend, posted };
}

describe('加成員時要加的人不在清單裡', () => {
  it('組織的管理員：人員選單下面有「新增人員…」，就地展開表單（不再開一層對話框），焦點到名稱', async () => {
    // Arrange
    const { user, dialog, form } = await openMembers();
    const toggle = within(form).getByRole('button', { name: '新增人員…' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(within(form).getByRole('combobox', { name: '人員' })).toHaveAccessibleDescription('組織裡的人都已經是成員了。');

    // Act
    await user.click(toggle);

    // Assert
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    const inline = within(dialog).getByRole('form', { name: '新增人員' });
    expect(toggle).toHaveAttribute('aria-controls', inline.id);
    expect(within(inline).getByLabelText('名稱')).toHaveFocus();
    expect(within(inline).getByLabelText('組織角色')).toHaveTextContent('成員');
    expect(within(inline).getByText('新的人用這裡設定的 Email 與密碼登入；建好之後會自動選進上面的「人員」。')).toBeInTheDocument();
  });

  it('建好之後自動選在「人員」裡、焦點回到「人員」，接著按「加入」就加進專案', async () => {
    // Arrange
    const { user, dialog, form, backend, posted } = await openMembers();
    await user.click(within(form).getByRole('button', { name: '新增人員…' }));
    const inline = within(dialog).getByRole('form', { name: '新增人員' });

    // Act
    await user.type(within(inline).getByLabelText('名稱'), ' 範例新人 ');
    await user.type(within(inline).getByLabelText('Email'), 'new@example.com');
    await user.type(within(inline).getByLabelText('密碼'), 'example-pass-1');
    await user.click(within(inline).getByRole('button', { name: '新增人員' }));

    // Assert
    const personSelect = within(form).getByRole('combobox', { name: '人員' });
    await waitFor(() => {
      expect(personSelect).toHaveTextContent('範例新人（new@example.com）');
    });
    expect(posted).toEqual([{ displayName: '範例新人', email: 'new@example.com', password: 'example-pass-1', role: 'member' }]);
    expect(within(dialog).queryByRole('form', { name: '新增人員' })).not.toBeInTheDocument();
    expect(personSelect).toHaveFocus();
    expect(within(dialog).getByRole('status')).toHaveTextContent('已新增人員「範例新人」，選好角色後按「加入」。');

    // Act
    await user.click(within(form).getByRole('button', { name: '加入' }));

    // Assert
    expect(await within(dialog).findByRole('status')).toHaveTextContent('已加入範例新人（編輯者）。');
    expect(backend.puts).toEqual([{ personId: created.personId, body: { role: 'editor' } }]);
  });

  it('「取消」收起來、焦點回到「新增人員…」；在表單裡按 Esc 也只收起表單，成員對話框還在', async () => {
    // Arrange
    const { user, dialog, form } = await openMembers();
    const toggle = within(form).getByRole('button', { name: '新增人員…' });
    await user.click(toggle);

    // Act
    await user.click(within(within(dialog).getByRole('form', { name: '新增人員' })).getByRole('button', { name: '取消' }));

    // Assert
    expect(within(dialog).queryByRole('form', { name: '新增人員' })).not.toBeInTheDocument();
    expect(toggle).toHaveFocus();

    // Act
    await user.click(toggle);
    await user.type(within(within(dialog).getByRole('form', { name: '新增人員' })).getByLabelText('名稱'), '打一半');
    await user.keyboard('{Escape}');

    // Assert
    expect(within(dialog).queryByRole('form', { name: '新增人員' })).not.toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '成員' })).toBeInTheDocument();
    expect(toggle).toHaveFocus();
  });

  it('輸入法選字中按 Esc（Safari 是 keyCode 229）只交給輸入法：表單與成員對話框都留著', async () => {
    // Arrange
    const { user, dialog, form } = await openMembers();
    await user.click(within(form).getByRole('button', { name: '新增人員…' }));
    const name = within(within(dialog).getByRole('form', { name: '新增人員' })).getByLabelText('名稱');

    // Act
    fireEvent.keyDown(name, { key: 'Escape', keyCode: 229 });
    fireEvent.keyDown(name, { key: 'Escape', isComposing: true });

    // Assert
    expect(within(dialog).getByRole('form', { name: '新增人員' })).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '成員' })).toBeInTheDocument();
  });

  it('欄位有問題時在欄位旁說明、不送出（和人員頁同一套檢查）', async () => {
    // Arrange
    const { user, dialog, form, posted } = await openMembers();
    await user.click(within(form).getByRole('button', { name: '新增人員…' }));
    const inline = within(dialog).getByRole('form', { name: '新增人員' });

    // Act
    await user.type(within(inline).getByLabelText('Email'), 'new@example.com');
    await user.type(within(inline).getByLabelText('密碼'), 'short');
    await user.click(within(inline).getByRole('button', { name: '新增人員' }));

    // Assert
    expect(within(inline).getByLabelText('名稱')).toHaveAccessibleDescription('請輸入名稱。');
    expect(within(inline).getByLabelText('名稱')).toHaveFocus();
    expect(posted).toHaveLength(0);
  });

  it('「加入」送出中停用但停得到（aria-disabled）：焦點不會掉到頁面本體，Esc 照樣關得掉對話框；不會重複送出', async () => {
    // Arrange
    const detail = agyDetail({ members: [ownerAsAdmin] });
    const { user, form } = await openMembers(exampleOwner, detail);
    let release: () => void = () => undefined;
    const puts: string[] = [];
    server.use(
      http.put('/v1/projects/AGY/members/:personId', async ({ params }) => {
        puts.push(String(params.personId));
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return ok({ ...memberAsEditor, role: 'editor' });
      }),
    );
    await chooseOption(user, within(form).getByRole('combobox', { name: '人員' }), '範例成員（member@example.com）');

    // Act
    await user.click(within(form).getByRole('button', { name: '加入' }));
    const pending = await within(form).findByRole('button', { name: '加入中…' });
    await user.click(pending);

    // Assert
    expect(pending).not.toBeDisabled();
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    expect(puts).toHaveLength(1);
    release();
    expect(await within(form).findByRole('button', { name: '加入' })).toHaveFocus();
  });

  it('不是組織管理員的專案管理員：沒有「新增人員…」，說明要找組織的管理員', async () => {
    // Arrange & Act
    const detail = agyDetail({ members: [ownerAsAdmin, memberAsEditor] });
    const { form } = await openMembers(exampleMember, detail);

    // Assert
    expect(within(form).queryByRole('button', { name: '新增人員…' })).not.toBeInTheDocument();
    expect(within(form).getByRole('combobox', { name: '人員' })).toHaveAccessibleDescription(
      '組織裡的人都已經是成員了。要加別人，請組織的管理員先到「人員」新增。',
    );
    expect(within(form).queryByRole('link')).not.toBeInTheDocument();
  });

  it('不是組織管理員、清單裡還有人時：說明「要加的人不在清單裡？」', async () => {
    // Arrange & Act
    const detail = agyDetail({ members: [memberAsEditor] });
    const { form } = await openMembers(exampleMember, detail);

    // Assert
    expect(within(form).getByRole('combobox', { name: '人員' })).toHaveAccessibleDescription(
      '要加的人不在清單裡？請組織的管理員先到「人員」新增。',
    );
  });
});
