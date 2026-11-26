import { Link } from 'react-router';
import type { OverviewCycle, OverviewMilestone, ProjectOverview } from '../../api/overview';
import { formatDate } from '../../domain/dates';
import { formatInstant, formatRelativeTime } from '../../domain/time';
import { ProgressMeter } from '../planning/ProgressMeter';
import type { OverviewNumber } from './overviewColumns';
import { overviewLinks } from './overviewLinks';
import styles from './Overview.module.css';

/**
 * 一個數字。是 0 時淡化、不是連結（沒有東西可看）；不是 0 的連到專案的清單、套好篩選。
 * 連結的名稱帶專案與數字的名稱（「WEB 已過期 3 個」）：同一頁有很多個「3」，報讀軟體要分得出是哪一個。
 */
export function NumberCell({ project, number }: { readonly project: ProjectOverview; readonly number: OverviewNumber }) {
  const value = number.valueOf(project);
  if (value === 0 || number.linkOf === null) {
    return <span className={value === 0 ? styles.zero : styles.plain}>{value}</span>;
  }
  return (
    <Link
      to={number.linkOf(project.projectKey)}
      className={styles.number}
      data-id={number.id}
      aria-label={`${project.projectKey} ${number.label} ${String(value)} 個`}
    >
      {value}
    </Link>
  );
}

function dateRange(cycle: OverviewCycle): string {
  return `${formatDate(cycle.startDate)} – ${formatDate(cycle.endDate)}`;
}

/** 這期週期：名稱連到只看這個週期的清單、日期範圍、完成進度。今天不在任何週期裡時是一條短橫線。 */
export function CycleCell({ projectKey, cycle }: { readonly projectKey: string; readonly cycle: OverviewCycle | null }) {
  if (cycle === null) return <span className={styles.none}>沒有進行中的週期</span>;
  return (
    <div className={styles.planned}>
      <Link to={overviewLinks.cycle(projectKey, cycle.id)} className={styles.plannedName} aria-label={`${projectKey} 這期週期 ${cycle.name}`}>
        {cycle.name}
      </Link>
      <span className={styles.plannedDate}>{dateRange(cycle)}</span>
      <ProgressMeter name={cycle.name} progress={cycle.progress} />
    </div>
  );
}

/** 下一個里程碑：名稱連到只看這個里程碑的清單、到期日、完成進度。 */
export function MilestoneCell({ projectKey, milestone }: { readonly projectKey: string; readonly milestone: OverviewMilestone | null }) {
  if (milestone === null) return <span className={styles.none}>沒有即將到期的里程碑</span>;
  return (
    <div className={styles.planned}>
      <Link
        to={overviewLinks.milestone(projectKey, milestone.id)}
        className={styles.plannedName}
        aria-label={`${projectKey} 下一個里程碑 ${milestone.name}`}
      >
        {milestone.name}
      </Link>
      <span className={styles.plannedDate}>{`到期 ${formatDate(milestone.dueDate)}`}</span>
      <ProgressMeter name={milestone.name} progress={milestone.progress} />
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
