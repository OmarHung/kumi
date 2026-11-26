import { Link } from 'react-router';
import type { ItemProgress } from '../../api/planning';
import type { OverviewCycle, OverviewMilestone, ProjectOverview } from '../../api/overview';
import type { CalendarDate } from '../../api/types';
import { formatInstant, formatRelativeTime } from '../../domain/time';
import { cx } from '../../lib/cx';
import { OPEN_NUMBER, type OverviewNumber } from './overviewColumns';
import { overviewLinks } from './overviewLinks';
import styles from './Overview.module.css';

/**
 * 一個數字。是 0 時淡化、不是連結（沒有東西可看）；不是 0 的連到專案的清單、套好篩選。
 * 不是 0 的數字用一般文字色加粗、不用強調色：九個一模一樣的紫色連結看不出哪個要緊；只有已過期用 danger（規範：已過期＝danger，旁邊有欄名，不只靠顏色）。
 * 連結的名稱帶專案與數字的名稱（「WEB 已過期 3 個」）：同一頁有很多個「3」，報讀軟體要分得出是哪一個。
 */
export function NumberCell({ project, number }: { readonly project: ProjectOverview; readonly number: OverviewNumber }) {
  const value = number.valueOf(project);
  if (value === 0) return <span className={styles.zero}>{value}</span>;
  if (number.linkOf === null) return <span className={styles.plain}>{value}</span>;
  return (
    <Link
      to={number.linkOf(project.projectKey)}
      className={cx(styles.number, number.id === 'overdue' && styles.numberDanger)}
      data-id={number.id}
      aria-label={`${project.projectKey} ${number.label} ${String(value)} 個`}
    >
      {value}
    </Link>
  );
}

/** 狀態橫條下面的註腳：還沒結束幾個（連到 open=1），全部結束時淡化、不是連結。沒有項目的專案橫條已經說「沒有項目」，不重複。 */
export function OpenFootnote({ project }: { readonly project: ProjectOverview }) {
  if (project.states.length === 0) return null;
  if (project.open === 0) return <span className={styles.footnote}>全部結束</span>;
  // 整段「還沒結束 12」是連結：只有數字能點時點擊範圍只有一個字寬（手機上量到 19px 高），撐不到規範的大小。
  return (
    <span className={styles.footnote}>
      <Link
        to={overviewLinks.open(project.projectKey)}
        className={styles.footnoteLink}
        aria-label={`${project.projectKey} ${OPEN_NUMBER.label} ${String(project.open)} 個`}
      >
        還沒結束 <span className={styles.footnoteCount}>{project.open}</span>
      </Link>
    </span>
  );
}

/** 月／日（今年以外的年份在名稱旁沒有地方寫，進度欄只放日期的短寫；完整日期在 title）。 */
function shortDate(date: CalendarDate): string {
  return date.slice(5).replace('-', '/');
}

interface Counts {
  /** 進度格右邊的字：完成／要做的；沒有東西要做時是一句話。 */
  readonly text: string;
  /** 報讀軟體用的完整說法（含取消的）；沒有東西要做時是 null，不畫條。 */
  readonly spoken: string | null;
  readonly done: number;
  readonly planned: number;
}

/** 取消的項目不算進分母（和 ProgressMeter 一樣：算進去 100% 永遠到不了）。 */
function countsOf(progress: ItemProgress): Counts {
  if (progress.total === 0) return { text: '還沒有項目', spoken: null, done: 0, planned: 0 };
  const planned = progress.total - progress.canceled;
  if (planned === 0) return { text: '全部取消', spoken: null, done: 0, planned: 0 };
  const canceled = progress.canceled > 0 ? `・取消 ${String(progress.canceled)}` : '';
  return {
    text: `${String(progress.done)}／${String(planned)}`,
    spoken: `完成 ${String(progress.done)}／${String(planned)}${canceled}`,
    done: progress.done,
    planned,
  };
}

/**
 * 細的進度條，原生 <progress>（理由同 ProgressMeter：寬度是執行時的數值，規範 13 節不准 style）。
 * 比 ProgressMeter 精簡：只有一條細線，數字由呼叫端寫在同一行；沒有東西要做時不畫條。
 */
function ThinProgress({ name, counts }: { readonly name: string; readonly counts: Counts }) {
  if (counts.spoken === null) return null;
  return (
    <progress className={styles.thinTrack} aria-label={`${name} 的完成進度`} aria-valuetext={counts.spoken} max={counts.planned} value={counts.done} />
  );
}

/**
 * 進度欄（週期與里程碑合成一格）：有這期週期時顯示週期；沒有但有下一個里程碑時顯示里程碑；都沒有就一個淡化的「—」。
 * 兩種都是兩行：名稱（連結）與數字同一行、下面一條細條，所以列高一致。
 */
export function PlanCell({ project }: { readonly project: ProjectOverview }) {
  const { projectKey, currentCycle: cycle, nextMilestone: milestone } = project;
  if (cycle !== null) return <CyclePlan projectKey={projectKey} cycle={cycle} />;
  if (milestone !== null) return <MilestonePlan projectKey={projectKey} milestone={milestone} />;
  return (
    <>
      <span className={styles.none} aria-hidden="true">
        —
      </span>
      <span className="visually-hidden">沒有進行中的週期或即將到期的里程碑</span>
    </>
  );
}

function CyclePlan({ projectKey, cycle }: { readonly projectKey: string; readonly cycle: OverviewCycle }) {
  const counts = countsOf(cycle.progress);
  return (
    <div className={styles.plan}>
      <div className={styles.planHead}>
        <Link to={overviewLinks.cycle(projectKey, cycle.id)} className={styles.plannedName} aria-label={`${projectKey} 這期週期 ${cycle.name}`}>
          {cycle.name}
        </Link>
        <span className={styles.planCount}>{counts.text}</span>
      </div>
      <ThinProgress name={cycle.name} counts={counts} />
    </div>
  );
}

function MilestonePlan({ projectKey, milestone }: { readonly projectKey: string; readonly milestone: OverviewMilestone }) {
  const counts = countsOf(milestone.progress);
  return (
    <div className={styles.plan}>
      <div className={styles.planHead}>
        <span className={styles.planLabel}>里程碑</span>
        <Link
          to={overviewLinks.milestone(projectKey, milestone.id)}
          className={styles.plannedName}
          aria-label={`${projectKey} 下一個里程碑 ${milestone.name}`}
        >
          {milestone.name}
        </Link>
      </div>
      <div className={styles.planMeta}>
        <span>{`到期 ${shortDate(milestone.dueDate)} · ${counts.text}`}</span>
        <ThinProgress name={milestone.name} counts={counts} />
      </div>
    </div>
  );
}

/** 最近一次更新：相對的說法，完整時間放在 title。沒有項目時沒有時間。now 由呼叫端給，同一頁用同一個「現在」。 */
export function UpdatedCell({ at, now }: { readonly at: string | null; readonly now: number }) {
  if (at === null) return <span className={styles.none}>還沒有更新</span>;
  return (
    <time dateTime={at} title={formatInstant(at)}>
      {formatRelativeTime(at, now)}
    </time>
  );
}
