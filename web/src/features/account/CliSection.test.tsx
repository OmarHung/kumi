import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleCliCatalog, macArmPackage, serveCli, windowsPackage } from '../../test/cli';
import { exampleOwner } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';

const MAC_UA = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Safari/605.1.15';
const WINDOWS_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36';

afterEach(() => {
  Reflect.deleteProperty(window.navigator, 'userAgentData');
});

function useBrowser(userAgent: string, architecture?: string | Promise<{ architecture: string }>) {
  vi.spyOn(window.navigator, 'userAgent', 'get').mockReturnValue(userAgent);
  if (architecture === undefined) return undefined;
  const getHighEntropyValues = vi.fn(() => (typeof architecture === 'string' ? Promise.resolve({ architecture }) : architecture));
  Object.defineProperty(window.navigator, 'userAgentData', { configurable: true, value: { getHighEntropyValues } });
  return getHighEntropyValues;
}

async function openSection() {
  signedInAs(exampleOwner);
  const view = renderApp('/account/integrations');
  const section = await screen.findByRole('region', { name: '命令列工具' });
  return { ...view, section };
}

describe('命令列與整合：命令列工具的錨點', () => {
  it('這一塊是 id="cli" 的區塊（連結 /account/integrations#cli 捲到這裡）', async () => {
    // Arrange
    // Act
    const { section } = await openSection();

    // Assert
    expect(section).toHaveAttribute('id', 'cli');
  });
});

describe('我的帳號：命令列工具（預設的平台）', () => {
  it('Windows 的瀏覽器：選 Windows，下載連結與 PowerShell 指令', async () => {
    // Arrange
    useBrowser(WINDOWS_UA);

    // Act
    const { section } = await openSection();

    // Assert
    const group = await within(section).findByRole('group', { name: '命令列工具的作業系統' });
    expect(within(group).getByRole('button', { name: 'Windows' })).toHaveAttribute('aria-pressed', 'true');
    const link = within(section).getByRole('link', { name: '下載 kumi 0.3.0（37 MB）' });
    expect(link).toHaveAttribute('href', '/v1/cli/download/windows-x64');
    expect(link).toHaveAttribute('download', 'kumi-0.3.0-windows-x64.exe');
    expect(within(section).getByRole('region', { name: '安裝的指令' })).toHaveTextContent('Unblock-File');
    expect(within(section).queryByText(/蘋果選單/)).not.toBeInTheDocument();
  });

  it('Mac 的瀏覽器：預設 Apple 晶片，並說明怎麼看晶片', async () => {
    // Arrange
    useBrowser(MAC_UA);

    // Act
    const { section } = await openSection();

    // Assert
    const group = await within(section).findByRole('group', { name: '命令列工具的作業系統' });
    expect(within(group).getByRole('button', { name: 'Mac（Apple 晶片）' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(section).getByRole('region', { name: '安裝的指令' })).toHaveTextContent('kumi-0.3.0-macos-arm64');
    expect(within(section).getByText(/蘋果選單/)).toBeInTheDocument();
  });

  it('Mac 而且拿得到 x86 的架構：改選 Intel', async () => {
    // Arrange
    useBrowser(MAC_UA, 'x86');

    // Act
    const { section } = await openSection();

    // Assert
    const group = await within(section).findByRole('group', { name: '命令列工具的作業系統' });
    await waitFor(() => {
      expect(within(group).getByRole('button', { name: 'Mac（Intel）' })).toHaveAttribute('aria-pressed', 'true');
    });
    expect(within(section).getByRole('link', { name: /下載 kumi 0\.3\.0/ })).toHaveAttribute('href', '/v1/cli/download/macos-x64');
  });

  it('Mac 而且架構是 arm：維持 Apple 晶片', async () => {
    // Arrange
    const detect = useBrowser(MAC_UA, 'arm');

    // Act
    const { section } = await openSection();

    // Assert：等偵測真的回來之後再看，不然偵測壞了這個測試也會過。
    const group = await within(section).findByRole('group', { name: '命令列工具的作業系統' });
    await waitFor(() => {
      expect(detect).toHaveBeenCalled();
    });
    await Promise.resolve();
    expect(within(group).getByRole('button', { name: 'Mac（Apple 晶片）' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('使用者先選了別的平台，偵測才回來說是 Intel：維持使用者選的', async () => {
    // Arrange
    let answer: (value: { architecture: string }) => void = () => undefined;
    useBrowser(MAC_UA, new Promise((resolve) => (answer = resolve)));
    const { user, section } = await openSection();
    const group = await within(section).findByRole('group', { name: '命令列工具的作業系統' });
    await user.click(within(group).getByRole('button', { name: 'Windows' }));

    // Act
    answer({ architecture: 'x86' });

    // Assert
    await waitFor(() => {
      expect(within(group).getByRole('button', { name: 'Windows' })).toHaveAttribute('aria-pressed', 'true');
    });
    expect(within(group).getByRole('button', { name: 'Mac（Intel）' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('伺服器回的檔名不合規則：那個平台不列，不會放進要執行的指令', async () => {
    // Arrange
    useBrowser(MAC_UA);
    signedInAs(exampleOwner);
    serveCli(exampleCliCatalog({ packages: [{ ...macArmPackage, fileName: 'kumi-0.3.0-macos-arm64; rm -rf ~' }, windowsPackage] }));

    // Act
    renderApp('/account/integrations');

    // Assert
    const section = await screen.findByRole('region', { name: '命令列工具' });
    const group = await within(section).findByRole('group', { name: '命令列工具的作業系統' });
    expect(within(group).getAllByRole('button')).toHaveLength(1);
    expect(within(section).getByRole('region', { name: '安裝的指令' })).not.toHaveTextContent('rm -rf');
  });

  it('全部的檔名都不合規則：和沒有提供下載一樣', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveCli(exampleCliCatalog({ packages: [{ ...windowsPackage, fileName: '$(id).exe' }] }));

    // Act
    renderApp('/account/integrations');

    // Assert
    const section = await screen.findByRole('region', { name: '命令列工具' });
    expect(await within(section).findByText(/沒有提供命令列工具的下載/)).toBeInTheDocument();
    expect(within(section).queryByRole('group', { name: '命令列工具的作業系統' })).not.toBeInTheDocument();
  });

  it('只列伺服器有的平台；猜的平台沒有就選第一個', async () => {
    // Arrange
    useBrowser(MAC_UA);
    signedInAs(exampleOwner);
    serveCli(exampleCliCatalog({ packages: [windowsPackage] }));

    // Act
    renderApp('/account/integrations');

    // Assert
    const section = await screen.findByRole('region', { name: '命令列工具' });
    const group = await within(section).findByRole('group', { name: '命令列工具的作業系統' });
    expect(within(group).getAllByRole('button')).toHaveLength(1);
    expect(within(group).getByRole('button', { name: 'Windows' })).toHaveAttribute('aria-pressed', 'true');
  });
});

describe('我的帳號：命令列工具（切換與步驟）', () => {
  it('切換平台之後下載連結與指令跟著換', async () => {
    // Arrange
    useBrowser(WINDOWS_UA);
    const { user, section } = await openSection();
    const group = await within(section).findByRole('group', { name: '命令列工具的作業系統' });

    // Act
    await user.click(within(group).getByRole('button', { name: 'Mac（Intel）' }));

    // Assert
    expect(within(section).getByRole('list', { name: 'Mac（Intel）的安裝步驟' })).toBeInTheDocument();
    expect(within(section).getByRole('link', { name: /下載 kumi 0\.3\.0/ })).toHaveAttribute('href', '/v1/cli/download/macos-x64');
    expect(within(section).getByRole('region', { name: '安裝的指令' })).toHaveTextContent('mv ~/Downloads/kumi-0.3.0-macos-x64 ~/.local/bin/kumi');
    expect(within(section).getByRole('region', { name: '加進 PATH 的指令' })).toHaveTextContent('~/.zprofile');
    expect(within(section).getByText(/蘋果選單/)).toBeInTheDocument();
  });

  it('後面的步驟：確認版本、建權杖、登入、更新', async () => {
    // Arrange
    useBrowser(MAC_UA);

    // Act
    const { section } = await openSection();

    // Assert
    const steps = await within(section).findByRole('list', { name: 'Mac（Apple 晶片）的安裝步驟' });
    expect(within(steps).getAllByRole('listitem')).toHaveLength(4);
    expect(within(steps).getByText(/kumi --version/)).toBeInTheDocument();
    expect(within(steps).getByText(/存取權杖/)).toBeInTheDocument();
    expect(within(steps).getByRole('region', { name: '登入的指令' })).toHaveTextContent(`kumi auth login --server ${window.location.origin}`);
    expect(within(steps).getByText(/kumi update/)).toBeInTheDocument();
  });
});

describe('我的帳號：命令列工具（沒有下載與讀取失敗）', () => {
  it('伺服器沒有提供下載：一行說明，指到 docs/CLI.md', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveCli({ version: null, packages: [] });

    // Act
    renderApp('/account/integrations');

    // Assert
    const section = await screen.findByRole('region', { name: '命令列工具' });
    expect(await within(section).findByText(/沒有提供命令列工具的下載/)).toHaveTextContent('docs/CLI.md');
    expect(within(section).queryByRole('group', { name: '命令列工具的作業系統' })).not.toBeInTheDocument();
    expect(within(section).queryByRole('link')).not.toBeInTheDocument();
  });

  it('讀取失敗：顯示錯誤並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveCli(exampleCliCatalog(), true);

    // Act
    renderApp('/account/integrations');

    // Assert
    const section = await screen.findByRole('region', { name: '命令列工具' });
    expect(await within(section).findByRole('button', { name: '重試' })).toBeInTheDocument();
    expect(within(section).queryByRole('group', { name: '命令列工具的作業系統' })).not.toBeInTheDocument();
  });
});
