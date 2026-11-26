import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { AiSettings } from '../../api/aiTypes';
import { failure, signedInAs } from '../../test/api';
import { exampleConnection, forbidAiSettings, serveAiSettings } from '../../test/aiSettings';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const SAVED: Partial<AiSettings> = {
  enabled: true,
  default: { connectionId: 'c-1', model: 'claude-opus-5-5', effort: 'high' },
  connections: [exampleConnection()],
};

async function openPage(initial: Partial<AiSettings> = {}) {
  signedInAs(exampleOwner);
  const backend = serveAiSettings(initial);
  const view = renderApp('/ai-settings');
  const save = await screen.findByRole('button', { name: '儲存' });
  return { ...view, backend, save };
}

/** 連線列上的「更多」選單裡選一個動作（重新讀取、刪除）。 */
async function chooseConnectionAction(user: ReturnType<typeof renderApp>['user'], connectionName: string, action: string) {
  await user.click(screen.getByRole('button', { name: `「${connectionName}」的更多動作` }));
  await user.click(await screen.findByRole('menuitem', { name: action }));
}

async function openNewDialog(initial: Partial<AiSettings> = {}) {
  const page = await openPage(initial);
  await page.user.click(screen.getByRole('button', { name: '新增 API 金鑰' }));
  const dialog = await screen.findByRole('dialog', { name: '新增 API 金鑰' });
  return { ...page, dialog };
}

async function openEditDialog(initial: Partial<AiSettings> = SAVED) {
  const page = await openPage(initial);
  await page.user.click(screen.getByRole('button', { name: '編輯 Anthropic' }));
  const dialog = await screen.findByRole('dialog', { name: '編輯 API 金鑰' });
  return { ...page, dialog };
}

describe('AI 設定：入口與權限', () => {
  it('組織管理員從側欄的「設定」進去，次導覽有「AI 設定」，按了到設定頁', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveAiSettings();
    const { user, router } = renderApp('/my-work');

    // Act
    await user.click(await screen.findByRole('link', { name: '設定' }));
    await user.click(await screen.findByRole('link', { name: 'AI 設定' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: 'AI 設定' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/ai-settings');
  });

  it('一般成員的側欄沒有「AI 設定」；直接打網址說沒有權限', async () => {
    // Arrange
    signedInAs(exampleMember);
    forbidAiSettings();

    // Act
    renderApp('/ai-settings');

    // Assert
    expect(await screen.findByText('只有組織的擁有者與管理員可以設定 AI。')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'AI 設定' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '儲存' })).not.toBeInTheDocument();
  });

  it('管理員被後端回 403：顯示沒有權限，不是載入失敗', async () => {
    // Arrange
    signedInAs(exampleOwner);
    forbidAiSettings();

    // Act
    renderApp('/ai-settings');

    // Assert
    expect(await screen.findByText('只有組織的擁有者與管理員可以設定 AI。')).toBeInTheDocument();
    expect(screen.queryByText('AI 設定載入失敗')).not.toBeInTheDocument();
  });
});

describe('AI 設定：載入', () => {
  it('第一次開：沒有連線，預設整段停用並說明怎麼辦', async () => {
    // Arrange / Act
    await openPage();

    // Assert
    expect(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ })).not.toBeChecked();
    expect(screen.getByRole('combobox', { name: '模型' })).toBeDisabled();
    expect(screen.getByRole('combobox', { name: '思考深度' })).toBeDisabled();
    expect(screen.getByText('先在上面新增 API 金鑰、開放至少一個模型')).toBeInTheDocument();
    expect(screen.getByText(/還沒有 API 金鑰/)).toBeInTheDocument();
  });

  it('存著的設定：勾選、預設、連線的金鑰、開放幾個模型與本月的用量', async () => {
    // Arrange / Act
    await openPage(SAVED);

    // Assert
    expect(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ })).toBeChecked();
    expect(screen.getByRole('combobox', { name: '模型' })).toHaveTextContent('Claude Opus 5.5');
    expect(screen.getByRole('combobox', { name: '思考深度' })).toHaveTextContent('高');
    expect(screen.getByText('金鑰 …a1b2 · 開放 1 個模型')).toBeInTheDocument();
    expect(screen.getByText('本月 12 則回覆 · 輸入 120,000 · 輸出 8,000 token')).toBeInTheDocument();
  });

  it('沒有金鑰的連線寫「沒有金鑰」', async () => {
    // Arrange / Act
    await openPage({ connections: [exampleConnection({ hasApiKey: false, apiKeyHint: null, kind: 'openai_compatible', name: 'Ollama' })] });

    // Assert
    expect(screen.getByText('沒有金鑰 · 開放 1 個模型')).toBeInTheDocument();
    expect(screen.getByText('OpenAI 相容')).toBeInTheDocument();
  });

  it('背景重抓失敗：表單與改到一半的內容留著，上面說明畫面可能是舊的', async () => {
    // Arrange
    const { user, queryClient } = await openPage(SAVED);
    await user.click(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ }));
    server.use(http.get('/v1/ai/settings', () => failure(500, 'server.error', '伺服器暫時無法處理。')));

    // Act
    await queryClient.invalidateQueries({ queryKey: ['ai'] });

    // Assert
    expect(await screen.findByText('無法更新 AI 設定，畫面上可能是舊的。')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ })).not.toBeChecked();
    expect(screen.queryByText('AI 設定載入失敗')).not.toBeInTheDocument();
  });
});

describe('AI 設定：用量上限', () => {
  const NO_CAPS = { dailyRequestsPerPerson: 200, monthlyRequests: null, monthlyTokens: null } as const;

  it('顯示三項上限（預設每人每天 200、其餘不限）、這個月的用量與「只算組織的 API 金鑰」的說明', async () => {
    // Arrange / Act
    await openPage({ limits: { dailyRequestsPerPerson: 200, monthlyRequests: 1000, monthlyTokens: null }, monthUsage: { month: '2026-11', requests: 120, tokens: 4500000 } });

    // Assert
    expect(screen.getByRole('textbox', { name: '每人每天最多（則）' })).toHaveValue('200');
    expect(screen.getByRole('textbox', { name: '全組織每月最多（則）' })).toHaveValue('1000');
    expect(screen.getByRole('textbox', { name: '全組織每月 token 上限' })).toHaveValue('');
    expect(screen.getByText('這個月已用 120 則，上限 1,000 則')).toBeInTheDocument();
    expect(screen.getByText(/這個月已用 4,500,000 token（不限）/)).toBeInTheDocument();
    expect(screen.getByText(/只算組織的 API 金鑰。成員自己的 API 金鑰與本機代理不算，主機 AI 代理在各來源自己設上限/)).toBeInTheDocument();
  });

  it('改上限連同其他設定一起存；留白送 null（不限）', async () => {
    // Arrange
    const { user, backend, save } = await openPage();
    const daily = screen.getByRole('textbox', { name: '每人每天最多（則）' });

    // Act
    await user.clear(daily);
    await user.type(screen.getByRole('textbox', { name: '全組織每月最多（則）' }), '5000');
    await user.type(screen.getByRole('textbox', { name: '全組織每月 token 上限' }), '20000000');
    await user.click(save);

    // Assert
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();
    expect(backend.savedSettings[0]?.limits).toEqual({ dailyRequestsPerPerson: null, monthlyRequests: 5000, monthlyTokens: 20000000 });
  });

  it.each(['0', '-3', '1.5', 'abc', '1000001'])('每月則數填 %s：不送出，說明範圍並把焦點放過去', async (value) => {
    // Arrange
    const { user, backend, save } = await openPage();
    const field = screen.getByRole('textbox', { name: '全組織每月最多（則）' });
    await user.type(field, value);

    // Act
    await user.click(save);

    // Assert
    expect(await screen.findByText('填 1 到 1,000,000 的整數，留白是不限')).toBeInTheDocument();
    await waitFor(() => { expect(field).toHaveFocus(); });
    expect(backend.savedSettings).toEqual([]);
  });

  it('token 超過 1000 億：說明範圍', async () => {
    // Arrange
    const { user, save } = await openPage();
    await user.type(screen.getByRole('textbox', { name: '全組織每月 token 上限' }), '100000000001');

    // Act
    await user.click(save);

    // Assert
    expect(await screen.findByText('填 1 到 100,000,000,000 的整數，留白是不限')).toBeInTheDocument();
  });

  it('伺服器的 422 細項（路徑 limits.xxx）顯示在對應的欄位底下', async () => {
    // Arrange
    const { user, backend, save } = await openPage();
    await user.type(screen.getByRole('textbox', { name: '全組織每月最多（則）' }), '10');
    backend.rejectNext('settings', {
      status: 422,
      code: 'ai.settings_invalid',
      message: '設定有問題。',
      details: [{ code: 'ai.field_invalid', path: 'limits.monthlyRequests', message: '每月的則數不合理。' }],
    });

    // Act
    await user.click(save);

    // Assert
    expect(await screen.findByText('每月的則數不合理。')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: '全組織每月最多（則）' })).toHaveAttribute('aria-invalid', 'true');
  });

  it('只改上限不存就離開：算有沒存的修改', async () => {
    // Arrange
    const { user } = await openPage({ limits: NO_CAPS });

    // Act
    await user.type(screen.getByRole('textbox', { name: '全組織每月最多（則）' }), '10');
    await user.click(within(screen.getByRole('navigation', { name: '設定' })).getByRole('link', { name: '主機 AI 代理' }));

    // Assert
    expect(await screen.findByRole('dialog', { name: '還有沒儲存的修改' })).toBeInTheDocument();
  });
});

describe('AI 設定：儲存預設', () => {
  it('選模型與思考深度，連同勾選一起存', async () => {
    // Arrange
    const { user, backend, save } = await openPage({ connections: [exampleConnection()] });
    await user.click(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ }));
    await chooseOption(user, screen.getByRole('combobox', { name: '模型' }), 'Claude Opus 5.5');

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '思考深度' }), '中');
    await user.click(save);

    // Assert
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();
    expect(backend.savedSettings).toEqual([{ enabled: true, allowPersonalConnections: false, default: { connectionId: 'c-1', model: 'claude-opus-5-5', effort: 'medium' } }]);
    expect(backend.savedSettings[0]).not.toHaveProperty('limits');
  });

  it('思考深度的選項是「預設」加這個模型能選的，由淺到深', async () => {
    // Arrange
    const { user } = await openPage(SAVED);

    // Act
    const options = await openOptions(user, screen.getByRole('combobox', { name: '思考深度' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['預設', '低', '中', '高']);
  });

  it('模型不能選思考深度：停用並說明；換成能選的模型再打開', async () => {
    // Arrange
    const noEfforts = exampleConnection({
      models: [
        { id: 'a', displayName: '模型甲', efforts: ['low'], enabled: true, custom: false },
        { id: 'b', displayName: '模型乙', efforts: [], enabled: true, custom: false },
      ],
    });
    const { user } = await openPage({ connections: [noEfforts], default: { connectionId: 'c-1', model: 'b', effort: null } });

    // Assert
    expect(screen.getByRole('combobox', { name: '思考深度' })).toBeDisabled();
    expect(screen.getByText('這個模型不能選思考深度')).toBeInTheDocument();

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '模型' }), '模型甲');

    // Assert
    expect(screen.getByRole('combobox', { name: '思考深度' })).toBeEnabled();
  });

  it('換成不能選原本思考深度的模型：回到「預設」', async () => {
    // Arrange
    const connection = exampleConnection({
      models: [
        { id: 'a', displayName: '模型甲', efforts: ['low', 'high'], enabled: true, custom: false },
        { id: 'b', displayName: '模型乙', efforts: ['low'], enabled: true, custom: false },
      ],
    });
    const { user, backend, save } = await openPage({ enabled: true, connections: [connection], default: { connectionId: 'c-1', model: 'a', effort: 'high' } });

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '模型' }), '模型乙');
    await user.click(save);

    // Assert
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();
    expect(backend.savedSettings[0]?.default).toEqual({ connectionId: 'c-1', model: 'b', effort: null });
  });

  it('有好幾個連線：選項是「連線名稱 · 模型名稱」', async () => {
    // Arrange
    const second = exampleConnection({ id: 'c-2', name: '備用帳號' });
    const { user } = await openPage({ connections: [exampleConnection(), second] });

    // Act
    const options = await openOptions(user, screen.getByRole('combobox', { name: '模型' }));

    // Assert
    expect(options.map((option) => option.textContent)).toEqual(['Anthropic · Claude Opus 5.5', '備用帳號 · Claude Opus 5.5']);
  });

  it('儲存失敗（422）：原因顯示在按鈕下面；429 說太頻繁', async () => {
    // Arrange
    const { user, backend, save } = await openPage(SAVED);
    await user.click(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ }));
    backend.rejectNext('settings', { status: 422, code: 'ai.settings_invalid', message: '預設的模型沒有開放。' });

    // Act
    await user.click(save);

    // Assert
    expect(await screen.findByText('預設的模型沒有開放。')).toBeInTheDocument();

    // Act
    backend.rejectNext('settings', { status: 429, code: 'request.rate_limited', message: '太快' });
    await user.click(save);

    // Assert
    expect(await screen.findByText('請求太頻繁，請稍後再試。')).toBeInTheDocument();
  });
});

describe('AI 設定：新增連線', () => {
  it('名稱預先填供應商的名稱，換供應商跟著換；自己改過就不再換', async () => {
    // Arrange
    const { user, dialog } = await openNewDialog();
    const name = within(dialog).getByLabelText('名稱');
    expect(name).toHaveValue('Anthropic');

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'OpenAI' }));

    // Assert
    expect(name).toHaveValue('OpenAI');

    // Act
    await user.clear(name);
    await user.type(name, '公司帳號');
    await user.click(within(dialog).getByRole('button', { name: 'Google Gemini' }));

    // Assert
    expect(name).toHaveValue('公司帳號');
  });

  it('基底網址只有 OpenAI 相容有；其他供應商有「到 … 取得金鑰」的連結', async () => {
    // Arrange
    const { user, dialog } = await openNewDialog();

    // Assert
    expect(within(dialog).queryByLabelText('基底網址')).not.toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: '到 Anthropic 取得金鑰' })).toHaveAttribute('href', 'https://platform.claude.com/settings/keys');

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'OpenAI 相容' }));

    // Assert
    expect(within(dialog).getByLabelText('基底網址')).toHaveAttribute('placeholder', 'http://localhost:11434/v1');
    expect(within(dialog).getByText('沒有就留白')).toBeInTheDocument();
    expect(within(dialog).queryByRole('link', { name: /取得金鑰/ })).not.toBeInTheDocument();
  });

  it('讀取模型：用畫面上的值讀，列出模型與能選的思考深度，勾選後存', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewDialog();
    await user.type(within(dialog).getByLabelText('API 金鑰'), 'sk-ant-1234');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '讀取模型' }));
    const sonnet = await within(dialog).findByRole('checkbox', { name: /Claude Sonnet 5\.5/ });
    await user.click(sonnet);
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(backend.discoveries).toEqual([{ kind: 'anthropic', baseUrl: null, apiKey: 'sk-ant-1234', connectionId: null }]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: '新增 API 金鑰' })).not.toBeInTheDocument();
    });
    expect(backend.savedConnections).toEqual([
      { id: null, input: { name: 'Anthropic', kind: 'anthropic', baseUrl: null, apiKey: 'sk-ant-1234', enabledModels: ['claude-sonnet-5-5'], customModels: [] } },
    ]);
    expect(await screen.findByText('金鑰 …1234 · 開放 1 個模型')).toBeInTheDocument();
  });

  it('模型名稱旁有小字的代號與能選的思考深度', async () => {
    // Arrange
    const { user, dialog } = await openNewDialog();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '讀取模型' }));

    // Assert
    expect(await within(dialog).findByText('claude-sonnet-5-5 · 思考深度 低／高')).toBeInTheDocument();
  });

  it('讀取模型失敗：原因用提示顯示在按鈕下面，不關掉對話框', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewDialog();
    backend.rejectNext('discover', { status: 422, code: 'ai.provider_failed', message: '金鑰不對，供應商拒絕了這個要求。' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '讀取模型' }));

    // Assert
    expect(await within(dialog).findByText('金鑰不對，供應商拒絕了這個要求。')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '新增 API 金鑰' })).toBeInTheDocument();
  });

  it('超過 12 個模型才有篩選框，篩選只留下符合的', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewDialog();
    backend.setDiscovered(Array.from({ length: 13 }, (_, index) => ({ id: `m-${String(index)}`, displayName: `模型 ${String(index)}`, efforts: [] })));
    expect(within(dialog).queryByLabelText('篩選模型')).not.toBeInTheDocument();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '讀取模型' }));
    await user.type(await within(dialog).findByLabelText('篩選模型'), 'm-12');

    // Assert
    expect(within(dialog).getAllByRole('checkbox')).toHaveLength(1);
    expect(within(dialog).getByRole('checkbox', { name: /模型 12/ })).toBeInTheDocument();
  });

  it('手動加模型：列在清單裡、開放、可以移除；空的說明要輸入', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewDialog();
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    expect(await within(dialog).findByText('請輸入模型代號。')).toBeInTheDocument();

    // Act
    await user.type(within(dialog).getByLabelText('手動加模型'), 'my-model-1{Enter}');

    // Assert
    expect(within(dialog).getByRole('checkbox', { name: /my-model-1/ })).toBeChecked();
    expect(within(dialog).queryByText('請輸入模型代號。')).not.toBeInTheDocument();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '移除 my-model-1' }));

    // Assert
    expect(within(dialog).queryByRole('checkbox', { name: /my-model-1/ })).not.toBeInTheDocument();

    // Act
    await user.type(within(dialog).getByLabelText('手動加模型'), 'keep-me');
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(backend.savedConnections).toHaveLength(1);
    });
    expect(backend.savedConnections[0]?.input).toMatchObject({ enabledModels: ['keep-me'], customModels: ['keep-me'] });
  });

  it('422 的細項顯示在對應的欄位下面，焦點到第一個有問題的欄位', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewDialog();
    backend.rejectNext('connection', {
      status: 422,
      code: 'ai.settings_invalid',
      message: '連線的設定不正確。',
      details: [
        { code: 'ai.field_required', path: 'name', message: '請輸入名稱。' },
        { code: 'ai.field_required', path: 'apiKey', message: '請輸入 API 金鑰。' },
      ],
    });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    const name = within(dialog).getByLabelText('名稱');
    expect(await within(dialog).findByText('請輸入名稱。')).toBeInTheDocument();
    expect(name).toHaveAccessibleDescription(expect.stringContaining('請輸入名稱。'));
    expect(within(dialog).getByLabelText('API 金鑰')).toHaveAccessibleDescription(expect.stringContaining('請輸入 API 金鑰。'));
    await waitFor(() => {
      expect(name).toHaveFocus();
    });
  });

  it('基底網址的細項（相容端點）放在基底網址下面；ai.provider_failed 用提示顯示在按鈕附近', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewDialog();
    await user.click(within(dialog).getByRole('button', { name: 'OpenAI 相容' }));
    backend.rejectNext('connection', {
      status: 422,
      code: 'ai.settings_invalid',
      message: '網址不正確。',
      details: [{ code: 'ai.endpoint_private', path: 'baseUrl', message: '這個網址指向內網，不能使用。' }],
    });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByText('這個網址指向內網，不能使用。')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('基底網址')).toHaveAccessibleDescription(expect.stringContaining('這個網址指向內網'));

    // Act
    backend.rejectNext('connection', { status: 422, code: 'ai.provider_failed', message: '連不上 localhost:11434。' });
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByText('連不上 localhost:11434。')).toBeInTheDocument();
    expect(within(dialog).queryByText('這個網址指向內網，不能使用。')).not.toBeInTheDocument();
  });

  it('429：說太頻繁', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewDialog();
    backend.rejectNext('connection', { status: 429, code: 'request.rate_limited', message: '太快' });

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByText('請求太頻繁，請稍後再試。')).toBeInTheDocument();
  });
});

describe('AI 設定：編輯連線', () => {
  it('金鑰欄是空的，提示已經存了；留白存就是沿用（apiKey null）', async () => {
    // Arrange
    const { user, dialog, backend } = await openEditDialog();
    const key = within(dialog).getByLabelText('API 金鑰');
    expect(key).toHaveValue('');
    expect(key).toHaveAccessibleDescription(expect.stringContaining('已經存了（…a1b2），不改就留白'));
    expect(within(dialog).getByRole('checkbox', { name: /Claude Opus 5\.5/ })).toBeChecked();
    expect(within(dialog).getByRole('checkbox', { name: /Claude Haiku 5/ })).not.toBeChecked();

    // Act
    await user.click(within(dialog).getByRole('checkbox', { name: /Claude Haiku 5/ }));
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: '編輯 API 金鑰' })).not.toBeInTheDocument();
    });
    expect(backend.savedConnections).toEqual([
      { id: 'c-1', input: { name: 'Anthropic', kind: 'anthropic', baseUrl: null, apiKey: null, enabledModels: ['claude-opus-5-5', 'claude-haiku-5'], customModels: [] } },
    ]);
    expect(await screen.findByText('金鑰 …a1b2 · 開放 2 個模型')).toBeInTheDocument();
  });

  it('換了供應商而金鑰還是空的：欄位下面說要重新輸入；輸入之後提示消失', async () => {
    // Arrange
    const { user, dialog } = await openEditDialog();

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'OpenAI' }));

    // Assert
    expect(within(dialog).getByLabelText('API 金鑰')).toHaveAccessibleDescription(expect.stringContaining('換了供應商或網址，要重新輸入金鑰'));

    // Act
    await user.type(within(dialog).getByLabelText('API 金鑰'), 'sk-new');

    // Assert
    expect(within(dialog).getByLabelText('API 金鑰')).not.toHaveAccessibleDescription(expect.stringContaining('換了供應商或網址'));
  });

  it('編輯時讀取模型帶連線編號（金鑰留白就用存的）', async () => {
    // Arrange
    const { user, dialog, backend } = await openEditDialog();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '讀取模型' }));

    // Assert
    expect(await within(dialog).findByRole('checkbox', { name: /Claude Sonnet 5\.5/ })).not.toBeChecked();
    expect(backend.discoveries).toEqual([{ kind: 'anthropic', baseUrl: null, apiKey: null, connectionId: 'c-1' }]);
    expect(within(dialog).getByRole('checkbox', { name: /Claude Opus 5\.5/ })).toBeChecked();
  });

  it('取消：不存、對話框關掉', async () => {
    // Arrange
    const { user, dialog, backend } = await openEditDialog();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    expect(screen.queryByRole('dialog', { name: '編輯 API 金鑰' })).not.toBeInTheDocument();
    expect(backend.savedConnections).toHaveLength(0);
  });
});

describe('AI 設定：重新讀取與刪除', () => {
  it('重新讀取模型：結果用提示說明；失敗說原因', async () => {
    // Arrange
    const { user, backend } = await openPage(SAVED);

    // Act
    await chooseConnectionAction(user, 'Anthropic', '重新讀取模型');

    // Assert
    expect(await screen.findByText('已重新讀取，共 2 個模型，開放 1 個。')).toBeInTheDocument();
    expect(backend.refreshed).toEqual(['c-1']);

    // Act
    backend.rejectNext('refresh', { status: 422, code: 'ai.provider_failed', message: '供應商沒有回應。' });
    await chooseConnectionAction(user, 'Anthropic', '重新讀取模型');

    // Assert
    expect(await screen.findByText('供應商沒有回應。')).toBeInTheDocument();
    expect(screen.queryByText(/已重新讀取/)).not.toBeInTheDocument();
  });

  it('刪除：先確認，說明後果；確認後連線不見', async () => {
    // Arrange
    const { user, backend } = await openPage(SAVED);

    // Act
    await chooseConnectionAction(user, 'Anthropic', '刪除');

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '刪除 API 金鑰「Anthropic」？' });
    expect(within(dialog).getByText('已經有的對話留著；它是預設時，AI 會一起關掉。')).toBeInTheDocument();
    expect(backend.deleted).toHaveLength(0);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /刪除 API 金鑰/ })).not.toBeInTheDocument();
    });
    expect(backend.deleted).toEqual(['c-1']);
    expect(await screen.findByText(/還沒有 API 金鑰/)).toBeInTheDocument();
  });

  it('刪除確認按取消：什麼都不做；刪除失敗留在對話框並說明', async () => {
    // Arrange
    const { user, backend } = await openPage(SAVED);
    await chooseConnectionAction(user, 'Anthropic', '刪除');
    const dialog = await screen.findByRole('dialog', { name: '刪除 API 金鑰「Anthropic」？' });
    await user.click(within(dialog).getByRole('button', { name: '取消' }));
    expect(backend.deleted).toHaveLength(0);

    // Act
    backend.rejectNext('delete', { status: 403, code: 'ai.forbidden', message: '沒有權限刪除連線。' });
    await chooseConnectionAction(user, 'Anthropic', '刪除');
    await user.click(await screen.findByRole('button', { name: '刪除' }));

    // Assert
    expect(await screen.findByText('沒有權限刪除連線。')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '刪除 API 金鑰「Anthropic」？' })).toBeInTheDocument();
  });

  it('預設用的連線刪掉之後，預設回到沒選', async () => {
    // Arrange
    const { user } = await openPage(SAVED);
    await chooseConnectionAction(user, 'Anthropic', '刪除');

    // Act
    await user.click(await screen.findByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: '模型' })).toBeDisabled();
    });
    expect(screen.getByText('先在上面新增 API 金鑰、開放至少一個模型')).toBeInTheDocument();
  });
});

describe('AI 設定：沒存的修改', () => {
  it('改了就離開：先問；留下來還在，捨棄才離開', async () => {
    // Arrange
    const { user, router } = await openPage(SAVED);
    await user.click(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ }));

    // Act
    await user.click(screen.getByRole('link', { name: '我的工作' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '還有沒儲存的修改' });
    expect(currentPath(router)).toBe('/ai-settings');
    await user.click(within(dialog).getByRole('button', { name: '留下來' }));
    expect(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ })).not.toBeChecked();
    await user.click(screen.getByRole('link', { name: '我的工作' }));
    await user.click(await screen.findByRole('button', { name: '捨棄修改並離開' }));
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
  });

  it('存完再離開、沒改就離開：都不問', async () => {
    // Arrange
    const { user, router, save } = await openPage(SAVED);
    await user.click(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ }));
    await user.click(save);
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('link', { name: '我的工作' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
    expect(screen.queryByRole('dialog', { name: '還有沒儲存的修改' })).not.toBeInTheDocument();
  });

  it('新增連線不算沒存的修改（對話框自己存）', async () => {
    // Arrange
    const { user, router, dialog } = await openNewDialog();
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: '新增 API 金鑰' })).not.toBeInTheDocument();
    });

    // Act
    await user.click(screen.getByRole('link', { name: '我的工作' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work');
    });
  });
});

describe('AI 設定：版面', () => {
  it('由上往下：四種接法的對照、組織的 API 金鑰、開放給成員、成員自己的 AI；表單不另外取名，個人的指到設定的「AI 帳號」', async () => {
    // Arrange / Act
    await openPage();

    // Assert
    const titles = screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent);
    expect(titles).toEqual(['AI 的四種接法', '組織的 API 金鑰', '開放給成員', '成員自己的 AI']);
    const assistant = screen.getByRole('region', { name: '開放給成員' });
    expect(within(assistant).getByRole('button', { name: '儲存' }).closest('form')).not.toBeNull();
    expect(within(assistant).queryByRole('form')).not.toBeInTheDocument();
    expect(within(assistant).getByRole('heading', { level: 3, name: '預設' })).toBeInTheDocument();
    const policy = screen.getByRole('region', { name: '成員自己的 AI' });
    expect(within(policy).getByText(/設定的「AI 帳號」/)).toBeInTheDocument();
    expect(within(policy).getByRole('button', { name: '儲存（成員自己的 AI）' })).toBeInTheDocument();
    const keys = screen.getByRole('region', { name: '組織的 API 金鑰' });
    expect(within(keys).getByRole('button', { name: '新增 API 金鑰' })).toBeInTheDocument();
  });

  it('四種接法的對照：這一頁的格子標成目前所在，主機 AI 代理連到它的頁面', async () => {
    // Arrange / Act
    await openPage();

    // Assert
    const map = screen.getByRole('region', { name: 'AI 的四種接法' });
    expect(within(map).getByRole('link', { name: '組織的 API 金鑰' })).toHaveAttribute('aria-current', 'page');
    expect(within(map).getByRole('link', { name: '主機 AI 代理' })).toHaveAttribute('href', '/ai-settings/host');
    expect(within(map).getByRole('link', { name: '本機代理' })).toHaveAttribute('href', '/account/ai#local-agent');
  });

  it('沒有開放任何模型時「開放組織的 API 金鑰」不能勾，並說先新增 API 金鑰', async () => {
    // Arrange / Act
    await openPage();

    // Assert
    expect(screen.getByRole('checkbox', { name: /開放組織的 API 金鑰給成員使用/ })).toBeDisabled();
    expect(screen.getByText('先在上面新增 API 金鑰、開放至少一個模型才能打開。')).toBeInTheDocument();
  });

  it('擁有者的次導覽有「主機 AI 代理」連結，頁面上沒有主機代理的內容', async () => {
    // Arrange / Act
    await openPage();

    // Assert
    expect(within(screen.getByRole('navigation', { name: '設定' })).getByRole('link', { name: '主機 AI 代理' })).toHaveAttribute('href', '/ai-settings/host');
    expect(screen.queryByRole('button', { name: '新增主機代理' })).not.toBeInTheDocument();
  });
});
