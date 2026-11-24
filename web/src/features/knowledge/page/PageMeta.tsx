import { useState } from 'react';
import { Link } from 'react-router';
import type { KnowledgePage } from '../../../api/knowledgeTypes';
import { Button } from '../../../components/Button/Button';
import { Icon } from '../../../components/Icon/Icon';
import { formatInstant, formatRelativeTime } from '../../../domain/time';
import { knowledgePageUrl } from '../knowledgeLinks';
import { RESTRICTED_ICON } from '../restrictedIcon';
import styles from './KnowledgePage.module.css';

function RestrictionNote({ page }: { readonly page: KnowledgePage }) {
  if (page.isRestricted) {
    return (
      <span className={styles.restriction}>
        <Icon name={RESTRICTED_ICON} />
        限制：只有指定的人看得到
      </span>
    );
  }
  if (page.restrictedAncestors.length === 0) return null;
  return (
    <span className={styles.restriction}>
      <Icon name={RESTRICTED_ICON} />
      限制：繼承自
      {page.restrictedAncestors.map((ancestor, index) => (
        <span key={ancestor.id}>
          {index > 0 && '、'}
          <Link to={knowledgePageUrl(page.space.key, ancestor.number)}>{`「${ancestor.title}」`}</Link>
        </span>
      ))}
    </span>
  );
}

interface RestrictionEntryProps {
  readonly page: KnowledgePage;
  readonly onOpen: () => void;
}

/** 限制的入口：能改的人一直有（沒有限制的頁面也是從這裡設）；只能看的人只有在有限制時才有，打開是唯讀的名單。 */
function RestrictionEntry({ page, onOpen }: RestrictionEntryProps) {
  const canEdit = page.myPermissions.canEdit;
  const hasRestriction = page.isRestricted || page.restrictedAncestors.length > 0;
  if (!canEdit && !hasRestriction) return null;
  return (
    <Button size="sm" variant="ghost" onClick={onOpen}>
      {canEdit ? '限制…' : '查看名單'}
    </Button>
  );
}

/**
 * 標題下面的一行：最後是誰什麼時候改的（滑過看完整的時間），有限制時說明誰看得到，繼承來的連到設限的那一頁；
 * 最後是打開「限制…」對話框的入口。
 */
export function PageMeta({ page, onOpenRestriction }: { readonly page: KnowledgePage; readonly onOpenRestriction: () => void }) {
  const [now] = useState(() => Date.now());
  return (
    <p className={styles.meta}>
      <span>
        {`最後由 ${page.updatedBy.displayName} 更新於 `}
        <time dateTime={page.updatedAt} title={formatInstant(page.updatedAt)}>
          {formatRelativeTime(page.updatedAt, now)}
        </time>
      </span>
      <RestrictionNote page={page} />
      <RestrictionEntry page={page} onOpen={onOpenRestriction} />
    </p>
  );
}
