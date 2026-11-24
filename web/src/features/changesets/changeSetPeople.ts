import type { ChangeSet } from '../../api/types';
import { formatInstant } from '../../domain/time';

/** 「範例成員 在 2026/11/15 10:00 提出」。 */
export function proposedText(changeSet: ChangeSet): string {
  return `${changeSet.proposedBy.displayName} 在 ${formatInstant(changeSet.proposedAt)} 提出`;
}

/** 決定的結果：誰在何時套用（第幾版）、放棄，或什麼時候過期。等待核准的是 null。 */
export function decidedText(changeSet: ChangeSet): string | null {
  const at = changeSet.decidedAt === null ? '' : ` 在 ${formatInstant(changeSet.decidedAt)}`;
  const who = changeSet.decidedBy?.displayName ?? '';
  switch (changeSet.status) {
    case 'pending':
      return null;
    case 'applied': {
      const version = changeSet.appliedVersion === null ? '' : `，藍圖是第 ${String(changeSet.appliedVersion)} 版`;
      return `${who}${at} 套用${version}`;
    }
    case 'rejected':
      return `${who}${at} 放棄`;
    case 'expired':
      return `${at.trim()} 過期`.trim();
  }
}
