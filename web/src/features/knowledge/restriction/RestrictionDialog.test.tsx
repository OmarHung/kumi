import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, ok, signedInAs } from '../../../test/api';
import { exampleOwner, projectDetail, webProject } from '../../../test/fixtures';
import { knowledgeSpaceOf, pageNodeOf, serveKnowledge } from '../../../test/knowledge';
import { adminPerson, guestPerson, memberPerson, ownerPerson } from '../../../test/people';
import { renderApp } from '../../../test/renderApp';
import { chooseOption, openOptions } from '../../../test/select';
import { server } from '../../../test/server';

const disabledPerson = { ...adminPerson, personId: 'p-gone', displayName: '已停用的人', email: 'gone@example.com', disabled: true };
const readOnlySpace = knowledgeSpaceOf({ myPermissions: { canEdit: false, canComment: false, canManage: false } });

function openPage(path: string, options: Parameters<typeof serveKnowledge>[0] = {}, signedIn: Parameters<typeof signedInAs>[1] = {}) {
  signedInAs(exampleOwner, signedIn);
  server.use(http.get('/v1/people', () => ok([ownerPerson, memberPerson, adminPerson, disabledPerson])));
  const backend = serveKnowledge(options);
  return { backend, ...renderApp(path) };
}

function putsOf(backend: ReturnType<typeof serveKnowledge>) {
  return backend.requests.filter((request) => request.method === 'PUT').map((request) => request.body);
}

async function openFromPage(view: ReturnType<typeof openPage>) {
  await view.user.click(await screen.findByRole('button', { name: '限制…' }));
  return screen.findByRole('dialog', { name: '限制「帳號申請」' });
}

describe('「限制…」對話框：入口', () => {
  it('頁面上那一行的「限制…」打開對話框，說明誰看得到，Esc 關掉', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/2');

    // Act
    const dialog = await openFromPage(view);

    // Assert
    expect(within(dialog).getByText('只有名單上的人看得到這一頁與底下的頁面；組織管理員一律看得到。')).toBeInTheDocument();
    await view.user.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('樹的選單在「搬移…」與「刪除…」之間有「限制…」，選了打開那一頁的對話框', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS');
    await view.user.click(await screen.findByRole('button', { name: '「會議記錄」的更多動作' }));

    // Act
    const menu = await screen.findByRole('menu', { name: '「會議記錄」的更多動作' });
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['新增子頁面', '搬移…', '限制…', '刪除…']);
    await view.user.click(within(menu).getByRole('menuitem', { name: '限制…' }));

    // Assert
    expect(await screen.findByRole('dialog', { name: '限制「會議記錄」' })).toBeInTheDocument();
  });

  it('沒有 canEdit：沒有限制的頁面沒有入口；有限制的頁面是「查看名單」，打開只有名單、沒有任何改的按鈕', async () => {
    // Arrange
    const tree = [pageNodeOf({ number: 1, title: '人事', isRestricted: true })];
    const view = openPage('/knowledge/DOCS/1', { spaces: [readOnlySpace], trees: { DOCS: tree } });
    const backend = view.backend;
    backend.restrictions['DOCS/1'] = [ownerPerson.personId, memberPerson.personId];

    // Act
    await view.user.click(await screen.findByRole('button', { name: '查看名單' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '限制「人事」' });
    const list = await within(dialog).findByRole('list', { name: '看得到這一頁的人' });
    expect(within(list).getAllByRole('listitem').map((row) => row.textContent)).toEqual([expect.stringMatching(/範例使用者（你）$/), expect.stringMatching(/範例成員$/)]);
    expect(within(dialog).queryByRole('button', { name: '儲存' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: '拿掉限制' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('沒有 canEdit 又沒有限制的頁面：沒有入口', async () => {
    // Arrange & Act
    openPage('/knowledge/DOCS/2', { spaces: [readOnlySpace] });

    // Assert
    await screen.findByRole('heading', { level: 1, name: '帳號申請' });
    expect(screen.queryByRole('button', { name: '限制…' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '查看名單' })).not.toBeInTheDocument();
  });
});

describe('「限制…」對話框：設定', () => {
  it('沒有限制時按「限制這一頁」：名單先有自己，選人加進來，儲存送出自己在最前面的名單，頁面與樹都出現鎖', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/2');
    const dialog = await openFromPage(view);

    // Act
    await view.user.click(await within(dialog).findByRole('button', { name: '限制這一頁' }));
    await chooseOption(view.user, await within(dialog).findByRole('combobox', { name: '加入人員' }), '範例成員（member@example.com）');
    await view.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(putsOf(view.backend)).toEqual([{ people: [ownerPerson.personId, memberPerson.personId] }]);
    expect(await screen.findByText(/限制：只有指定的人看得到/)).toBeInTheDocument();
    expect(await screen.findByRole('img', { name: '限制' })).toBeInTheDocument();
  });

  it('選人的清單不列停用的人、已在名單上的人', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/2');
    const dialog = await openFromPage(view);
    await view.user.click(await within(dialog).findByRole('button', { name: '限制這一頁' }));

    // Act
    const options = await openOptions(view.user, await within(dialog).findByRole('combobox', { name: '加入人員' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual([
      expect.stringMatching(/範例成員（member@example.com）$/),
      expect.stringMatching(/範例管理員（admin@example.com）$/),
    ]);
  });

  it('可以移除名單上的別人，自己沒有移除的按鈕；沒改的時候儲存是停用的', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/1', { trees: { DOCS: [pageNodeOf({ number: 1, title: '人事', isRestricted: true })] } });
    view.backend.restrictions['DOCS/1'] = [ownerPerson.personId, memberPerson.personId];
    await view.user.click(await screen.findByRole('button', { name: '限制…' }));
    const dialog = await screen.findByRole('dialog', { name: '限制「人事」' });
    const save = await within(dialog).findByRole('button', { name: '儲存' });
    expect(save).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: '移除 範例使用者' })).not.toBeInTheDocument();

    // Act
    await view.user.click(within(dialog).getByRole('button', { name: '移除 範例成員' }));

    // Assert
    expect(within(dialog).queryByText('範例成員')).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: '儲存' })).toBeEnabled();
    expect(within(dialog).getByRole('combobox', { name: '加入人員' })).toHaveFocus();
    await view.user.click(within(dialog).getByRole('button', { name: '儲存' }));
    await waitFor(() => {
      expect(putsOf(view.backend)).toEqual([{ people: [ownerPerson.personId] }]);
    });
  });

  it('「拿掉限制」一次就送出 null 並關掉對話框，鎖不見', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/1', { trees: { DOCS: [pageNodeOf({ number: 1, title: '人事', isRestricted: true })] } });
    view.backend.restrictions['DOCS/1'] = [ownerPerson.personId, memberPerson.personId];
    await view.user.click(await screen.findByRole('button', { name: '限制…' }));
    const dialog = await screen.findByRole('dialog', { name: '限制「人事」' });

    // Act
    await view.user.click(await within(dialog).findByRole('button', { name: '拿掉限制' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(putsOf(view.backend)).toEqual([{ people: null }]);
    await waitFor(() => {
      expect(screen.queryByText(/限制：只有指定的人看得到/)).not.toBeInTheDocument();
    });
  });

  it('專案空間：專案成員排在前面，其他人在後面', async () => {
    // Arrange
    const space = knowledgeSpaceOf({ id: 'space-web', key: 'WEB', project: { key: 'WEB', name: '官網改版', isArchived: false } });
    const detail = { ...projectDetail(webProject), members: [{ personId: adminPerson.personId, displayName: adminPerson.displayName, role: 'editor' }] };
    const view = openPage('/knowledge/WEB/1', { spaces: [space], trees: { WEB: [pageNodeOf({ number: 1, title: '部署' })] } }, { details: [detail] });
    await view.user.click(await screen.findByRole('button', { name: '限制…' }));
    const dialog = await screen.findByRole('dialog', { name: '限制「部署」' });
    await view.user.click(await within(dialog).findByRole('button', { name: '限制這一頁' }));

    // Act
    const options = await openOptions(view.user, await within(dialog).findByRole('combobox', { name: '加入人員' }));

    // Assert
    await waitFor(() => {
      expect(options.map((option) => option.textContent)).toEqual([
        expect.stringMatching(/範例管理員（admin@example.com）$/),
        expect.stringMatching(/範例成員（member@example.com）$/),
      ]);
    });
  });
});

describe('「限制…」對話框：繼承與錯誤', () => {
  it('上層有限制：寫出上層是哪幾頁並連過去', async () => {
    // Arrange
    const tree = [pageNodeOf({ number: 1, title: '人事', isRestricted: true }), pageNodeOf({ number: 2, title: '薪資', parentId: 'page-1' })];
    const view = openPage('/knowledge/DOCS/2', { trees: { DOCS: tree } });

    // Act
    const dialog = await screen.findByRole('button', { name: '限制…' }).then(async (button) => {
      await view.user.click(button);
      return screen.findByRole('dialog', { name: '限制「薪資」' });
    });

    // Assert
    const link = await within(dialog).findByRole('link', { name: '「人事」' });
    expect(link).toHaveAttribute('href', '/knowledge/DOCS/1');
    expect(within(dialog).getByText(/上層也有限制/)).toBeInTheDocument();
  });

  it('伺服器 422：people[i] 的訊息寫在那個人底下，people 的寫在表單上方', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/2');
    server.use(
      http.put('/v1/knowledge/spaces/:key/pages/:number/restriction', () =>
        failure(422, 'knowledge.restriction_invalid', '名單有問題。', [
          { path: 'people[1]', code: 'knowledge.restriction_person_unknown', message: '這個人已經不在組織裡。' },
          { path: 'people', code: 'knowledge.restriction_invalid', message: '名單不能超過 100 人。' },
        ]),
      ),
    );
    const dialog = await openFromPage(view);
    await view.user.click(await within(dialog).findByRole('button', { name: '限制這一頁' }));
    await chooseOption(view.user, await within(dialog).findByRole('combobox', { name: '加入人員' }), '範例成員（member@example.com）');

    // Act
    await view.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    const row = (await within(dialog).findByText('這個人已經不在組織裡。')).closest('li');
    expect(row).toHaveTextContent('範例成員');
    expect(within(dialog).getByRole('alert')).toHaveTextContent('名單不能超過 100 人。');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('讀名單失敗：說明原因並可以關閉', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/2');
    server.use(http.get('/v1/knowledge/spaces/:key/pages/:number/restriction', () => failure(500, 'server.error', '伺服器出錯了。')));

    // Act
    await view.user.click(await screen.findByRole('button', { name: '限制…' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '限制「帳號申請」' });
    expect(await within(dialog).findByText('名單載入失敗')).toBeInTheDocument();
    await view.user.click(within(dialog).getByRole('button', { name: '關閉' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('名單滿了：選人的下拉停用並說明上限', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/1', { trees: { DOCS: [pageNodeOf({ number: 1, title: '人事', isRestricted: true })] } });
    view.backend.restrictions['DOCS/1'] = [ownerPerson.personId, ...Array.from({ length: 99 }, (_, index) => `gone-${String(index)}`)];

    // Act
    await view.user.click(await screen.findByRole('button', { name: '限制…' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '限制「人事」' });
    expect(await within(dialog).findByText('100 / 100 人')).toBeInTheDocument();
    expect(within(dialog).getByRole('combobox', { name: '加入人員' })).toBeDisabled();
    expect(within(dialog).getByText('名單最多 100 人。')).toBeInTheDocument();
  });

  it('組織空間：選人的清單不列訪客（訪客看不到組織空間）', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/2');
    server.use(http.get('/v1/people', () => ok([ownerPerson, memberPerson, guestPerson])));
    const dialog = await openFromPage(view);
    await view.user.click(await within(dialog).findByRole('button', { name: '限制這一頁' }));

    // Act
    const options = await openOptions(view.user, await within(dialog).findByRole('combobox', { name: '加入人員' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual([expect.stringMatching(/範例成員（member@example.com）$/)]);
  });

  it('名單有 100 人、自己不在名單上（組織管理員）：送出會補上自己變成 101 人，所以下拉也是停用的', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/1', { trees: { DOCS: [pageNodeOf({ number: 1, title: '人事', isRestricted: true })] } });
    view.backend.restrictions['DOCS/1'] = Array.from({ length: 100 }, (_, index) => `gone-${String(index)}`);

    // Act
    await view.user.click(await screen.findByRole('button', { name: '限制…' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '限制「人事」' });
    await within(dialog).findByText('100 / 100 人');
    expect(within(dialog).getByRole('combobox', { name: '加入人員' })).toBeDisabled();
    expect(within(dialog).getByText('名單最多 100 人。')).toBeInTheDocument();
  });

  it('儲存中按 Esc：對話框不會關掉', async () => {
    // Arrange
    const view = openPage('/knowledge/DOCS/2');
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.put('/v1/knowledge/spaces/:key/pages/:number/restriction', async () => {
        await gate;
        return ok({ people: [], restrictedAncestors: [] });
      }),
    );
    const dialog = await openFromPage(view);
    await view.user.click(await within(dialog).findByRole('button', { name: '限制這一頁' }));
    await chooseOption(view.user, await within(dialog).findByRole('combobox', { name: '加入人員' }), '範例成員（member@example.com）');
    await view.user.click(within(dialog).getByRole('button', { name: '儲存' }));
    await within(dialog).findByRole('button', { name: '儲存中…' });

    // Act
    await view.user.keyboard('{Escape}');

    // Assert
    expect(screen.getByRole('dialog', { name: '限制「帳號申請」' })).toBeInTheDocument();
    release();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });
});
