import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { exampleEngine, exampleHostAgent, exampleHostSource } from '../../test/hostAgents';
import { chooseOption } from '../../test/select';
import { chooseMenuAction, openHostSection } from './hostAgentsTestKit';

const BILLING = /我了解這會以 API 用量計費/;

async function openNewSource(initial: Parameters<typeof openHostSection>[0] = { agents: [exampleHostAgent()] }) {
  const page = await openHostSection(initial);
  await screen.findByText('辦公室主機');
  await page.user.click(screen.getByRole('button', { name: '新增來源' }));
  const dialog = await screen.findByRole('dialog', { name: '新增來源' });
  return { ...page, dialog };
}

describe('主機來源：清單', () => {
  it('每個來源寫名稱、狀態、代理與引擎、開放幾個模型、執行中與排隊的件數、這個月的用量', async () => {
    // Arrange / Act
    await openHostSection({ agents: [exampleHostAgent()], sources: [exampleHostSource()] });

    // Assert
    const list = await screen.findByRole('list', { name: '主機來源' });
    expect(within(list).getByText('主機上的 Claude Code')).toBeInTheDocument();
    expect(within(list).getByText('可以使用')).toBeInTheDocument();
    expect(within(list).getByText('辦公室主機 · Claude Code · 開放 1 個模型')).toBeInTheDocument();
    expect(within(list).getByText('執行中 1 件 · 排隊 2 件')).toBeInTheDocument();
    expect(within(list).getByText('本月 12 則 · 輸入 120,000 · 輸出 8,000 token')).toBeInTheDocument();
  });

  it('不能用的來源：狀態文字加伺服器給的說明', async () => {
    // Arrange / Act
    await openHostSection({
      agents: [exampleHostAgent()],
      sources: [exampleHostSource({ status: 'agent_offline', statusMessage: '主機已經 10 分鐘沒有回應。' })],
    });

    // Assert
    expect(await screen.findByText('主機離線：主機已經 10 分鐘沒有回應。')).toBeInTheDocument();
  });

  it('沒有代理時「新增來源」不能按，說明先新增代理', async () => {
    // Arrange
    const { user } = await openHostSection();

    // Act
    await user.click(await screen.findByRole('button', { name: '新增來源' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByText('先新增一台主機代理。')).toBeInTheDocument();
  });
});

describe('主機來源：新增', () => {
  it('預設值：名稱、引擎回報的模型都開放、能接工作所以開著；沒勾計費確認不能存', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewSource();

    // Assert
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveValue('主機上的 Claude Code');
    expect(within(dialog).getByRole('checkbox', { name: /Opus/ })).toBeChecked();
    expect(within(dialog).getByRole('checkbox', { name: /Sonnet/ })).toBeChecked();
    expect(within(dialog).getByRole('checkbox', { name: '開放給成員使用' })).toBeChecked();
    expect(within(dialog).getByRole('checkbox', { name: BILLING })).not.toBeChecked();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByText('開著之前要先確認以 API 用量計費')).toBeInTheDocument();
    expect(within(dialog).getByRole('checkbox', { name: BILLING })).toHaveFocus();
    expect(backend.savedSources).toHaveLength(0);
  });

  it('勾了計費確認、填上限、選預設模型與思考深度：送出的內容正確，列表出現新來源', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewSource();

    // Act
    await user.click(within(dialog).getByRole('checkbox', { name: /Sonnet/ }));
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '預設的模型' }), 'Opus');
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '預設的思考深度' }), '低');
    await user.type(within(dialog).getByRole('textbox', { name: '每人每天最多（則）' }), '15');
    await user.type(within(dialog).getByRole('textbox', { name: '每月 token 上限' }), '5000000');
    await user.click(within(dialog).getByRole('checkbox', { name: BILLING }));
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: '新增來源' })).not.toBeInTheDocument();
    });
    expect(backend.savedSources).toEqual([
      {
        id: null,
        input: {
          agentId: 'agent-1',
          engine: 'claude_code',
          name: '主機上的 Claude Code',
          enabled: true,
          enabledModels: ['opus'],
          defaultModel: 'opus',
          defaultEffort: 'low',
          limits: { dailyRequestsPerPerson: 15, monthlyRequests: null, monthlyTokens: 5000000 },
          acknowledgeApiBilling: true,
        },
      },
    ]);
    expect(await screen.findByRole('list', { name: '主機來源' })).toBeInTheDocument();
  });

  it('引擎還不能接工作（Codex 沒登入）：預設關著、說明可以先存成停用，不用勾計費確認', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewSource();

    // Act
    await user.click(within(dialog).getByRole('button', { name: 'Codex' }));

    // Assert
    expect(within(dialog).getByText(/Codex 現在是「需要登入」/)).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveValue('主機上的 Codex');
    expect(within(dialog).getByRole('checkbox', { name: '開放給成員使用' })).not.toBeChecked();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(backend.savedSources).toHaveLength(1);
    });
    expect(backend.savedSources[0]?.input).toMatchObject({ engine: 'codex', enabled: false, acknowledgeApiBilling: false });
  });

  it('上限不是正整數：說明在那一欄、不送出', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewSource();

    // Act
    await user.click(within(dialog).getByRole('checkbox', { name: BILLING }));
    await user.type(within(dialog).getByRole('textbox', { name: '每月最多（則）' }), '0');
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByText('填 1 到 1,000,000 的整數，留白是不限')).toBeInTheDocument();
    expect(backend.savedSources).toHaveLength(0);
  });

  it('引擎登入的是訂閱帳號（伺服器拒絕）：說明顯示在對話框裡，不關掉', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewSource();
    backend.rejectNext('source', { status: 422, code: 'ai.host_billing_not_shareable', message: '這個引擎登入的是訂閱帳號，條款不允許全組織共用。' });

    // Act
    await user.click(within(dialog).getByRole('checkbox', { name: BILLING }));
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByText('這個引擎登入的是訂閱帳號，條款不允許全組織共用。')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '新增來源' })).toBeInTheDocument();
  });
});

describe('主機來源：編輯、用量、刪除', () => {
  it('編輯：代理與引擎只讀、已開放的模型勾著、計費確認照已確認的；停用之後不用確認', async () => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent()], sources: [exampleHostSource()] });
    await user.click(await screen.findByRole('button', { name: '編輯 主機上的 Claude Code' }));
    const dialog = await screen.findByRole('dialog', { name: '編輯來源' });

    // Assert
    expect(within(dialog).getByText('辦公室主機 · Claude Code（建立之後不能換）')).toBeInTheDocument();
    expect(within(dialog).getByRole('checkbox', { name: /Opus/ })).toBeChecked();
    expect(within(dialog).getByRole('checkbox', { name: /Sonnet/ })).not.toBeChecked();
    expect(within(dialog).getByRole('checkbox', { name: BILLING })).toBeChecked();
    expect(within(dialog).getByRole('textbox', { name: '每人每天最多（則）' })).toHaveValue('20');

    // Act
    await user.click(within(dialog).getByRole('checkbox', { name: '開放給成員使用' }));
    await user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(backend.savedSources).toHaveLength(1);
    });
    expect(backend.savedSources[0]).toMatchObject({ id: 'src-1', input: { enabled: false } });
  });

  it('引擎這次沒回報模型清單：已開放的模型仍然列出來，不會憑空不見', async () => {
    // Arrange
    const agent = exampleHostAgent({ engines: [exampleEngine({ models: [], readiness: 'not_logged_in', loggedIn: false })] });
    const { user } = await openHostSection({ agents: [agent], sources: [exampleHostSource()] });

    // Act
    await user.click(await screen.findByRole('button', { name: '編輯 主機上的 Claude Code' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '編輯來源' });
    expect(within(dialog).getByRole('checkbox', { name: /Opus/ })).toBeChecked();
  });

  it('更多選單的「用量」：依成員、依專案列出則數與 token，並說明不是供應商的帳單', async () => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent()], sources: [exampleHostSource()] });
    await screen.findByText('主機上的 Claude Code');

    // Act
    await chooseMenuAction(user, '「主機上的 Claude Code」的更多動作', '用量');

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '「主機上的 Claude Code」的用量' });
    const people = await within(dialog).findByRole('list', { name: '依成員的用量' });
    expect(within(people).getByText('小明')).toBeInTheDocument();
    expect(within(people).getByText('8 則（完成 7、失敗 1、中斷 0）')).toBeInTheDocument();
    expect(within(dialog).getByRole('list', { name: '依專案的用量' })).toHaveTextContent('WEB');
    expect(within(dialog).getByText('Kumi 記錄的用量，不是供應商的帳單或剩餘額度。')).toBeInTheDocument();
    expect(backend.usageRequests).toEqual(['src-1']);
  });

  it('用量讀不到：顯示失敗與重試', async () => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent()], sources: [exampleHostSource()] });
    backend.rejectNext('usage', { status: 500, code: 'server.error', message: '伺服器出錯了。' });
    await screen.findByText('主機上的 Claude Code');

    // Act
    await chooseMenuAction(user, '「主機上的 Claude Code」的更多動作', '用量');

    // Assert
    expect(await screen.findByText('用量載入失敗')).toBeInTheDocument();
  });

  it('刪除要先確認', async () => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent()], sources: [exampleHostSource()] });
    await screen.findByText('主機上的 Claude Code');

    // Act
    await chooseMenuAction(user, '「主機上的 Claude Code」的更多動作', '刪除');
    const confirm = await screen.findByRole('dialog');
    expect(backend.deletedSources).toHaveLength(0);
    await user.click(within(confirm).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(backend.deletedSources).toEqual(['src-1']);
    });
    expect(await screen.findByText(/還沒有來源/)).toBeInTheDocument();
  });
});

describe('主機來源：後端的欄位錯誤不會被吞掉', () => {
  const save = (dialog: HTMLElement, user: Awaited<ReturnType<typeof openNewSource>>['user']) =>
    user.click(within(dialog).getByRole('button', { name: '儲存' }));
  const ENGINE_TAKEN = '這台代理的這個引擎已經有來源了。';

  it('engine 的細項：訊息顯示在引擎切換下面，焦點移到切換鈕', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewSource();
    backend.rejectNext('source', { status: 422, code: 'ai.settings_invalid', message: '來源設定不對。', details: [{ code: 'ai.field_invalid', path: 'engine', message: ENGINE_TAKEN }] });

    // Act
    await user.click(within(dialog).getByRole('checkbox', { name: BILLING }));
    await save(dialog, user);

    // Assert
    expect(await within(dialog).findByText(ENGINE_TAKEN)).toBeInTheDocument();
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: 'Claude Code' })).toHaveFocus();
    });
    expect(within(dialog).getByRole('button', { name: 'Claude Code' })).toHaveAttribute('aria-describedby');
  });

  it('這台代理已經有來源的引擎：新增時不再列出，並說明原因', async () => {
    // Arrange / Act
    const { dialog } = await openNewSource({ agents: [exampleHostAgent()], sources: [exampleHostSource()] });

    // Assert
    expect(within(dialog).queryByRole('button', { name: 'Claude Code' })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Codex' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(dialog).getByText('Claude Code 在這台代理上已經有來源了，不能再新增。')).toBeInTheDocument();
  });

  it('defaultEffort 的細項：訊息顯示在預設的思考深度下面', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewSource();
    backend.rejectNext('source', { status: 422, code: 'ai.settings_invalid', message: '設定不對。', details: [{ code: 'ai.field_invalid', path: 'defaultEffort', message: '這個模型不能選這一級。' }] });

    // Act
    await user.click(within(dialog).getByRole('checkbox', { name: BILLING }));
    await save(dialog, user);

    // Assert
    expect(await within(dialog).findByText('這個模型不能選這一級。')).toBeInTheDocument();
  });

  it('limits.xxx 的路徑對到畫面上的上限欄位', async () => {
    // Arrange
    const { user, dialog, backend } = await openNewSource();
    backend.rejectNext('source', {
      status: 422,
      code: 'ai.settings_invalid',
      message: '上限不對。',
      details: [
        { code: 'ai.field_invalid', path: 'limits.dailyRequestsPerPerson', message: '每天的上限太大。' },
        { code: 'ai.field_invalid', path: 'limits.monthlyRequests', message: '每月的則數太大。' },
        { code: 'ai.field_invalid', path: 'limits.monthlyTokens', message: '每月的 token 太大。' },
      ],
    });

    // Act
    await user.click(within(dialog).getByRole('checkbox', { name: BILLING }));
    await save(dialog, user);

    // Assert
    expect(await within(dialog).findByText('每天的上限太大。')).toBeInTheDocument();
    expect(within(dialog).getByText('每月的則數太大。')).toBeInTheDocument();
    expect(within(dialog).getByText('每月的 token 太大。')).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: '每人每天最多（則）' })).toHaveFocus();
  });

  it('編輯時畫面上沒有欄位可放的錯誤（agentId、engine）：併進表單上方的說明', async () => {
    // Arrange
    const { user, backend } = await openHostSection({ agents: [exampleHostAgent()], sources: [exampleHostSource()] });
    backend.rejectNext('source', { status: 422, code: 'ai.settings_invalid', message: '不能改。', details: [{ code: 'ai.field_invalid', path: 'engine', message: '代理與引擎建立之後不能改。' }] });
    await user.click(await screen.findByRole('button', { name: '編輯 主機上的 Claude Code' }));
    const dialog = await screen.findByRole('dialog', { name: '編輯來源' });

    // Act
    await save(dialog, user);

    // Assert
    expect(await within(dialog).findByText('代理與引擎建立之後不能改。')).toBeInTheDocument();
  });
});
