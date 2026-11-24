import type { ChangeKind, ChangeSet, ChangeSetDetail, ChangeSetStatus } from '../../api/types';
import { Badge, type BadgeTone } from '../../components/Badge/Badge';
import { blueprintChangeDetails, describeBlueprintChange, type ChangeSetBlueprints } from '../../domain/changeSetText';
import { formatNumber } from '../../domain/fields';
import { impactLines, itemsTouched, touchesItems } from '../../domain/impactText';
import { CHANGE_SET_STATUS_LABELS } from '../../domain/labels';
import { cx } from '../../lib/cx';
import styles from './ChangeSets.module.css';

/** 等待核准是中性的：強調色只給「可以互動、目前所在、已選取」（docs/DESIGN.md 第 4 節），不拿來表達狀態。 */
const STATUS_TONES: Readonly<Record<ChangeSetStatus, BadgeTone>> = {
  pending: 'neutral',
  applied: 'success',
  rejected: 'neutral',
  expired: 'warning',
};

export function ChangeSetStatusBadge({ status }: { readonly status: ChangeSetStatus }) {
  return <Badge tone={STATUS_TONES[status]}>{CHANGE_SET_STATUS_LABELS[status]}</Badge>;
}

const KIND_BADGES: Readonly<Record<ChangeKind, { readonly label: string; readonly tone: BadgeTone }>> = {
  added: { label: '新增', tone: 'accent' },
  removed: { label: '刪除', tone: 'danger' },
  modified: { label: '修改', tone: 'warning' },
};

const ORIGIN_LABELS: Readonly<Partial<Record<ChangeSet['origin'], string>>> = {
  assistant: 'AI 助理提出',
  agent: 'AI 代理提出',
};

/** 模型提出的變更集（網頁的助理、外部的代理）：核准的人要知道內容是模型寫的。直接提出的不畫。 */
export function ChangeSetOriginBadge({ origin }: { readonly origin: ChangeSet['origin'] }) {
  const label = ORIGIN_LABELS[origin];
  // 前面的空格跟著徽章，直接提出的不留多餘的空白。
  return label === undefined ? null : <> <Badge>{label}</Badge></>;
}

/** 擋住套用的原因有幾個：伺服器列的原因，或標成擋住的影響（取多的那個）。 */
export function blockingCount(detail: ChangeSetDetail): number {
  return Math.max(detail.blockers.length, detail.impacts.filter((impact) => impact.blocksApply).length);
}

function touchedText(detail: ChangeSetDetail): string {
  if (detail.changeSet.status !== 'pending') return '不再計算';
  const touched = itemsTouched(detail.impacts);
  const kinds = detail.impacts.filter((impact) => impact.affected > 0 && touchesItems(impact));
  if (touched === 0) return '沒有';
  // 同一個項目可能同時被搬移又封存，加總只是上限。
  return kinds.length > 1 ? `最多 ${formatNumber(touched)} 個` : `${formatNumber(touched)} 個`;
}

function applicabilityText(detail: ChangeSetDetail): string {
  const { status, appliedVersion } = detail.changeSet;
  if (status === 'applied') return appliedVersion === null ? '已套用' : `已套用（第 ${String(appliedVersion)} 版）`;
  if (status !== 'pending') return CHANGE_SET_STATUS_LABELS[status];
  const blocking = blockingCount(detail);
  if (blocking > 0) return `有 ${String(blocking)} 個原因擋住套用`;
  return detail.canApply ? '可以套用' : '要有核准權限的人才能套用';
}

/** 頁首下面的三格摘要：改了幾項設定、會動到多少項目、能不能套用。 */
export function ChangeSetOverview({ detail }: { readonly detail: ChangeSetDetail }) {
  const isBlocked = detail.changeSet.status === 'pending' && blockingCount(detail) > 0;
  const isReady = detail.changeSet.status === 'pending' && !isBlocked && detail.canApply;
  return (
    // 預覽頁的第一筆內容（docs/DESIGN.md 6.2）：給 scripts/ui-audit.js 量高度預算。
    <ul className={styles.overview} aria-label="摘要" data-content-start>
      <li className={styles.stat}>
        <span className={styles.statLabel}>設定的修改</span>
        <strong className={styles.statValue}>{`${String(detail.changes.length)} 項`}</strong>
      </li>
      <li className={styles.stat}>
        <span className={styles.statLabel}>會動到的項目</span>
        <strong className={styles.statValue}>{touchedText(detail)}</strong>
      </li>
      <li className={cx(styles.stat, isBlocked && styles.statDanger, isReady && styles.statSuccess)}>
        <span className={styles.statLabel}>能不能套用</span>
        <strong className={styles.statValue}>{applicabilityText(detail)}</strong>
      </li>
    </ul>
  );
}

export function ChangeList({ detail, names }: { readonly detail: ChangeSetDetail; readonly names: ChangeSetBlueprints }) {
  if (detail.changes.length === 0) {
    return <p className={styles.empty}>和目前的設定一樣，沒有差異。</p>;
  }
  return (
    <ol className={styles.lines} aria-label="設定的差異">
      {detail.changes.map((change) => {
        const badge = KIND_BADGES[change.kind];
        const details = blueprintChangeDetails(change, names);
        return (
          <li key={`${change.kind}:${change.resource}:${change.parentKey ?? ''}:${change.key}`} className={styles.line}>
            <Badge tone={badge.tone}>{badge.label}</Badge>
            <span>{describeBlueprintChange(change, names)}</span>
            {details.length > 0 && (
              <ul className={styles.details} aria-label="可以走的轉換">
                {details.map((line) => (
                  <li key={line}>{line}</li>
                ))}
              </ul>
            )}
          </li>
        );
      })}
    </ol>
  );
}

export function ImpactList({ detail, names }: { readonly detail: ChangeSetDetail; readonly names: ChangeSetBlueprints }) {
  const isCounted = detail.changeSet.status === 'pending';
  if (detail.impacts.length === 0) {
    return <p className={styles.empty}>不會動到既有的項目。</p>;
  }
  return (
    <>
      {!isCounted && <p className={styles.hint}>這份變更集已經不是等待核准，影響的數量不再計算。</p>}
      <ul className={styles.lines} aria-label="對既有資料的影響">
        {impactLines(detail.impacts, names, isCounted).map((line, index) => (
          // 影響的順序由伺服器的計畫決定，不會重排。
          <li key={index} className={cx(styles.line, line.blocksApply && isCounted && styles.blockingLine)}>
            <span>{line.text}</span>
            {line.blocksApply && isCounted && <Badge tone="danger">擋住套用</Badge>}
          </li>
        ))}
      </ul>
    </>
  );
}

export function BlockerList({ detail }: { readonly detail: ChangeSetDetail }) {
  if (detail.blockers.length === 0) {
    return null;
  }
  return (
    <ul className={cx(styles.lines, styles.blockers)} aria-label="擋住套用的原因">
      {detail.blockers.map((blocker, index) => (
        // 原因的順序由伺服器決定，不會重排。
        <li key={index} className={styles.line}>
          {blocker.message}
        </li>
      ))}
    </ul>
  );
}
