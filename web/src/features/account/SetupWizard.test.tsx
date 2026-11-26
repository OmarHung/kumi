import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { serveAccessTokens, tokenOf } from '../../test/account';
import { signedInAs } from '../../test/api';
import { serveCli } from '../../test/cli';
import { exampleOwner } from '../../test/fixtures';
import { grantOf, serveOAuthGrants } from '../../test/oauth';
import { exampleMachine, serveLocalAgents } from '../../test/personalAi';
import { makeAiOptions, serveAiOptions } from '../../test/assistant';
import { renderApp } from '../../test/renderApp';
import { failure, ok } from '../../test/api';
import { server } from '../../test/server';
import { http } from 'msw';

const MAC_UA = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Safari/605.1.15';
const WINDOWS_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36';
const ORIGIN = window.location.origin;

// jsdom 點下載連結會試著「導覽」並印警告：在冒泡的最後攔下來，連結的點擊處理照樣執行。
function blockNavigation(event: Event) {
  event.preventDefault();
}

beforeEach(() => {
  document.addEventListener('click', blockNavigation);
});

afterEach(() => {
  document.removeEventListener('click', blockNavigation);
  vi.restoreAllMocks();
  vi.useRealTimers();
});

interface OpenOptions {
  readonly userAgent?: string;
  /** 要蓋掉預設端點的設定：signedInAs 之後才執行（後加的優先）。 */
  readonly serve?: () => void;
  /** 命令列已經登入過：第 1 步一開始就收起來，沒有下載連結可等，改等第 1 步的標題。 */
  readonly isSeeded?: boolean;
}

async function openWizard({ userAgent = MAC_UA, serve, isSeeded = false }: OpenOptions = {}) {
  vi.spyOn(window.navigator, 'userAgent', 'get').mockReturnValue(userAgent);
  signedInAs(exampleOwner);
  serve?.();
  const view = renderApp('/account/integrations');
  const section = await screen.findByRole('region', { name: '設定精靈' });
  if (isSeeded) await within(section).findByRole('heading', { level: 3, name: /下載 kumi/ });
  else await within(section).findByRole('link', { name: /下載 kumi 0\.3\.0/ });
  return { ...view, section };
}

function stepOf(section: HTMLElement, title: string): HTMLElement {
  const heading = within(section).getByRole('heading', { level: 3, name: new RegExp(title) });
  const step = heading.closest('li');
  if (step === null) throw new Error(`找不到步驟「${title}」`);
  return step;
}

describe('設定精靈：版面與展開順序', () => {
  it('是 id="setup" 的區塊，也是這一頁的第一筆內容；放在命令列工具的前面', async () => {
    // Arrange
    // Act
    const { section } = await openWizard();

    // Assert
    expect(section).toHaveAttribute('id', 'setup');
    expect(section).toHaveAttribute('data-content-start');
    const regions = screen.getAllByRole('region').map((region) => region.getAttribute('aria-labelledby'));
    expect(regions.length).toBeGreaterThan(1);
    expect(screen.getAllByRole('heading', { level: 2 })[0]).toHaveTextContent('設定精靈');
  });

  it('一開始只有第一步展開，後面兩步只有淡色的標題', async () => {
    // Arrange
    // Act
    const { section } = await openWizard();

    // Assert
    const first = stepOf(section, '下載 kumi');
    expect(first).toHaveAttribute('data-state', 'current');
    expect(within(first).getByRole('link', { name: /下載 kumi 0\.3\.0/ })).toBeInTheDocument();
    const second = stepOf(section, '在終端機貼上這一行');
    const third = stepOf(section, '完成的項目');
    expect(second).toHaveAttribute('data-state', 'upcoming');
    expect(third).toHaveAttribute('data-state', 'upcoming');
    expect(within(second).queryByRole('region')).not.toBeInTheDocument();
    expect(within(third).queryByRole('list')).not.toBeInTheDocument();
  });

  it('按下載：第一步收成一行摘要並打勾，第二步展開、焦點到第二步的標題', async () => {
    // Arrange
    const { user, section } = await openWizard();

    // Act
    await user.click(within(section).getByRole('link', { name: /下載 kumi 0\.3\.0/ }));

    // Assert
    const first = stepOf(section, '下載 kumi');
    expect(first).toHaveAttribute('data-state', 'done');
    expect(within(first).getByText('已下載 kumi 0.3.0（Mac（Apple 晶片））')).toBeInTheDocument();
    expect(within(first).queryByRole('link')).not.toBeInTheDocument();
    const second = stepOf(section, '在終端機貼上這一行');
    expect(second).toHaveAttribute('data-state', 'current');
    expect(within(second).getByRole('region', { name: '安裝並啟動設定精靈的指令' })).toBeInTheDocument();
    await waitFor(() => {
      expect(within(second).getByRole('heading', { level: 3 })).toHaveFocus();
    });
  });

  it('「已經裝好 kumi 了」：第二步只給啟動精靈的那一行', async () => {
    // Arrange
    const { user, section } = await openWizard();

    // Act
    await user.click(within(section).getByRole('button', { name: '已經裝好 kumi 了' }));

    // Assert
    expect(within(stepOf(section, '下載 kumi')).getByText('已經裝好 kumi')).toBeInTheDocument();
    const second = stepOf(section, '在終端機貼上這一行');
    expect(within(second).getByRole('region', { name: '啟動設定精靈的指令' })).toHaveTextContent(`kumi setup --server "${ORIGIN}"`);
    expect(within(second).queryByRole('region', { name: '安裝並啟動設定精靈的指令' })).not.toBeInTheDocument();
  });

  it('完成的步驟可以「重看」再「收起」，不影響目前的步驟', async () => {
    // Arrange
    const { user, section } = await openWizard();
    await user.click(within(section).getByRole('link', { name: /下載 kumi 0\.3\.0/ }));
    const first = stepOf(section, '下載 kumi');

    // Act
    await user.click(within(first).getByRole('button', { name: '重看：下載 kumi' }));

    // Assert
    expect(within(first).getByRole('link', { name: /下載 kumi 0\.3\.0/ })).toBeInTheDocument();
    expect(within(first).getByRole('button', { name: '收起：下載 kumi' })).toHaveAttribute('aria-expanded', 'true');
    expect(stepOf(section, '在終端機貼上這一行')).toHaveAttribute('data-state', 'current');

    // Act
    await user.click(within(first).getByRole('button', { name: '收起：下載 kumi' }));

    // Assert
    expect(within(first).queryByRole('link')).not.toBeInTheDocument();
  });
});

describe('設定精靈：和「命令列工具」共用平台的選擇', () => {
  it('在精靈選 Windows，「命令列工具」跟著選 Windows；反過來也一樣', async () => {
    // Arrange
    const { user, section } = await openWizard();
    const cliSection = await screen.findByRole('region', { name: '命令列工具' });
    const wizardGroup = within(section).getByRole('group', { name: '設定精靈的作業系統' });
    const cliGroup = within(cliSection).getByRole('group', { name: '命令列工具的作業系統' });

    // Act
    await user.click(within(wizardGroup).getByRole('button', { name: 'Windows' }));

    // Assert
    expect(within(cliGroup).getByRole('button', { name: 'Windows' })).toHaveAttribute('aria-pressed', 'true');

    // Act
    await user.click(within(cliGroup).getByRole('button', { name: 'Mac（Intel）' }));

    // Assert
    expect(within(wizardGroup).getByRole('button', { name: 'Mac（Intel）' })).toHaveAttribute('aria-pressed', 'true');
  });
});

describe('設定精靈：第二步的一行指令', () => {
  it('Mac：安裝、PATH、完整路徑啟動精靈，網址用雙引號；另附已經裝好的那一行', async () => {
    // Arrange
    const { user, section } = await openWizard();

    // Act
    await user.click(within(section).getByRole('link', { name: /下載 kumi 0\.3\.0/ }));

    // Assert
    const second = stepOf(section, '在終端機貼上這一行');
    const command = within(second).getByRole('region', { name: '安裝並啟動設定精靈的指令' });
    expect(command).toHaveTextContent('mv ~/Downloads/kumi-0.3.0-macos-arm64 ~/.local/bin/kumi');
    expect(command).toHaveTextContent('&& { grep -qsxF');
    expect(command).toHaveTextContent(`&& ~/.local/bin/kumi setup --server "${ORIGIN}"`);
    expect(within(second).getByRole('region', { name: '啟動設定精靈的指令' })).toHaveTextContent(`kumi setup --server "${ORIGIN}"`);
    expect(within(second).getByText(/會問你要設定哪些，再開瀏覽器請你按「允許」/)).toBeInTheDocument();
  });

  it('Windows：PowerShell 的安裝、PATH，最後用 $d 的完整路徑啟動精靈', async () => {
    // Arrange
    const { user, section } = await openWizard({ userAgent: WINDOWS_UA });

    // Act
    await user.click(within(section).getByRole('link', { name: /下載 kumi 0\.3\.0/ }));

    // Assert
    const command = within(stepOf(section, '在終端機貼上這一行')).getByRole('region', { name: '安裝並啟動設定精靈的指令' });
    expect(command).toHaveTextContent('Unblock-File');
    expect(command).toHaveTextContent(`& "$d\\kumi.exe" setup --server "${ORIGIN}"`);
  });

  it('按「複製」就算這一步做了：第二步收起、第三步展開', async () => {
    // Arrange
    const { user, section } = await openWizard();
    await user.click(within(section).getByRole('link', { name: /下載 kumi 0\.3\.0/ }));
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);

    // Act
    await user.click(within(section).getByRole('button', { name: '複製安裝並啟動設定精靈的指令' }));

    // Assert
    expect(writeText).toHaveBeenCalledWith(expect.stringContaining(`setup --server "${ORIGIN}"`));
    await waitFor(() => {
      expect(stepOf(section, '完成的項目')).toHaveAttribute('data-state', 'current');
    });
    expect(within(stepOf(section, '在終端機貼上這一行')).getByText('已複製指令')).toBeInTheDocument();
  });

  it('按「下一步」也算（複製不到時自己選取貼上）', async () => {
    // Arrange
    const { user, section } = await openWizard();
    await user.click(within(section).getByRole('link', { name: /下載 kumi 0\.3\.0/ }));

    // Act
    await user.click(within(stepOf(section, '在終端機貼上這一行')).getByRole('button', { name: '下一步' }));

    // Assert
    expect(stepOf(section, '完成的項目')).toHaveAttribute('data-state', 'current');
    expect(within(stepOf(section, '在終端機貼上這一行')).getByText('已執行指令')).toBeInTheDocument();
  });
});

async function reachStatusStep(section: HTMLElement, user: Awaited<ReturnType<typeof openWizard>>['user']) {
  await user.click(within(section).getByRole('button', { name: '已經裝好 kumi 了' }));
  await user.click(within(stepOf(section, '在終端機貼上這一行')).getByRole('button', { name: '下一步' }));
  return stepOf(section, '完成的項目');
}

describe('設定精靈：第三步的即時狀態', () => {
  it('什麼都還沒有：三列都是「還沒有」', async () => {
    // Arrange
    const { user, section } = await openWizard({ serve: () => { serveAiOptions(makeAiOptions({ canAddPersonal: true })); } });

    // Act
    const third = await reachStatusStep(section, user);

    // Assert
    await waitFor(() => {
      expect(within(third).getAllByText('還沒有')).toHaveLength(3);
    });
    expect(within(third).getByText('命令列工具')).toBeInTheDocument();
    expect(within(third).getByText('在 AI 工具裡用 Kumi')).toBeInTheDocument();
    expect(within(third).getByText('本機代理（AI 助理用你的電腦）')).toBeInTheDocument();
  });

  it('都做完了：已登入的電腦、已授權的應用、在線的電腦', async () => {
    // Arrange
    const { section } = await openWizard({
      isSeeded: true,
      serve: () => {
        serveAiOptions(makeAiOptions({ canAddPersonal: true }));
        serveAccessTokens([tokenOf({ machineName: 'MacBook-Pro' }), tokenOf({ tokenId: 'a', machineName: 'MacBook-Pro', scopes: ['agent:run', 'items:read'] })]);
        serveOAuthGrants([grantOf(), grantOf({ grantId: 'g2', clientName: 'Codex' })]);
        serveLocalAgents([exampleMachine({ name: 'MacBook-Pro' })]);
      },
    });

    // Act
    const third = stepOf(section, '完成的項目');

    // Assert
    expect(await within(third).findByText('已登入：MacBook-Pro')).toBeInTheDocument();
    expect(await within(third).findByText('已授權：Claude Code、Codex')).toBeInTheDocument();
    expect(await within(third).findByText('在線：MacBook-Pro')).toBeInTheDocument();
  });

  it('本機代理登記過但都離線：已登記，目前離線', async () => {
    // Arrange
    const { user, section } = await openWizard({
      serve: () => {
        serveAiOptions(makeAiOptions({ canAddPersonal: true }));
        serveLocalAgents([exampleMachine({ online: false })]);
      },
    });

    // Act
    const third = await reachStatusStep(section, user);

    // Assert
    expect(await within(third).findByText('已登記，目前離線')).toBeInTheDocument();
  });

  it('本機代理都離線：按「排查說明」開對話框教怎麼排查與修復，關上後焦點回到按鈕', async () => {
    // Arrange
    const { user, section } = await openWizard({
      serve: () => {
        serveAiOptions(makeAiOptions({ canAddPersonal: true }));
        serveLocalAgents([exampleMachine({ online: false })]);
      },
    });
    const third = await reachStatusStep(section, user);
    const button = await within(third).findByRole('button', { name: '排查說明' });

    // Act
    await user.click(button);

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '本機代理離線時怎麼排查' });
    expect(within(dialog).getByRole('region', { name: '看狀態的指令' })).toHaveTextContent('kumi agent status');
    expect(within(dialog).getByRole('region', { name: '重新在背景啟動的指令' })).toHaveTextContent('kumi agent enable');
    expect(within(dialog).getByRole('region', { name: '重新登入本機代理的指令' })).toHaveTextContent('kumi agent login --server');
    await user.click(within(dialog).getByRole('button', { name: '關閉' }));
    expect(screen.queryByRole('dialog', { name: '本機代理離線時怎麼排查' })).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it('本機代理在線：不顯示排查的說明', async () => {
    // Arrange
    const { user, section } = await openWizard({
      serve: () => {
        serveAiOptions(makeAiOptions({ canAddPersonal: true }));
        serveLocalAgents([exampleMachine({ name: 'MacBook-Pro' })]);
      },
    });

    // Act
    const third = await reachStatusStep(section, user);

    // Assert
    expect(await within(third).findByText('在線：MacBook-Pro')).toBeInTheDocument();
    expect(within(third).queryByRole('button', { name: '排查說明' })).not.toBeInTheDocument();
  });

  it('組織不允許自己的 AI 帳號、也沒有電腦：本機代理那列寫「組織沒有開放」', async () => {
    // Arrange
    const { user, section } = await openWizard({ serve: () => { serveAiOptions(makeAiOptions({ canAddPersonal: false })); } });

    // Act
    const third = await reachStatusStep(section, user);

    // Assert
    expect(await within(third).findByText('組織沒有開放')).toBeInTheDocument();
  });
});

describe('設定精靈：只在看得到第三步時才每 5 秒讀', () => {
  async function openWithCounter() {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    let machines: ReturnType<typeof serveLocalAgents> | undefined;
    const view = await openWizard({
      serve: () => {
        serveAiOptions(makeAiOptions({ canAddPersonal: true }));
        machines = serveLocalAgents([]);
      },
    });
    if (machines === undefined) throw new Error('沒有設定假的本機代理端點');
    return { machines, ...view };
  }

  it('第三步還沒展開時不讀：前兩步停著 5 秒，沒有新的請求', async () => {
    // Arrange
    const { machines } = await openWithCounter();
    const before = machines.listCalls();

    // Act
    await vi.advanceTimersByTimeAsync(10_000);

    // Assert
    expect(machines.listCalls()).toBe(before);
  });

  it('展開之後每 5 秒重讀，電腦上線了畫面跟上', async () => {
    // Arrange
    const { machines, user, section } = await openWithCounter();
    const third = await reachStatusStep(section, user);
    await waitFor(() => {
      expect(within(third).getByText('本機代理（AI 助理用你的電腦）').closest('div')).toHaveTextContent('本機代理（AI 助理用你的電腦）還沒有');
    });
    const before = machines.listCalls();

    // Act
    machines.set([exampleMachine({ name: 'MacBook-Pro' })]);
    await vi.advanceTimersByTimeAsync(5000);

    // Assert
    await waitFor(() => {
      expect(machines.listCalls()).toBeGreaterThan(before);
    });
    expect(await within(third).findByText('在線：MacBook-Pro')).toBeInTheDocument();
  });

  it('分頁看不見時不讀', async () => {
    // Arrange
    const { machines, user, section } = await openWithCounter();
    await reachStatusStep(section, user);
    await waitFor(() => {
      expect(machines.listCalls()).toBeGreaterThan(0);
    });
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    const before = machines.listCalls();

    // Act
    await vi.advanceTimersByTimeAsync(15_000);

    // Assert
    expect(machines.listCalls()).toBe(before);
  });
});

describe('設定精靈：命令列已經登入過的，回到這一頁直接看狀態', () => {
  const loggedIn = () => {
    serveAccessTokens([tokenOf({ machineName: 'MacBook-Pro' })]);
  };

  it('第 1、2 步一開始就是完成，第 3 步展開', async () => {
    // Arrange
    // Act
    const { section } = await openWizard({ serve: loggedIn, isSeeded: true });

    // Assert
    const first = stepOf(section, '下載 kumi');
    expect(first).toHaveAttribute('data-state', 'done');
    expect(within(first).getByText('已經裝好 kumi')).toBeInTheDocument();
    const second = stepOf(section, '在終端機貼上這一行');
    expect(second).toHaveAttribute('data-state', 'done');
    expect(within(second).getByText('已執行過')).toBeInTheDocument();
    const third = stepOf(section, '完成的項目');
    expect(third).toHaveAttribute('data-state', 'current');
    expect(await within(third).findByText('已登入：MacBook-Pro')).toBeInTheDocument();
  });

  it('要設定另一台電腦：按第 1 步的「重看」，下載連結還在', async () => {
    // Arrange
    const { user, section } = await openWizard({ serve: loggedIn, isSeeded: true });

    // Act
    await user.click(within(stepOf(section, '下載 kumi')).getByRole('button', { name: '重看：下載 kumi' }));

    // Assert
    expect(within(stepOf(section, '下載 kumi')).getByRole('link', { name: /下載 kumi 0\.3\.0/ })).toBeInTheDocument();
  });

  it.each([
    ['沒有電腦名稱', tokenOf()],
    ['是本機代理用的', tokenOf({ machineName: 'MacBook-Pro', scopes: ['agent:run', 'items:read'] })],
    ['已經過期', tokenOf({ machineName: 'MacBook-Pro', expiresAt: '2020-01-01T00:00:00Z' })],
  ])('權杖%s：照舊從第 1 步開始', async (_name, token) => {
    // Arrange
    // Act
    const { section } = await openWizard({
      serve: () => {
        serveAccessTokens([token]);
      },
    });

    // Assert
    expect(stepOf(section, '下載 kumi')).toHaveAttribute('data-state', 'current');
    expect(stepOf(section, '完成的項目')).toHaveAttribute('data-state', 'upcoming');
  });

  it('清單讀不到：照舊從第 1 步開始', async () => {
    // Arrange
    // Act
    const { section } = await openWizard({
      serve: () => {
        server.use(http.get('/v1/account/tokens', () => failure(500, 'server.error', '壞了')));
      },
    });

    // Assert
    expect(stepOf(section, '下載 kumi')).toHaveAttribute('data-state', 'current');
  });

  it('還在讀的時候不畫第 1 步（讀到之後才決定，不先畫再跳）；等太久就從第 1 步開始', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/account/tokens', () => new Promise<never>(() => undefined)));

    // Act
    renderApp('/account/integrations');
    const section = await screen.findByRole('region', { name: '設定精靈' });

    // Assert
    expect(within(section).queryByRole('heading', { level: 3 })).not.toBeInTheDocument();
    expect(within(section).getByText('正在確認設定的進度…')).toBeInTheDocument();
    expect(await within(section).findByRole('link', { name: /下載 kumi 0\.3\.0/ }, { timeout: 3000 })).toBeInTheDocument();
  });
});

describe('設定精靈：第三步的變化唸給報讀軟體聽', () => {
  async function openWithMutableTokens(initial: readonly ReturnType<typeof tokenOf>[], isSeeded = false) {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    let tokens = initial;
    const view = await openWizard({
      isSeeded,
      serve: () => {
        server.use(http.get('/v1/account/tokens', () => ok(tokens)));
      },
    });
    return {
      ...view,
      setTokens: (next: readonly ReturnType<typeof tokenOf>[]) => {
        tokens = next;
      },
    };
  }

  it('從「還沒有」變成完成：寫進永遠掛著的 status', async () => {
    // Arrange
    const { user, section, setTokens } = await openWithMutableTokens([]);
    const third = await reachStatusStep(section, user);
    await waitFor(() => {
      expect(within(third).getAllByText('還沒有').length).toBeGreaterThan(0);
    });
    expect(within(third).queryByText('命令列工具：已登入 MacBook-Pro')).not.toBeInTheDocument();

    // Act
    setTokens([tokenOf({ machineName: 'MacBook-Pro' })]);
    await vi.advanceTimersByTimeAsync(5000);

    // Assert
    const message = await within(third).findByText('命令列工具：已登入 MacBook-Pro');
    expect(message.closest('[role="status"]')).not.toBeNull();
  });

  it('進頁時就已經完成的不報', async () => {
    // Arrange
    const { section } = await openWithMutableTokens([tokenOf({ machineName: 'MacBook-Pro' })], true);

    // Act
    const third = stepOf(section, '完成的項目');
    await within(third).findByText('已登入：MacBook-Pro');

    // Assert
    expect(within(third).queryByText(/命令列工具：已登入/)).not.toBeInTheDocument();
  });
});

describe('設定精靈：本機代理的排查說明', () => {
  it('對話框開著時代理上線：對話框留著並說已經在線，關上後焦點到「完成的項目」', async () => {
    // Arrange
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    let agents: ReturnType<typeof serveLocalAgents> | undefined;
    const { user, section } = await openWizard({
      serve: () => {
        serveAiOptions(makeAiOptions({ canAddPersonal: true }));
        agents = serveLocalAgents([exampleMachine({ online: false })]);
      },
    });
    const third = await reachStatusStep(section, user);
    await user.click(await within(third).findByRole('button', { name: '排查說明' }));
    const dialog = await screen.findByRole('dialog', { name: '本機代理離線時怎麼排查' });

    // Act
    agents?.set([exampleMachine({ name: 'MacBook-Pro' })]);
    await vi.advanceTimersByTimeAsync(5000);

    // Assert
    expect(await within(dialog).findByText('本機代理已經在線了，可以關上這個視窗。')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: '關閉' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(within(third).getByRole('heading', { name: /完成的項目/ })).toHaveFocus();
  });
});

describe('設定精靈：伺服器沒有提供下載', () => {
  it('第一步說明沒有下載，仍可以按「已經裝好 kumi 了」往下', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveCli({ version: null, packages: [] });
    const { user } = renderApp('/account/integrations');
    const section = await screen.findByRole('region', { name: '設定精靈' });

    // Act
    await within(section).findByText(/沒有提供命令列工具的下載/);
    await user.click(within(section).getByRole('button', { name: '已經裝好 kumi 了' }));

    // Assert
    expect(within(stepOf(section, '在終端機貼上這一行')).getByRole('region', { name: '啟動設定精靈的指令' })).toBeInTheDocument();
  });
});
