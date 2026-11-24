import { useEffect, useState } from 'react';
import { useCliCatalog, type CliPackage, type CliPlatform } from '../../api/cli';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import { PLATFORM_LABELS, defaultPlatform, detectIntelMac, guessPlatform, usablePackages } from './cliText';
import styles from './CliSection.module.css';

/** 瀏覽器猜的平台；Mac 先猜 Apple 晶片，拿得到 x86 的架構時再改成 Intel（拿不到就維持）。 */
function useGuessedPlatform(): CliPlatform | null {
  const [guess, setGuess] = useState(() => guessPlatform(window.navigator.userAgent));
  useEffect(() => {
    if (guess !== 'macos-arm64') return undefined;
    let isCurrent = true;
    void detectIntelMac(window.navigator).then((isIntel) => {
      if (isCurrent && isIntel) setGuess('macos-x64');
    });
    return () => {
      isCurrent = false;
    };
  }, [guess]);
  return guess;
}

/**
 * 下載哪一個 kumi：目錄、能用的檔案、使用者選的平台（沒選用猜的，猜的沒有用第一個）。
 * 「命令列與整合」頁在最上層呼叫一次，設定精靈的第一步與「命令列工具」拿同一份：選的平台兩邊同步（各自呼叫會各選各的）。在這一層開始猜：和讀目錄同時進行，目錄讀到時通常已經猜好，畫面不會先出現 Apple 晶片再跳成 Intel。
 * 檔名會放進要貼到終端機的指令，對不上規則的不列（usablePackages）。
 */
export function useDownloadChoice() {
  const catalog = useCliCatalog();
  const guess = useGuessedPlatform();
  const [chosen, setChosen] = useState<CliPlatform | null>(null);
  const data = catalog.data;
  const packages = data === undefined ? [] : usablePackages(data);
  // 選過的平台在重新讀取之後不在了，就回到預設的，不讓整塊變成「沒有提供下載」。
  const platform = packages.some((candidate) => candidate.platform === chosen) ? chosen : defaultPlatform(guess, packages);
  const pkg = packages.find((candidate) => candidate.platform === platform) ?? null;
  return { catalog, packages, pkg, version: data?.version ?? null, choose: setChosen };
}

export type DownloadChoice = ReturnType<typeof useDownloadChoice>;

interface PlatformPickerProps {
  /** 同一頁有兩組選擇，名稱要分得出來（報讀軟體的地標清單與測試都靠它）。 */
  readonly label: string;
  readonly packages: readonly CliPackage[];
  readonly value: CliPlatform;
  readonly onChange: (platform: CliPlatform) => void;
}

/** 選作業系統：390 寬放得下三個選項；320 寬放不下，那時整組換行，不撐寬頁面。 */
export function PlatformPicker({ label, packages, value, onChange }: PlatformPickerProps) {
  return (
    <div className={styles.platforms}>
      <SegmentedControl
        label={label}
        options={packages.map((candidate) => ({ value: candidate.platform, label: PLATFORM_LABELS[candidate.platform] }))}
        value={value}
        onChange={onChange}
      />
    </div>
  );
}
