import { useEffect, useState, type ReactNode } from 'react';
import { useAccessTokens } from '../../api/queries';
import type { CliPackage } from '../../api/cli';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { COMMAND_NAME } from '../../config/product';
import type { DownloadChoice } from './cliPlatform';
import { PLATFORM_LABELS } from './cliText';
import { CommandStep, DownloadStep, StatusStep } from './SetupSteps';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { cliLine } from './setupStatus';
import { SetupStepFrame, type StepState } from './SetupStepFrame';
import { STEP_TITLES, WIZARD_NOTE } from './setupText';
import styles from './SetupWizard.module.css';

type StepId = 1 | 2 | 3;

interface Progress {
  /** 做完的步驟與它的一行摘要（只有 1、2；第三步看的是即時狀態，沒有「做完」）。 */
  readonly summaries: Readonly<Partial<Record<1 | 2, string>>>;
  /** 第一步是「已經裝好」：第二步只給啟動精靈的那一行。 */
  readonly isInstalled: boolean;
  /** 被點開重看的完成步驟。 */
  readonly reopened: readonly StepId[];
  /** 剛輪到、焦點要放過去的步驟。 */
  readonly focused: StepId | null;
}

const INITIAL: Progress = {
  summaries: {},
  isInstalled: false,
  reopened: [],
  focused: null,
};

/** 命令列已經登入過：前兩步算做完，直接到第三步看狀態。 */
const RETURNING: Progress = {
  summaries: { 1: `已經裝好 ${COMMAND_NAME}`, 2: '已執行過' },
  isInstalled: true,
  reopened: [],
  focused: null,
};

/** 讀存取權杖清單最多等這麼久；超過就照第一步開始（之後讀到也不改，不讓畫面在使用者手下變動）。 */
export const PROGRESS_WAIT_MS = 1500;

function currentStep(summaries: Progress['summaries']): StepId {
  if (summaries[1] === undefined) return 1;
  return summaries[2] === undefined ? 2 : 3;
}

function stateOf(step: StepId, current: StepId): StepState {
  if (step === current) return 'current';
  return step < current ? 'done' : 'upcoming';
}

/**
 * 步驟的進度與動作：只存在這一頁的記憶體裡。重新整理之後不從頭來：進頁時讀一次存取權杖清單（只讀一次，不輪詢），
 * 命令列已經登入過的人（有電腦名稱的權杖）前兩步算做完、直接看第三步；要設定另一台電腦按第一步的「重看」。
 * 決定之前（null）整塊是骨架：先畫第一步再跳到第三步會推動版面，而且可能在使用者已經開始操作時改掉他的進度。
 */
function useProgress() {
  const tokens = useAccessTokens();
  const [progress, setProgress] = useState<Progress | null>(null);
  const [hasWaitedTooLong, setHasWaitedTooLong] = useState(false);
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setHasWaitedTooLong(true);
    }, PROGRESS_WAIT_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, []);
  const isSettled = tokens.isSuccess || tokens.isError || hasWaitedTooLong;
  if (progress === null && isSettled) {
    setProgress(tokens.isSuccess && cliLine(tokens.data, tokens.dataUpdatedAt).kind === 'done' ? RETURNING : INITIAL);
  }
  const shown = progress ?? INITIAL;
  const current = currentStep(shown.summaries);
  function update(change: (previous: Progress) => Progress) {
    setProgress((previous) => change(previous ?? INITIAL));
  }
  function complete(step: 1 | 2, summary: string, patch: Partial<Progress> = {}) {
    update((previous) => {
      const summaries = { ...previous.summaries, [step]: summary };
      const wasCurrent = currentStep(previous.summaries) === step;
      return {
        ...previous,
        ...patch,
        summaries,
        // 重看時再按一次（例如換了平台又下載）只更新摘要，不搶走焦點。
        focused: wasCurrent ? currentStep(summaries) : previous.focused,
        reopened: previous.reopened.filter((id) => id !== step),
      };
    });
  }
  function toggle(step: StepId) {
    update((previous) => ({
      ...previous,
      focused: null,
      reopened: previous.reopened.includes(step) ? previous.reopened.filter((id) => id !== step) : [...previous.reopened, step],
    }));
  }
  return {
    progress: shown,
    isSettled: progress !== null,
    current,
    complete,
    toggle,
  };
}

interface StepProps {
  readonly step: StepId;
  readonly title: string;
  readonly progress: Progress;
  readonly current: StepId;
  readonly onToggle: (step: StepId) => void;
  readonly children: ReactNode;
}

function Step({ step, title, progress, current, onToggle, children }: StepProps) {
  return (
    <SetupStepFrame
      number={step}
      title={title}
      state={stateOf(step, current)}
      summary={step === 3 ? undefined : progress.summaries[step]}
      isReopened={progress.reopened.includes(step)}
      onToggle={() => {
        onToggle(step);
      }}
      shouldFocus={progress.focused === step}
    >
      {children}
    </SetupStepFrame>
  );
}

/**
 * 命令列與整合最上方的設定精靈（internal/HANDOFF-setup-wizard.md 5.2）：三步由上往下，前一步完成才展開下一步。
 * 這個畫面只負責「把那一行指令交給使用者」，登入與設定都在終端機裡做；第三步用即時的清單看結果。
 */
export function SetupWizard({ choice }: { readonly choice: DownloadChoice }) {
  const { progress, isSettled, current, complete, toggle } = useProgress();
  const shared = { progress, current, onToggle: toggle };
  return (
    <SettingsSection id="setup" title="設定精靈" description={WIZARD_NOTE} isContentStart>
      {!isSettled && <Skeleton label="正在確認設定的進度…" rows={3} />}
      {isSettled && (
        <ol className={styles.steps} aria-label="設定的步驟">
          <Step step={1} title={STEP_TITLES.download} {...shared}>
            <DownloadStep
              choice={choice}
              onDownloaded={(pkg: CliPackage, version: string) => {
                complete(1, `已下載 ${COMMAND_NAME} ${version}（${PLATFORM_LABELS[pkg.platform]}）`, { isInstalled: false });
              }}
              onAlreadyInstalled={() => {
                complete(1, `已經裝好 ${COMMAND_NAME}`, { isInstalled: true });
              }}
            />
          </Step>
          <Step step={2} title={STEP_TITLES.command} {...shared}>
            <CommandStep
              pkg={choice.pkg}
              isInstalled={progress.isInstalled}
              onDone={(summary) => {
                complete(2, summary);
              }}
            />
          </Step>
          <Step step={3} title={STEP_TITLES.status} {...shared}>
            <StatusStep />
          </Step>
        </ol>
      )}
    </SettingsSection>
  );
}
