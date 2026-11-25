import { cliDownloadPath, type CliPackage } from '../../api/cli';
import { buttonClassName } from '../../components/Button/Button';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { COMMAND_NAME, PRODUCT_NAME } from '../../config/product';
import { CopyBlock } from './ConnectAgentSection';
import {
  CHIP_HINT,
  CLI_NOTE,
  FOLDER_HINT,
  NO_DOWNLOAD_NOTE,
  PATH_HINT_MAC,
  PATH_HINT_WINDOWS,
  PLATFORM_LABELS,
  UPDATE_COMMAND,
  VERSION_COMMAND,
  downloadLabel,
  HASH_NOTE,
  installCommand,
  isMac,
  loginCommand,
  pasteLoginCommand,
  pathCommand,
} from './cliText';
import { PlatformPicker, type DownloadChoice } from './cliPlatform';
import accountStyles from './AccountPage.module.css';
import styles from './CliSection.module.css';

function Steps({ pkg, version }: { readonly pkg: CliPackage; readonly version: string }) {
  const platform = pkg.platform;
  return (
    <ol className={styles.steps} aria-label={`${PLATFORM_LABELS[platform]}的安裝步驟`}>
      <li>
        {/* 晶片的提示放在下載的前面：Safari、Firefox 猜不出 Intel 的 Mac，下載之前就要看到。 */}
        {isMac(platform) && <p className={accountStyles.note}>{CHIP_HINT}</p>}
        <p>
          <a className={buttonClassName('primary', 'md', styles.download)} href={cliDownloadPath(platform)} download={pkg.fileName}>
            {downloadLabel(version, pkg)}
          </a>
        </p>
      </li>
      <li>
        <p>{isMac(platform) ? '在終端機執行，把它放到固定的位置、改名成 ' : '在 PowerShell 執行，把它放到固定的位置、改名成 '}<code>{COMMAND_NAME}</code>：</p>
        <CopyBlock label="安裝的指令" text={installCommand(pkg)} wrap />
        <CopyBlock label="SHA-256" text={pkg.sha256} />
        <p className={accountStyles.note}>{HASH_NOTE}</p>
        <p className={accountStyles.note}>{FOLDER_HINT}</p>
      </li>
      <li>
        <p>讓終端機找得到它：</p>
        <CopyBlock label="加進 PATH 的指令" text={pathCommand(platform)} wrap />
        <p className={accountStyles.note}>{isMac(platform) ? PATH_HINT_MAC : PATH_HINT_WINDOWS}</p>
      </li>
      <li>
        <p>
          在新的視窗執行 <code>{VERSION_COMMAND}</code> 確認裝好了，再登入。瀏覽器會開 {PRODUCT_NAME} 的頁面，按「允許」：
        </p>
        <CopyBlock label="登入的指令" text={loginCommand(window.location.origin)} wrap />
        <p className={accountStyles.note}>
          沒辦法開瀏覽器（例如透過 SSH）時，到下面的「存取權杖」建一把，再執行 <code>{pasteLoginCommand(window.location.origin) ?? `${COMMAND_NAME} auth login --paste`}</code> 貼上權杖。
        </p>
        <p className={accountStyles.note}>
          之後有新版時執行 <code>{UPDATE_COMMAND}</code> 更新。
        </p>
      </li>
    </ol>
  );
}

/**
 * 命令列工具（internal/HANDOFF-cli-downloads.md 第 5 節）：從這個伺服器下載 kumi 的執行檔，照平台給安裝與加進 PATH 的指令。
 * 本機代理的第一步連到這一塊（id="cli"）。伺服器沒有提供下載時只有一行說明。
 */
export function CliSection({ choice }: { readonly choice: DownloadChoice }) {
  const { catalog, packages, pkg, version, choose } = choice;
  const data = catalog.data;
  return (
    <SettingsSection id="cli" title="命令列工具" description={CLI_NOTE}>
      {catalog.isPending && <Skeleton label="正在載入下載資訊…" rows={1} />}
      {catalog.isLoadingError && (
        <ErrorState
          error={catalog.error}
          onRetry={() => {
            void catalog.refetch();
          }}
        />
      )}
      <RefetchErrorNotice query={catalog} message="無法更新下載資訊，畫面上可能是舊的。" />
      {data !== undefined && (pkg === null || version === null) && <p className={accountStyles.note}>{NO_DOWNLOAD_NOTE}</p>}
      {pkg !== null && version !== null && (
        <>
          <PlatformPicker label="命令列工具的作業系統" packages={packages} value={pkg.platform} onChange={choose} />
          <Steps pkg={pkg} version={version} />
        </>
      )}
    </SettingsSection>
  );
}
