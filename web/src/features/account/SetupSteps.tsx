import { useEffect, useRef, useState, type ReactNode } from 'react';
import { cliDownloadPath, type CliPackage } from '../../api/cli';
import { Button, buttonClassName } from '../../components/Button/Button';
import { Icon, type IconName } from '../../components/Icon/Icon';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import { COMMAND_NAME, PRODUCT_NAME } from '../../config/product';
import { PlatformPicker, type DownloadChoice } from './cliPlatform';
import { CHIP_HINT, HASH_NOTE, NO_DOWNLOAD_NOTE, downloadLabel, isMac } from './cliText';
import { CopyBlock } from './ConnectAgentSection';
import { LocalAgentOfflineHelp } from './LocalAgentOfflineHelp';
import { COMMAND_NOTE, STATUS_NOTE, oneLineCommand, setupCommand } from './setupText';
import type { StatusKind, StatusLine } from './setupStatus';
import { useSetupStatus } from './useSetupStatus';
import accountStyles from './AccountPage.module.css';
import styles from './SetupWizard.module.css';

interface DownloadStepProps {
  readonly choice: DownloadChoice;
  readonly onDownloaded: (pkg: CliPackage, version: string) => void;
  readonly onAlreadyInstalled: () => void;
}

/** 第一步：選平台、下載；已經裝好的直接跳過（按下載或「已經裝好」就算這一步做了）。 */
export function DownloadStep({ choice, onDownloaded, onAlreadyInstalled }: DownloadStepProps) {
  const { catalog, packages, pkg, version, choose } = choice;
  return (
    <>
      {catalog.isPending && <Skeleton label="正在載入下載資訊…" rows={1} />}
      {catalog.isLoadingError && (
        <ErrorState
          error={catalog.error}
          onRetry={() => {
            void catalog.refetch();
          }}
        />
      )}
      {catalog.isSuccess && (pkg === null || version === null) && <p className={accountStyles.note}>{NO_DOWNLOAD_NOTE}</p>}
      {pkg !== null && version !== null && (
        <>
          <PlatformPicker label="設定精靈的作業系統" packages={packages} value={pkg.platform} onChange={choose} />
          {/* 晶片的提示放在下載的前面：Safari、Firefox 猜不出 Intel 的 Mac，下載之前就要看到。 */}
          {isMac(pkg.platform) && <p className={accountStyles.note}>{CHIP_HINT}</p>}
          <CopyBlock label="SHA-256" text={pkg.sha256} />
          <p className={accountStyles.note}>{HASH_NOTE}</p>
        </>
      )}
      <div className={styles.actions}>
        {pkg !== null && version !== null && (
          <a
            className={buttonClassName('primary', 'md', styles.download)}
            href={cliDownloadPath(pkg.platform)}
            download={pkg.fileName}
            onClick={() => {
              onDownloaded(pkg, version);
            }}
          >
            {downloadLabel(version, pkg)}
          </a>
        )}
        <Button onClick={onAlreadyInstalled}>已經裝好 {COMMAND_NAME} 了</Button>
      </div>
    </>
  );
}

interface CommandStepProps {
  readonly pkg: CliPackage | null;
  readonly isInstalled: boolean;
  readonly onDone: (summary: string) => void;
}

function terminalHint(pkg: CliPackage | null): string {
  if (pkg === null) return '在終端機貼上，按 Enter：';
  return isMac(pkg.platform) ? '開「終端機」，貼上，按 Enter：' : '開 PowerShell，貼上，按 Enter：';
}

/**
 * 第二步：一行做完安裝、加進 PATH、啟動精靈。已經裝好、或伺服器沒有提供下載（沒有檔案可裝）時只給啟動精靈的那一行。
 * 按「複製」或「下一步」就算這一步做了：貼進哪個視窗我們看不到。
 */
export function CommandStep({ pkg, isInstalled, onDone }: CommandStepProps) {
  const origin = window.location.origin;
  const isFull = pkg !== null && !isInstalled;
  return (
    <>
      <p>{terminalHint(pkg)}</p>
      <CopyBlock
        label={isFull ? '安裝並啟動設定精靈的指令' : '啟動設定精靈的指令'}
        text={isFull ? oneLineCommand(pkg, origin) : setupCommand(origin)}
        wrap
        onCopy={() => {
          onDone('已複製指令');
        }}
      />
      <p className={accountStyles.note}>{COMMAND_NOTE}</p>
      {isFull && (
        <>
          <p className={accountStyles.note}>已經裝好 {COMMAND_NAME} 的話，只要執行：</p>
          <CopyBlock
            label="啟動設定精靈的指令"
            text={setupCommand(origin)}
            wrap
            onCopy={() => {
              onDone('已複製指令');
            }}
          />
        </>
      )}
      <div className={styles.actions}>
        <Button
          onClick={() => {
            onDone('已執行指令');
          }}
        >
          下一步
        </Button>
      </div>
    </>
  );
}

const LINE_ICONS: Readonly<Record<StatusKind, IconName>> = {
  done: 'success',
  todo: 'stateNotStarted',
  muted: 'stateCanceled',
  pending: 'stateBacklog',
  error: 'error',
};

interface StatusRowProps {
  readonly label: string;
  readonly kind: StatusKind;
  readonly text: string;
  /** 結果後面接的按鈕（本機代理離線時的「排查說明」）。 */
  readonly action?: ReactNode;
}

function StatusRow({ label, kind, text, action }: StatusRowProps) {
  return (
    <div className={styles.line} data-kind={kind}>
      <dt className={styles.lineLabel}>
        <span className={styles.lineIcon} aria-hidden="true">
          <Icon name={LINE_ICONS[kind]} />
        </span>
        {label}
      </dt>
      <dd className={styles.lineText}>
        {text}
        {action}
      </dd>
    </div>
  );
}

const AGENTS_LABEL = `在 AI 工具裡用 ${PRODUCT_NAME}`;

interface StatusEntry {
  readonly label: string;
  readonly line: StatusLine;
}

/** 「已登入：MacBook-Pro」唸成「命令列工具：已登入 MacBook-Pro」。 */
function announcement({ label, line }: StatusEntry): string {
  return `${label}：${line.text.replace('：', ' ')}`;
}

/**
 * 第三步有哪一項從「還沒有」變成完成，就寫一句給報讀軟體。只報這一種變化：讀取中、讀不到不報，
 * 進頁時就已經完成的（前一個狀態是讀取中，不是「還沒有」）也不報。用「渲染時比對上一次」而不是 effect：
 * 狀態一到手就知道要報什麼，不必多畫一次。
 */
function useCompletionAnnouncement(entries: readonly StatusEntry[]): string {
  const kinds = entries.map((entry) => entry.line.kind).join();
  const [seen, setSeen] = useState({ kinds, message: '' });
  if (seen.kinds !== kinds) {
    const before = seen.kinds.split(',');
    const finished = entries.filter((entry, index) => before[index] === 'todo' && entry.line.kind === 'done');
    setSeen({ kinds, message: finished.length === 0 ? seen.message : finished.map(announcement).join('；') });
  }
  return seen.message;
}

/**
 * 本機代理離線時的「排查說明」對話框的開關。開著時代理上線，按鈕會不見、對話框照樣留著；
 * 關上時按鈕已經不在，焦點交給這一步的標題（不然 ModalFrame 還給已卸下的按鈕，焦點掉到 body）。
 * 在 effect 裡放：要等對話框卸下、ModalFrame 還完焦點之後才輪到這裡。
 */
function useOfflineHelp(isOffline: boolean) {
  const [isOpen, setIsOpen] = useState(false);
  const anchorRef = useRef<HTMLDListElement>(null);
  const shouldFocusHeading = useRef(false);
  useEffect(() => {
    if (isOpen || !shouldFocusHeading.current) return;
    shouldFocusHeading.current = false;
    anchorRef.current?.closest('li')?.querySelector<HTMLElement>('h3')?.focus();
  }, [isOpen]);
  return {
    isOpen,
    anchorRef,
    open: () => {
      setIsOpen(true);
    },
    close: () => {
      shouldFocusHeading.current = !isOffline;
      setIsOpen(false);
    },
  };
}

/** 第三步：即時看完成了哪幾項（只有畫出這一步時才每 5 秒讀；分頁看不見時不讀）。 */
export function StatusStep() {
  const status = useSetupStatus();
  const isOffline = status.localAgent.isOffline === true;
  const { isOpen: isHelpOpen, anchorRef, open: openHelp, close: closeHelp } = useOfflineHelp(isOffline);
  const helpButton = (
    <Button size="sm" variant="ghost" onClick={openHelp}>
      <Icon name="info" />
      排查說明
    </Button>
  );
  const entries: readonly StatusEntry[] = [
    { label: '命令列工具', line: status.cli },
    // 兩項都有 Claude Code／Codex、方向相反：標籤寫出方向（在它們裡面用 Kumi／網頁的 AI 助理用它們）。
    { label: AGENTS_LABEL, line: status.agents },
    { label: '本機代理（AI 助理用你的電腦）', line: status.localAgent },
  ];
  const message = useCompletionAnnouncement(entries);
  return (
    <>
      <dl ref={anchorRef} className={styles.status}>
        {entries.map(({ label, line }) => (
          <StatusRow
            key={label}
            label={label}
            kind={line.kind}
            text={line.text}
            action={line.isOffline === true ? helpButton : undefined}
          />
        ))}
      </dl>
      {/* 永遠掛著：live region 要先在畫面上，之後寫進去的字才會被唸出來。 */}
      <p className="visually-hidden" role="status">
        {message}
      </p>
      <p className={accountStyles.note}>{STATUS_NOTE}</p>
      {isHelpOpen && <LocalAgentOfflineHelp isOnline={!isOffline} onClose={closeHelp} />}
    </>
  );
}
