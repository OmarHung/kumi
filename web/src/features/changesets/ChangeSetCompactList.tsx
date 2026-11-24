import type { ChangeSet } from '../../api/types';
import { List, ListRow, type ListMeta } from '../../components/List/List';
import { formatInstant } from '../../domain/time';
import { changeSetPagePath } from '../work-items/itemLinks';
import { ChangeSetOriginBadge, ChangeSetStatusBadge } from './ChangeSetSections';

interface ChangeSetCompactListProps {
  readonly projectKey: string;
  readonly changeSets: readonly ChangeSet[];
}

/** 第二行：狀態（已套用的加上第幾版）、提出的人與時間；整筆是連結，每一個欄位都帶著說法。 */
function changeSetMeta(changeSet: ChangeSet): ListMeta[] {
  const version = changeSet.appliedVersion === null ? null : `（第 ${String(changeSet.appliedVersion)} 版）`;
  return [
    {
      key: 'status',
      label: '狀態',
      content: (
        <>
          <ChangeSetStatusBadge status={changeSet.status} />
          {version}
        </>
      ),
    },
    {
      key: 'proposer',
      label: '提出的人',
      content: (
        <>
          {changeSet.proposedBy.displayName}<ChangeSetOriginBadge origin={changeSet.origin} />
        </>
      ),
    },
    { key: 'proposedAt', label: '提出時間', content: <time dateTime={changeSet.proposedAt}>{formatInstant(changeSet.proposedAt)}</time> },
  ];
}

/**
 * 窄螢幕的變更集清單（docs/DESIGN.md 7.1：窄螢幕不用橫向捲動的表格）：components/List，一筆兩行，
 * 第一行編號與摘要，第二行狀態、提出的人與時間；整筆連到預覽頁。內容和寬螢幕的表格一樣。
 */
export function ChangeSetCompactList({ projectKey, changeSets }: ChangeSetCompactListProps) {
  return (
    <List label="變更集">
      {changeSets.map((changeSet, index) => (
        <ListRow
          key={changeSet.number}
          to={changeSetPagePath(projectKey, changeSet.number)}
          code={`#${String(changeSet.number)}`}
          title={changeSet.summary ?? '沒有摘要'}
          titleText={changeSet.summary ?? undefined}
          meta={changeSetMeta(changeSet)}
          isContentStart={index === 0}
        />
      ))}
    </List>
  );
}
