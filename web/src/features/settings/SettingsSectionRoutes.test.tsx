import { act, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { captureProposals, invalidChangeSet, serveChangeSets } from '../../test/changesets';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';

async function openSettings(path: string, detail: ProjectDetail = smkDetail(), respond?: Parameters<typeof captureProposals>[1]) {
  signedInAs(exampleOwner, { details: [detail] });
  serveChangeSets(detail.project.key, []);
  const proposals = captureProposals(detail, respond);
  const view = renderApp(path);
  await screen.findByRole('heading', { level: 1, name: '設定' });
  return { ...view, proposals };
}

function sectionTabs() {
  return within(screen.getByRole('navigation', { name: '設定的區塊' }));
}

describe('設定頁的區塊', () => {
  it('設定頁本身轉到「流程」，換頁之前帶的提示也帶過去', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', []);

    // Act
    const { router } = renderApp('/projects/SMK/settings');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
    });
    // 設定頁是 lazy 載入，導覽區塊要等頁面出現才有（CI 比本機慢，不能同步找）。
    await screen.findByRole('navigation', { name: '設定的區塊' });
    expect(await sectionTabs().findByRole('link', { name: '流程' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('heading', { level: 2, name: '流程' })).toBeInTheDocument();
    await router.navigate('/projects/SMK/settings', { state: { notice: '已套用變更集 #3，藍圖是第 2 版。' } });
    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('已套用變更集 #3，藍圖是第 2 版。');
    });
    expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
  });

  it.each([
    ['types', '類型', 'list', '類型'],
    ['fields', '欄位', 'list', '自訂欄位'],
    ['roles', '角色', 'list', '內建角色'],
  ] as const)('/settings/%s 只畫那個區塊', async (section, tab, role, name) => {
    // Act
    await openSettings(`/projects/SMK/settings/${section}`);

    // Assert
    expect(sectionTabs().getByRole('link', { name: tab })).toHaveAttribute('aria-current', 'page');
    expect(sectionTabs().getByRole('link', { name: '流程' })).not.toHaveAttribute('aria-current');
    expect(screen.getByRole(role, { name })).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: '「任務流程」的狀態' })).not.toBeInTheDocument();
  });

  it('不認得的區塊是找不到的頁面', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });

    // Act
    renderApp('/projects/SMK/settings/views');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '找不到這個頁面' })).toBeInTheDocument();
  });

  it('切換區塊時草稿還在、不問要不要離開；分頁帶著還沒送出的修改有幾項', async () => {
    // Arrange
    const { user, router } = await openSettings('/projects/SMK/settings/workflows');
    await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));

    // Act
    await user.click(sectionTabs().getByRole('link', { name: '欄位' }));
    await user.click(within(screen.getByRole('list', { name: '自訂欄位' })).getByRole('button', { name: '刪除「標籤」' }));
    await user.click(sectionTabs().getByRole('link', { name: '流程' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
    expect(screen.getByRole('list', { name: '「任務流程」要刪除的狀態' })).toBeInTheDocument();
    expect(screen.getByText('還沒送出的修改：2 項')).toBeInTheDocument();
    expect(sectionTabs().getByRole('link', { name: '流程' })).toHaveAccessibleDescription('1 項還沒送出的修改');
    expect(sectionTabs().getByRole('link', { name: '欄位' })).toHaveAccessibleDescription('1 項還沒送出的修改');
    expect(sectionTabs().getByRole('link', { name: '類型' })).not.toHaveAccessibleDescription();
  });

  it('連到設定頁本身（轉到預設的區塊）時草稿也還在', async () => {
    // Arrange
    const { user, router } = await openSettings('/projects/SMK/settings/fields');
    await user.click(screen.getByRole('button', { name: '刪除「標籤」' }));

    // Act
    await router.navigate('/projects/SMK/settings');

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
  });

  it('離開設定頁才問：留下來還在原本的區塊', async () => {
    // Arrange
    const { user, router } = await openSettings('/projects/SMK/settings/roles');
    await user.click(sectionTabs().getByRole('link', { name: '流程' }));
    await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));
    await user.click(sectionTabs().getByRole('link', { name: '類型' }));

    // Act
    await user.click(screen.getByRole('link', { name: '冒煙測試' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '還有沒送出的修改' });
    await user.click(within(dialog).getByRole('button', { name: '留下來' }));
    expect(currentPath(router)).toBe('/projects/SMK/settings/types');
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
  });

  it('問題在別的區塊時，分頁帶著問題的數量，送出列列出那些區塊並連過去', async () => {
    // Arrange
    const { user, proposals } = await openSettings('/projects/AGY/settings/fields', agyDetail());
    await user.clear(screen.getByRole('textbox', { name: '「報價金額」的名稱' }));
    await user.click(sectionTabs().getByRole('link', { name: '角色' }));
    await user.clear(screen.getByRole('textbox', { name: '角色「客戶」的名稱' }));
    await user.click(sectionTabs().getByRole('link', { name: '流程' }));

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('還有 2 個地方要修正，請看標示的地方。欄位 1 個、角色 1 個。');
    expect(proposals).toHaveLength(0);
    expect(sectionTabs().getByRole('link', { name: '欄位' })).toHaveAccessibleDescription('1 項還沒送出的修改、1 個要修正的問題');
    const elsewhere = within(screen.getByRole('region', { name: '送出修改' }));
    expect(elsewhere.getByText(/別的區塊也有要修正的地方/)).toBeInTheDocument();
    expect(elsewhere.getByRole('link', { name: '角色 1 個' })).toHaveAttribute('href', '/projects/AGY/settings/roles');
    await user.click(elsewhere.getByRole('link', { name: '欄位 1 個' }));
    expect(screen.getByRole('textbox', { name: '「報價金額」的名稱' })).toHaveAccessibleDescription('請輸入名稱。');
    expect(within(screen.getByRole('region', { name: '送出修改' })).queryByRole('link', { name: '欄位 1 個' })).not.toBeInTheDocument();
  });
});

describe('設定頁的區塊：分頁標題、焦點與對話框', () => {
  it('每個區塊的分頁標題不同，頁面的標題（h1）照舊是「設定」', async () => {
    // Arrange
    const { user } = await openSettings('/projects/SMK/settings/workflows');
    expect(document.title.startsWith('設定 · 流程 · ')).toBe(true);

    // Act
    await user.click(sectionTabs().getByRole('link', { name: '角色' }));

    // Assert
    expect(document.title.startsWith('設定 · 角色 · ')).toBe(true);
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(/^設定$/);
  });

  it('伺服器擋下時，說明裡也有每個區塊各幾個（對不到的算「其他問題」）', async () => {
    // Arrange
    const details = [
      { code: 'field.invalid', path: '/blueprint/fields/0/name', message: '欄位有問題。' },
      { code: 'role.invalid', path: '/blueprint/roles/1/name', message: '角色有問題。' },
      { code: 'automation.unknown_state', path: '/blueprint/automations/0/actions/0/to', message: '自動化有問題。' },
    ];
    const { user } = await openSettings('/projects/AGY/settings/workflows', agyDetail(), () => invalidChangeSet(details));
    await user.type(screen.getByRole('textbox', { name: '「報價」的名稱' }), '單');

    // Act
    await user.click(screen.getByRole('button', { name: '預覽變更' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(
      '伺服器檢查後擋下了這份變更（3 個問題），請看標示的地方。欄位 1 個、角色 1 個、其他問題 1 個。',
    );
  });

  it('按著修飾鍵點送出列的區塊連結不留下焦點的要求：之後自己換到那個區塊，焦點留在分頁上', async () => {
    // Arrange
    const { user } = await openSettings('/projects/AGY/settings/roles', agyDetail());
    await user.clear(screen.getByRole('textbox', { name: '角色「客戶」的名稱' }));
    await user.click(sectionTabs().getByRole('link', { name: '流程' }));
    await user.click(screen.getByRole('button', { name: '預覽變更' }));
    const link = within(screen.getByRole('region', { name: '送出修改' })).getByRole('link', { name: '角色 1 個' });

    // Act
    await user.keyboard('{Control>}');
    await user.click(link);
    await user.keyboard('{/Control}');
    await user.click(sectionTabs().getByRole('link', { name: '角色' }));

    // Assert
    expect(sectionTabs().getByRole('link', { name: '角色' })).toHaveFocus();
  });

  it('對話框開著時換區塊（例如上一頁）先問：留下來時對話框與打到一半的內容都還在；捨棄才換過去，焦點到新區塊的標題', async () => {
    // Arrange
    const { user, router } = await openSettings('/projects/SMK/settings/fields');
    await user.click(sectionTabs().getByRole('link', { name: '流程' }));
    await user.click(screen.getByRole('button', { name: '在「任務流程」新增狀態' }));
    await user.type(within(screen.getByRole('dialog', { name: '新增狀態' })).getByLabelText('名稱'), '審查中');

    // Act
    await act(async () => {
      await router.navigate(-1);
    });

    // Assert
    const confirm = await screen.findByRole('dialog', { name: '對話框裡的內容還沒加入' });
    await user.click(within(confirm).getByRole('button', { name: '留下來' }));
    expect(currentPath(router)).toBe('/projects/SMK/settings/workflows');
    const name = within(screen.getByRole('dialog', { name: '新增狀態' })).getByLabelText('名稱');
    expect(name).toHaveValue('審查中');
    expect(name).toHaveFocus();
    await act(async () => {
      await router.navigate(-1);
    });
    await user.click(within(await screen.findByRole('dialog', { name: '對話框裡的內容還沒加入' })).getByRole('button', { name: '捨棄並換過去' }));
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/settings/fields');
    });
    const heading = await screen.findByRole('heading', { level: 2, name: '欄位' });
    await waitFor(() => {
      expect(heading).toHaveFocus();
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('設定頁的送出列：窄螢幕', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('摘要收在「加上摘要」後面，打開之後焦點到摘要', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    const { user } = await openSettings('/projects/SMK/settings/workflows');
    await user.click(screen.getByRole('button', { name: '刪除「待辦」' }));
    expect(screen.queryByLabelText('摘要')).not.toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '加上摘要' }));

    // Assert
    expect(screen.getByLabelText('摘要')).toHaveFocus();
    expect(screen.queryByRole('button', { name: '加上摘要' })).not.toBeInTheDocument();
  });
});
