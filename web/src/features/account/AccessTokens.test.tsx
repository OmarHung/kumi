import { screen, waitFor, within } from '@testing-library/react';
import { delay, http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { serveAccessTokens, tokenOf } from '../../test/account';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const LAPTOP = tokenOf({ tokenId: '11111111-1111-4111-8111-111111111111', name: '我的筆電', displayPrefix: 'pmt_Ab12' });
const CI = tokenOf({
  tokenId: '22222222-2222-4222-8222-222222222222',
  name: 'CI 唯讀',
  displayPrefix: 'pmt_Cd34',
  scopes: ['items:read', 'blueprint:read'],
  createdAt: '2026-10-01T02:00:00Z',
  // 遠未來：測試不隨日期變成「已過期」。
  expiresAt: '2099-12-31T02:00:00Z',
});
const OLD = tokenOf({
  tokenId: '33333333-3333-4333-8333-333333333333',
  name: '舊的',
  displayPrefix: 'pmt_Ef56',
  scopes: ['items:write'],
  createdAt: '2020-01-01T02:00:00Z',
  lastUsedAt: '2020-01-02T02:00:00Z',
  expiresAt: '2020-06-01T02:00:00Z',
});

function storageText(storage: Storage): string {
  const entries = Array.from({ length: storage.length }, (_, index) => storage.key(index) ?? '').map((key) => [key, storage.getItem(key)]);
  return JSON.stringify(entries);
}

async function openTokens(initial = [LAPTOP, CI, OLD]) {
  signedInAs(exampleOwner);
  const backend = serveAccessTokens(initial);
  const view = renderApp('/account/integrations');
  const section = await screen.findByRole('region', { name: '存取權杖' });
  return { ...view, backend, section };
}

async function openCreate(initial?: Parameters<typeof openTokens>[0]) {
  const opened = await openTokens(initial);
  const button = within(opened.section).getByRole('button', { name: '建立存取權杖' });
  await opened.user.click(button);
  const dialog = await screen.findByRole('dialog', { name: '建立存取權杖' });
  return { ...opened, button, dialog };
}

/** 預設值照建立；回傳明文所在的對話框。 */
async function createWithName(opened: Awaited<ReturnType<typeof openCreate>>, name: string) {
  await opened.user.type(within(opened.dialog).getByLabelText('名稱'), name);
  await opened.user.click(within(opened.dialog).getByRole('button', { name: '建立' }));
  return screen.findByRole('dialog', { name: '存取權杖已建立' });
}

describe('我的帳號：存取權杖的清單', () => {
  it('沒有權杖時是一句說明加「建立存取權杖」', async () => {
    // Act
    const { section } = await openTokens([]);

    // Assert
    expect(await within(section).findByText('還沒有存取權杖。')).toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '建立存取權杖' })).toBeInTheDocument();
    expect(within(section).queryByRole('table')).not.toBeInTheDocument();
  });

  it('寬窄螢幕都是清單：名稱，第二行開頭、範圍、建立、最後使用、到期；過期的標「已過期」', async () => {
    // Arrange
    const { section } = await openTokens();

    // Act
    const list = await within(section).findByRole('list', { name: '存取權杖' });
    const rows = within(list).getAllByRole('listitem');

    // Assert
    expect(within(section).queryByRole('table')).not.toBeInTheDocument();
    expect(rows[0]).toHaveTextContent('我的筆電');
    expect(rows[0]).toHaveTextContent('開頭：pmt_Ab12…');
    expect(rows[0]).toHaveTextContent('範圍：全部');
    expect(rows[0]).toHaveTextContent('2026/11/01 建立');
    expect(rows[0]).toHaveTextContent('還沒用過');
    expect(rows[0]).toHaveTextContent('不過期');
    expect(within(rows[0] as HTMLElement).queryByText('已過期')).not.toBeInTheDocument();
    expect(rows[1]).toHaveTextContent('範圍：讀取項目、讀取設定');
    expect(rows[1]).toHaveTextContent('2099/12/31 到期');
    expect(rows[2]).toHaveTextContent('範圍：修改項目');
    expect(rows[2]).toHaveTextContent('2020/06/01 到期 已過期');
    expect(within(rows[0] as HTMLElement).getByRole('button', { name: '撤銷「我的筆電」（pmt_Ab12…）' })).toBeInTheDocument();
  });

  it('讀不到清單時說明原因，可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/account/tokens', () => failure(500, 'server.error', 'boom')));
    const { user } = renderApp('/account/integrations');
    const section = await screen.findByRole('region', { name: '存取權杖' });
    serveAccessTokens([LAPTOP]);

    // Act
    await user.click(await within(section).findByRole('button', { name: '重試' }));

    // Assert
    expect(await within(section).findByRole('list', { name: '存取權杖' })).toBeInTheDocument();
  });
});

describe('我的帳號：存取權杖的清單（窄螢幕也一樣）', () => {
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

  it('不是表格，是一筆兩行的清單，「撤銷」在最後面', async () => {
    // Arrange
    const { section } = await openTokens([CI]);

    // Act
    const list = await within(section).findByRole('list', { name: '存取權杖' });

    // Assert
    expect(within(section).queryByRole('table')).not.toBeInTheDocument();
    const [row] = within(list).getAllByRole('listitem');
    expect(row).toHaveTextContent('CI 唯讀');
    expect(row).toHaveTextContent('開頭：pmt_Cd34…');
    expect(row).toHaveTextContent('範圍：讀取項目、讀取設定');
    expect(row).toHaveTextContent('2099/12/31 到期');
    expect(within(row as HTMLElement).getByRole('button', { name: '撤銷「CI 唯讀」（pmt_Cd34…）' })).toBeInTheDocument();
  });
});

describe('我的帳號：撤銷存取權杖', () => {
  it('確認之後撤銷，那一列不在了，焦點到「建立存取權杖」', async () => {
    // Arrange
    const { user, section, backend } = await openTokens();
    await user.click(await within(section).findByRole('button', { name: '撤銷「我的筆電」（pmt_Ab12…）' }));
    const dialog = await screen.findByRole('dialog', { name: '撤銷「我的筆電」？' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '撤銷權杖' }));

    // Assert
    await waitFor(() => { expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); });
    expect(backend.revokes).toEqual([LAPTOP.tokenId]);
    await waitFor(() => { expect(within(section).queryByText('我的筆電')).not.toBeInTheDocument(); });
    expect(within(section).getByText('CI 唯讀')).toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '建立存取權杖' })).toHaveFocus();
  });

  it('Esc 關上、不送出，焦點回到那一列的「撤銷」', async () => {
    // Arrange
    const { user, section, backend } = await openTokens();
    const button = await within(section).findByRole('button', { name: '撤銷「我的筆電」（pmt_Ab12…）' });
    await user.click(button);
    await screen.findByRole('dialog', { name: '撤銷「我的筆電」？' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => { expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); });
    expect(button).toHaveFocus();
    await flushPending();
    expect(backend.revokes).toEqual([]);
  });

  it('送出中確認鈕標 aria-disabled、焦點留著，Esc 與再按都不作用；結果回來後才關', async () => {
    // Arrange
    const { user, section } = await openTokens();
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => { release = resolve; });
    let requests = 0;
    server.use(http.delete('/v1/account/tokens/:tokenId', async () => {
      requests += 1;
      await gate;
      return ok(LAPTOP);
    }));
    await user.click(await within(section).findByRole('button', { name: '撤銷「我的筆電」（pmt_Ab12…）' }));
    const dialog = await screen.findByRole('dialog', { name: '撤銷「我的筆電」？' });
    await user.click(within(dialog).getByRole('button', { name: '撤銷權杖' }));

    // Act
    const pending = await within(dialog).findByRole('button', { name: '撤銷中…' });
    await user.keyboard('{Escape}');
    await user.click(pending);

    // Assert
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    await flushPending();
    expect(requests).toBe(1);
    release();
    await waitFor(() => { expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); });
    // 蓋掉的 handler 不經過 backend；結果回來之後那一列要不在了。
    await waitFor(() => { expect(within(section).queryByText('我的筆電')).not.toBeInTheDocument(); });
  });

  it('伺服器回 404（別的分頁先撤了）當成功：對話框關上、清單重抓、提示已經撤銷了', async () => {
    // Arrange
    const { user, section } = await openTokens();
    server.use(http.delete('/v1/account/tokens/:tokenId', () => failure(404, 'access_token.not_found', '找不到這把存取權杖。')));
    await user.click(await within(section).findByRole('button', { name: '撤銷「我的筆電」（pmt_Ab12…）' }));
    const dialog = await screen.findByRole('dialog', { name: '撤銷「我的筆電」？' });
    server.use(http.get('/v1/account/tokens', () => ok([CI, OLD])));

    // Act
    await user.click(within(dialog).getByRole('button', { name: '撤銷權杖' }));

    // Assert
    await waitFor(() => { expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); });
    expect(await within(section).findByText('這把權杖已經撤銷了。')).toBeInTheDocument();
    await waitFor(() => { expect(within(section).queryByText('我的筆電')).not.toBeInTheDocument(); });
    expect(within(section).getByRole('button', { name: '建立存取權杖' })).toHaveFocus();
  });

  it('其他失敗（伺服器錯誤）時對話框留著，說明寫在按鈕上方，Esc 還能關', async () => {
    // Arrange
    const { user, section } = await openTokens();
    server.use(http.delete('/v1/account/tokens/:tokenId', () => failure(500, 'server.error', '伺服器出了問題。')));
    const button = await within(section).findByRole('button', { name: '撤銷「我的筆電」（pmt_Ab12…）' });
    await user.click(button);
    const dialog = await screen.findByRole('dialog', { name: '撤銷「我的筆電」？' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '撤銷權杖' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('伺服器暫時無法處理，請稍後再試。');
    expect(within(dialog).getByRole('button', { name: '撤銷權杖' })).toHaveFocus();
    await user.keyboard('{Escape}');
    await waitFor(() => { expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); });
    expect(button).toHaveFocus();
  });

  it('同名的兩把靠開頭分：按鈕的名稱與確認的說明都帶開頭', async () => {
    // Arrange
    const twin = tokenOf({ tokenId: '44444444-4444-4444-8444-444444444444', name: '我的筆電', displayPrefix: 'pmt_Zz99' });
    const { user, section } = await openTokens([LAPTOP, twin]);

    // Act
    await user.click(await within(section).findByRole('button', { name: '撤銷「我的筆電」（pmt_Zz99…）' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '撤銷「我的筆電」？' });
    expect(dialog).toHaveTextContent('開頭 pmt_Zz99…');
    expect(within(section).getAllByRole('button', { name: /^撤銷「我的筆電」/ })).toHaveLength(2);
  });
});

describe('我的帳號：建立存取權杖', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('預設是名稱空白、前四個範圍勾著（本機代理不勾）、期限 90 天；焦點在名稱上', async () => {
    // Act
    const { dialog } = await openCreate();

    // Assert
    const name = within(dialog).getByLabelText('名稱');
    expect(name).toHaveFocus();
    const group = within(dialog).getByRole('group', { name: '範圍' });
    const boxes = within(group).getAllByRole('checkbox');
    expect(boxes.map((box) => (box as HTMLInputElement).checked)).toEqual([true, true, true, true, false]);
    expect(boxes.map((box) => box.getAttribute('aria-describedby') !== null)).toEqual([true, true, true, true, true]);
    expect(within(dialog).getByRole('combobox', { name: '期限' })).toHaveTextContent('90 天');
  });

  it('範圍多一個「本機代理」，說明在旁邊，預設不勾；勾了就送 agent:run', async () => {
    // Arrange
    const opened = await openCreate([]);
    const { user, dialog, backend } = opened;
    const checkbox = within(dialog).getByRole('checkbox', { name: /本機代理/ });
    expect(checkbox).not.toBeChecked();
    expect(checkbox).toHaveAccessibleDescription('讓這台電腦的 Claude Code／Codex 接網頁的 AI 助理。');

    // Act
    await user.click(checkbox);
    await createWithName(opened, '本機代理');

    // Assert
    expect(backend.creates).toEqual([
      { name: '本機代理', scopes: ['items:read', 'items:write', 'blueprint:read', 'blueprint:write', 'agent:run'], expiresInDays: 90 },
    ]);
  });

  it('清單上的範圍：本機代理用的權杖列出名稱；全部的後面接「本機代理」', async () => {
    // Arrange / Act
    const { section } = await openTokens([
      tokenOf({ tokenId: '44444444-4444-4444-8444-444444444444', name: '代理', scopes: ['agent:run', 'blueprint:read', 'blueprint:write'] }),
      tokenOf({ tokenId: '55555555-5555-4555-8555-555555555555', name: '全包', scopes: ['items:read', 'items:write', 'blueprint:read', 'blueprint:write', 'agent:run'] }),
    ]);

    // Assert
    const rows = await within(section).findAllByRole('listitem');
    expect(rows[0]).toHaveTextContent('範圍：讀取設定、修改設定、本機代理');
    expect(rows[1]).toHaveTextContent('範圍：全部、本機代理');
  });

  it('送出名稱、範圍與期限；成功後換成「只會顯示這一次」的畫面，明文在唯讀的欄位裡', async () => {
    // Arrange
    const opened = await openCreate([]);
    const { user, dialog, backend } = opened;
    await user.click(within(dialog).getByRole('checkbox', { name: /修改設定/ }));
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '期限' }), '不過期');

    // Act
    const created = await createWithName(opened, '  我的筆電的 CLI ');

    // Assert
    expect(backend.creates).toEqual([
      { name: '我的筆電的 CLI', scopes: ['items:read', 'items:write', 'blueprint:read'], expiresInDays: null },
    ]);
    const secret = backend.lastSecret() ?? '';
    const field = within(created).getByLabelText('存取權杖');
    expect(field).toHaveValue(secret);
    expect(field).toHaveAttribute('readonly');
    expect(field).toHaveFocus();
    // 焦點直接落在欄位上：警告要連在欄位的說明裡，報讀軟體才聽得到。
    expect(field).toHaveAccessibleDescription(/只會顯示這一次/);
    expect(created).toHaveTextContent('只會顯示這一次');
    expect(created).toHaveTextContent('kumi auth login');
    expect(screen.queryByRole('dialog', { name: '建立存取權杖' })).not.toBeInTheDocument();
  });

  it('「複製」把明文放進剪貼簿並說明；剪貼簿不給時說明可以手動選取', async () => {
    // Arrange
    const opened = await openCreate([]);
    const created = await createWithName(opened, '筆電');
    const secret = opened.backend.lastSecret() ?? '';
    const copy = within(created).getByRole('button', { name: '複製' });

    // Act
    await opened.user.click(copy);

    // Assert
    expect(await within(created).findByText('已複製。')).toBeInTheDocument();
    expect(await navigator.clipboard.readText()).toBe(secret);

    // Act：之後剪貼簿不給了
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    await opened.user.click(copy);

    // Assert
    expect(await within(created).findByText('無法自動複製，請選取上面的權杖手動複製。')).toBeInTheDocument();
    expect(within(created).getByLabelText('存取權杖')).toHaveFocus();
  });

  it('「完成」關上之後畫面上找不到明文，也不在查詢快取、mutation 快取、localStorage、sessionStorage 裡；焦點回到「建立存取權杖」', async () => {
    // Arrange
    const opened = await openCreate([]);
    const created = await createWithName(opened, '筆電');
    const secret = opened.backend.lastSecret() ?? '';
    expect(secret).not.toBe('');

    // Act
    await opened.user.click(within(created).getByRole('button', { name: '完成' }));

    // Assert
    await waitFor(() => { expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); });
    expect(document.body).not.toHaveTextContent(secret);
    expect(screen.queryByDisplayValue(secret)).not.toBeInTheDocument();
    const caches = JSON.stringify([
      opened.queryClient.getQueryCache().getAll().map((query) => query.state.data),
      opened.queryClient.getMutationCache().getAll().map((mutation) => [mutation.state.data, mutation.state.variables, mutation.state.error]),
    ]);
    expect(caches).not.toContain(secret);
    expect(storageText(localStorage)).not.toContain(secret);
    expect(storageText(sessionStorage)).not.toContain(secret);
    expect(opened.button).toHaveFocus();
    expect(await within(opened.section).findByText('筆電')).toBeInTheDocument();
  });

  it('在「只會顯示這一次」的畫面按 Esc 也關上，焦點回到「建立存取權杖」', async () => {
    // Arrange
    const opened = await openCreate([]);
    await createWithName(opened, '筆電');

    // Act
    await opened.user.keyboard('{Escape}');

    // Assert
    await waitFor(() => { expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); });
    expect(opened.button).toHaveFocus();
  });

  it('名稱空白、一個範圍都沒選時標在欄位上、不送出，焦點到第一個有問題的欄位', async () => {
    // Arrange
    const { user, dialog, backend } = await openCreate();
    for (const box of within(dialog).getAllByRole('checkbox').filter((candidate) => (candidate as HTMLInputElement).checked)) await user.click(box);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(within(dialog).getByLabelText('名稱')).toHaveAccessibleDescription(/請輸入名稱。/);
    expect(within(dialog).getByRole('group', { name: '範圍' })).toHaveAccessibleDescription(/至少選一個範圍。/);
    expect(within(dialog).getByLabelText('名稱')).toHaveFocus();
    await flushPending();
    expect(backend.creates).toEqual([]);
  });

  it('伺服器的 422 依細項路徑標在欄位上，對話框留著', async () => {
    // Arrange
    const { user, dialog } = await openCreate();
    const issue = { code: 'access_token.name_too_long', path: 'name', message: '名稱不能超過 100 個字元。' };
    server.use(http.post('/v1/account/tokens', () => failure(422, issue.code, issue.message, [issue])));
    await user.type(within(dialog).getByLabelText('名稱'), 'x');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    await waitFor(() => { expect(within(dialog).getByLabelText('名稱')).toHaveAccessibleDescription(/名稱不能超過 100 個字元。/); });
    expect(screen.getByRole('dialog', { name: '建立存取權杖' })).toBeInTheDocument();
  });

  it('名稱有控制字元時，伺服器的 access_token.name_required 標在名稱欄位上', async () => {
    // Arrange
    const { user, dialog } = await openCreate();
    const issue = { code: 'access_token.name_required', path: 'name', message: '請輸入權杖的名稱。' };
    server.use(http.post('/v1/account/tokens', () => failure(422, issue.code, issue.message, [issue])));
    await user.type(within(dialog).getByLabelText('名稱'), 'a\u200Bb');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    await waitFor(() => { expect(within(dialog).getByLabelText('名稱')).toHaveAccessibleDescription(/請輸入權杖的名稱。/); });
    expect(within(dialog).getByLabelText('名稱')).toBeInvalid();
    expect(screen.getByRole('dialog', { name: '建立存取權杖' })).toBeInTheDocument();
  });

  it('已達上限（409）時說明寫在表單上方，對話框留著，焦點還在送出鈕', async () => {
    // Arrange
    const many = Array.from({ length: 20 }, (_, index) => tokenOf({ tokenId: `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`, name: `權杖 ${String(index)}` }));
    const { user, dialog } = await openCreate(many);
    await user.type(within(dialog).getByLabelText('名稱'), '第 21 把');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('最多只能同時有 20 把');
    expect(screen.getByRole('dialog', { name: '建立存取權杖' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: '建立' })).toHaveFocus();
  });

  it('Esc 關上、不送出，焦點回到「建立存取權杖」', async () => {
    // Arrange
    const { user, button, backend } = await openCreate();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => { expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); });
    expect(button).toHaveFocus();
    await flushPending();
    expect(backend.creates).toEqual([]);
  });

  it('送出中送出鈕標 aria-disabled、焦點留著，Esc 與再按都不作用，只送出一次', async () => {
    // Arrange
    const { user, dialog } = await openCreate();
    let requests = 0;
    server.use(http.post('/v1/account/tokens', async () => {
      requests += 1;
      await delay('infinite');
      return ok({});
    }));
    await user.type(within(dialog).getByLabelText('名稱'), '筆電');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Act
    const pending = await within(dialog).findByRole('button', { name: '建立中…' });
    await user.keyboard('{Escape}');
    await user.click(pending);

    // Assert
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    expect(screen.getByRole('dialog', { name: '建立存取權杖' })).toBeInTheDocument();
    await flushPending();
    expect(requests).toBe(1);
  });
});
