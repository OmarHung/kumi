import { describe, expect, it } from 'vitest';
import { exampleCliPackages, macArmPackage as ARM, macIntelPackage as INTEL, windowsPackage as WINDOWS } from '../../test/cli';
import {
  defaultPlatform,
  detectIntelMac,
  downloadLabel,
  guessPlatform,
  installCommand,
  pathCommand,
  sizeText,
  usablePackages,
} from './cliText';

describe('命令列工具的指令字串', () => {
  it('macOS（Apple 晶片）的安裝指令用下載的檔名，並清掉隔離標記', () => {
    // Arrange
    // Act
    const command = installCommand(ARM);

    // Assert
    expect(command).toBe(
      'mkdir -p ~/.local/bin && mv ~/Downloads/kumi-0.3.0-macos-arm64 ~/.local/bin/kumi && chmod 755 ~/.local/bin/kumi && xattr -c ~/.local/bin/kumi',
    );
  });

  it('macOS（Intel）的安裝指令用 x64 的檔名', () => {
    expect(installCommand(INTEL)).toContain('~/Downloads/kumi-0.3.0-macos-x64 ');
  });

  it('Windows 的安裝指令向系統問下載資料夾、搬檔失敗就停、把 exe 放到使用者的程式資料夾並解除封鎖', () => {
    // Arrange
    // Act
    const command = installCommand(WINDOWS);

    // Assert
    expect(command).toBe(
      [
        "$dl = (New-Object -ComObject Shell.Application).NameSpace('shell:Downloads').Self.Path",
        'if (-not $dl) { $dl = "$HOME\\Downloads" }',
        '$d = "$env:LOCALAPPDATA\\Programs\\Kumi"',
        'New-Item -ItemType Directory -Force $d | Out-Null',
        "Move-Item -Force -ErrorAction Stop (Join-Path $dl 'kumi-0.3.0-windows-x64.exe') \"$d\\kumi.exe\"",
        'Unblock-File "$d\\kumi.exe"',
      ].join('; '),
    );
  });

  it('PATH 指令：macOS 比對完整的那一行、前面補換行；Windows 逐項比對、去掉空的項目', () => {
    expect(pathCommand('macos-arm64')).toBe(
      `grep -qsxF 'export PATH="$HOME/.local/bin:$PATH"' ~/.zprofile || printf '\\nexport PATH="$HOME/.local/bin:$PATH"\\n' >> ~/.zprofile`,
    );
    expect(pathCommand('macos-x64')).toBe(pathCommand('macos-arm64'));
    const windows = pathCommand('windows-x64');
    expect(windows).toContain("-split ';' | Where-Object { $_ }");
    expect(windows).toContain('if ($p -notcontains $d)');
    expect(windows).not.toContain('-notlike');
  });

  it('下載連結的字帶版本與大小', () => {
    expect(downloadLabel('0.3.0', ARM)).toBe('下載 kumi 0.3.0（36 MB）');
    expect(sizeText(1)).toBe('1 MB');
  });
});

describe('只列檔名對得上規則的平台', () => {
  it('三個平台的檔名都對：全部列出', () => {
    expect(usablePackages({ version: '0.3.0', packages: exampleCliPackages })).toEqual(exampleCliPackages);
  });

  it.each([
    ['帶分號的指令', 'kumi-0.3.0-macos-arm64; rm -rf ~'],
    ['命令替換', '$(id)'],
    ['空白', 'kumi 0.3.0 macos-arm64'],
    ['版本和目錄不同', 'kumi-0.2.0-macos-arm64'],
    ['平台和代碼不同', 'kumi-0.3.0-macos-x64'],
  ])('檔名不對（%s）：那個平台不列，也就不會放進指令', (_case, fileName) => {
    // Arrange
    const catalog = { version: '0.3.0', packages: [{ ...ARM, fileName }, WINDOWS] };

    // Act
    const usable = usablePackages(catalog);

    // Assert
    expect(usable).toEqual([WINDOWS]);
  });

  it('Windows 的檔名少了 .exe、或版本不是三段數字：不列', () => {
    expect(usablePackages({ version: '0.3.0', packages: [{ ...WINDOWS, fileName: 'kumi-0.3.0-windows-x64' }] })).toEqual([]);
    expect(usablePackages({ version: '0.3.0; x', packages: exampleCliPackages })).toEqual([]);
    expect(usablePackages({ version: null, packages: exampleCliPackages })).toEqual([]);
  });

  it('網頁不認得的平台：不列', () => {
    const linux = { ...ARM, platform: 'linux-x64' as unknown as typeof ARM.platform, fileName: 'kumi-0.3.0-linux-x64' };
    expect(usablePackages({ version: '0.3.0', packages: [linux, WINDOWS] })).toEqual([WINDOWS]);
  });
});

describe('預設的平台', () => {
  it('依 userAgent 猜：Windows、Mac、其他不猜', () => {
    expect(guessPlatform('Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130')).toBe('windows-x64');
    expect(guessPlatform('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Safari/605')).toBe('macos-arm64');
    expect(guessPlatform('Mozilla/5.0 (X11; Linux x86_64)')).toBeNull();
  });

  it('手機不猜：iPhone 的 userAgent 也寫 Mac OS X，Android 也是 Linux', () => {
    expect(guessPlatform('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Safari/604')).toBeNull();
    expect(guessPlatform('Mozilla/5.0 (Linux; Android 15; Pixel 9) Chrome/130 Mobile')).toBeNull();
  });

  it('猜的平台伺服器沒有時，用第一個有的', () => {
    expect(defaultPlatform('macos-arm64', [WINDOWS])).toBe('windows-x64');
    expect(defaultPlatform(null, exampleCliPackages)).toBe('macos-arm64');
    expect(defaultPlatform('windows-x64', [])).toBeNull();
  });

  it('架構：x86 是 Intel；拿不到、被拒絕或是其他架構都不是', async () => {
    const nav = (userAgentData: unknown) => ({ userAgentData }) as unknown as Navigator;
    expect(await detectIntelMac(nav({ getHighEntropyValues: () => Promise.resolve({ architecture: 'x86' }) }))).toBe(true);
    expect(await detectIntelMac(nav({ getHighEntropyValues: () => Promise.resolve({ architecture: 'arm' }) }))).toBe(false);
    expect(await detectIntelMac(nav({ getHighEntropyValues: () => Promise.reject(new Error('no')) }))).toBe(false);
    expect(await detectIntelMac(nav(undefined))).toBe(false);
  });
});
