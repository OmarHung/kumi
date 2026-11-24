import type { CliCatalog, CliPackage, CliPlatform } from '../../api/cli';
import { COMMAND_NAME } from '../../config/product';

/** 選平台的標籤（internal/HANDOFF-cli-downloads.md 第 5 節）。 */
export const PLATFORM_LABELS: Readonly<Record<CliPlatform, string>> = {
  'macos-arm64': 'Mac（Apple 晶片）',
  'macos-x64': 'Mac（Intel）',
  'windows-x64': 'Windows',
};

export const CLI_NOTE = `下載單一的執行檔，不必先裝 .NET；裝好之後用 ${COMMAND_NAME} update 更新。`;

export const CHIP_HINT = '不確定晶片：蘋果選單 → 關於這台 Mac，「晶片」寫 Apple M… 的就是 Apple 晶片，寫 Intel 的選 Intel。';

export const PATH_HINT_MAC = '第一次安裝才要加進 PATH，之後的更新不必。執行完開一個新的終端機視窗。';
export const PATH_HINT_WINDOWS = '第一次安裝才要加進 PATH，之後的更新不必。執行完開一個新的 PowerShell 視窗。';

export const FOLDER_HINT =
  '瀏覽器存到別的資料夾的話，把指令裡下載資料夾的路徑換掉。下載資料夾裡已經有同名的檔案時，瀏覽器會在檔名後面加上 (1)：先刪掉舊的再下載一次。';

export const NO_DOWNLOAD_NOTE = '這個伺服器沒有提供命令列工具的下載。可以改用 dotnet tool 安裝，步驟見專案的 docs/CLI.md。';

const MEBIBYTE = 1024 * 1024;

export function isMac(platform: CliPlatform): boolean {
  return platform !== 'windows-x64';
}

/** 「36 MB」：四捨五入到整數，檔案都是幾十 MB，不需要小數。 */
export function sizeText(bytes: number): string {
  return `${String(Math.max(1, Math.round(bytes / MEBIBYTE)))} MB`;
}

/** 下載連結上的字，例如「下載 kumi 0.3.0（36 MB）」。 */
export function downloadLabel(version: string, pkg: CliPackage): string {
  return `下載 ${COMMAND_NAME} ${version}（${sizeText(pkg.sizeBytes)}）`;
}

const VERSION_PATTERN = /^\d+\.\d+\.\d+$/;

/** 伺服器照這個規則命名（internal/HANDOFF-cli-downloads.md 第 1 節）。 */
export function expectedFileName(version: string, platform: CliPlatform): string {
  return `${COMMAND_NAME}-${version}-${platform}${isMac(platform) ? '' : '.exe'}`;
}

/**
 * 能給使用者下載、照著指令裝的平台：網頁認得的平台，而且檔名剛好是規則算出來的那一個。
 * 檔名會原樣放進要貼到終端機執行的指令，所以不盲信伺服器：對不上的整個不列（不會變成命令注入）。
 */
export function usablePackages(catalog: CliCatalog): readonly CliPackage[] {
  const version = catalog.version;
  if (version === null || !VERSION_PATTERN.test(version)) return [];
  return catalog.packages.filter(
    (pkg) => Object.hasOwn(PLATFORM_LABELS, pkg.platform) && pkg.fileName === expectedFileName(version, pkg.platform),
  );
}

const MAC_PATH_LINE = 'export PATH="$HOME/.local/bin:$PATH"';
const WINDOWS_DIR = '$env:LOCALAPPDATA\\Programs\\Kumi';

/**
 * 把下載來的檔案放到固定的位置、改名成 kumi。macOS 的 xattr -c 清掉瀏覽器加的隔離標記，不然系統會直接擋掉。
 * Windows 的下載資料夾向系統問（被 OneDrive 或使用者搬走時不是 $HOME\Downloads），問不到才用 $HOME\Downloads；
 * 搬檔失敗時停下（-ErrorAction Stop），不接著對不存在的檔案解除封鎖、噴一串看不懂的錯。
 */
export function installCommand(pkg: CliPackage): string {
  if (isMac(pkg.platform)) {
    const target = `~/.local/bin/${COMMAND_NAME}`;
    return `mkdir -p ~/.local/bin && mv ~/Downloads/${pkg.fileName} ${target} && chmod 755 ${target} && xattr -c ${target}`;
  }
  const exe = `"$d\\${COMMAND_NAME}.exe"`;
  return [
    `$dl = (New-Object -ComObject Shell.Application).NameSpace('shell:Downloads').Self.Path`,
    `if (-not $dl) { $dl = "$HOME\\Downloads" }`,
    `$d = "${WINDOWS_DIR}"`,
    'New-Item -ItemType Directory -Force $d | Out-Null',
    `Move-Item -Force -ErrorAction Stop (Join-Path $dl '${pkg.fileName}') ${exe}`,
    `Unblock-File ${exe}`,
  ].join('; ');
}

/**
 * 加進 PATH，重複執行也不會重複加。macOS 比對完整的那一行（只出現 .local/bin 的別的寫法不算），前面先補一個換行，
 * 檔案最後沒有換行時才不會接在上一行後面。Windows 把使用者的 Path 拆開逐項比（不用萬用字元比對，路徑裡的 [ ] 不會誤判），
 * 去掉空的項目，原本是空的時也不會多一個開頭的分號。
 */
export function pathCommand(platform: CliPlatform): string {
  if (isMac(platform)) {
    return `grep -qsxF '${MAC_PATH_LINE}' ~/.zprofile || printf '\\n${MAC_PATH_LINE}\\n' >> ~/.zprofile`;
  }
  return [
    `$d = "${WINDOWS_DIR}"`,
    "$p = @([Environment]::GetEnvironmentVariable('Path', 'User') -split ';' | Where-Object { $_ })",
    "if ($p -notcontains $d) { [Environment]::SetEnvironmentVariable('Path', ((@($p) + $d) -join ';'), 'User') }",
  ].join('; ');
}

export const VERSION_COMMAND = `${COMMAND_NAME} --version`;
export const UPDATE_COMMAND = `${COMMAND_NAME} update`;

/** 瀏覽器登入：終端機裡 kumi 開瀏覽器請使用者按「允許」，不必複製權杖。 */
export function loginCommand(origin: string): string {
  return `${COMMAND_NAME} auth login --server ${origin}`;
}

/** 沒辦法開瀏覽器（透過 SSH）時：貼上網頁「存取權杖」建的權杖。 */
export function pasteLoginCommand(origin: string): string {
  return `${loginCommand(origin)} --paste`;
}

/** 瀏覽器猜的平台：Windows 選 Windows，Mac 選 Apple 晶片（猜不出晶片時），其他（Linux、手機）不猜。iPhone 的 userAgent 也寫「like Mac OS X」，先排除。 */
export function guessPlatform(userAgent: string): CliPlatform | null {
  if (/iPhone|iPad|iPod|Android/i.test(userAgent)) return null;
  if (/Windows/i.test(userAgent)) return 'windows-x64';
  if (/Macintosh|Mac OS X/i.test(userAgent)) return 'macos-arm64';
  return null;
}

interface UserAgentDataLike {
  readonly getHighEntropyValues?: (hints: readonly string[]) => Promise<{ readonly architecture?: string }>;
}

/** 拿得到架構、而且是 x86 就是 Intel 的 Mac；拿不到（Safari、Firefox 沒有這個 API）或出錯都算了。 */
export async function detectIntelMac(nav: Navigator): Promise<boolean> {
  const data = (nav as Navigator & { readonly userAgentData?: UserAgentDataLike }).userAgentData;
  if (typeof data?.getHighEntropyValues !== 'function') return false;
  try {
    const values = await data.getHighEntropyValues(['architecture']);
    return values.architecture === 'x86';
  } catch {
    return false;
  }
}

/** 從伺服器有的平台裡挑預設的：猜的有就用它，沒有就第一個。 */
export function defaultPlatform(guess: CliPlatform | null, available: readonly CliPackage[]): CliPlatform | null {
  if (guess !== null && available.some((pkg) => pkg.platform === guess)) return guess;
  return available[0]?.platform ?? null;
}
