import { useState, type RefObject } from 'react';
import { Link } from 'react-router';
import type { KnowledgePageNode, KnowledgeSpace } from '../../../api/knowledgeTypes';
import { Button } from '../../../components/Button/Button';
import { Icon } from '../../../components/Icon/Icon';
import { EmptyState } from '../../../components/StatusViews/StatusViews';
import { formatInstant, formatRelativeTime } from '../../../domain/time';
import { knowledgePageUrl } from '../knowledgeLinks';
import type { TreeModel } from '../tree/treeModel';
import styles from './SpaceHome.module.css';

/** 「最近更新」列幾頁。 */
const RECENT_COUNT = 10;

function recentPagesOf(pages: readonly KnowledgePageNode[]): readonly KnowledgePageNode[] {
  return [...pages].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)).slice(0, RECENT_COUNT);
}

interface SpaceHomeProps {
  readonly space: KnowledgeSpace;
  readonly model: TreeModel;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
  readonly onNewPage: () => void;
}

function TopPages({ spaceKey, model }: { readonly spaceKey: string; readonly model: TreeModel }) {
  return (
    <section className={styles.group} aria-labelledby="space-home-top">
      <h2 id="space-home-top" className={styles.heading}>
        最上層的頁面
      </h2>
      <ul className={styles.list}>
        {model.roots.map((page) => (
          <li key={page.id}>
            <Link to={knowledgePageUrl(spaceKey, page.number)}>{page.title}</Link>
          </li>
        ))}
      </ul>
    </section>
  );
}

function RecentPages({ spaceKey, pages }: { readonly spaceKey: string; readonly pages: readonly KnowledgePageNode[] }) {
  const [now] = useState(() => Date.now());
  return (
    <section className={styles.group} aria-labelledby="space-home-recent">
      <h2 id="space-home-recent" className={styles.heading}>
        最近更新
      </h2>
      <ul className={styles.list}>
        {recentPagesOf(pages).map((page) => (
          <li key={page.id} className={styles.recent}>
            <Link to={knowledgePageUrl(spaceKey, page.number)}>{page.title}</Link>
            <span className={styles.recentMeta}>
              {`${page.updatedBy.displayName} · `}
              <time dateTime={page.updatedAt} title={formatInstant(page.updatedAt)}>
                {formatRelativeTime(page.updatedAt, now)}
              </time>
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function EmptySpace({ canEdit, onNewPage }: { readonly canEdit: boolean; readonly onNewPage: () => void }) {
  const action = canEdit && (
    <Button variant="primary" onClick={onNewPage}>
      新增第一頁
    </Button>
  );
  return (
    <EmptyState title="還沒有頁面" action={action}>
      在這裡寫下操作手冊、會議記錄與決策紀錄，同事找得到、也改得動。
    </EmptyState>
  );
}

/**
 * 空間首頁（docs/DESIGN.md 6.2 的閱讀頁）：空間名稱、簡介、最上層的頁面與最近更新的頁面。
 * 沒有頁面時是空白狀態，能寫的人給「新增第一頁」（那一頁就是這裡唯一的主要動作，標題列的「新增頁面」先不出現）。
 */
export function SpaceHome({ space, model, headingRef, onNewPage }: SpaceHomeProps) {
  const canEdit = space.myPermissions.canEdit;
  const hasPages = model.pages.length > 0;
  return (
    <section className={styles.home}>
      <div className={styles.head}>
        <h1 ref={headingRef} tabIndex={-1} className={styles.title} data-content-start>
          {space.name}
        </h1>
        {canEdit && hasPages && (
          <Button size="sm" onClick={onNewPage}>
            <Icon name="add" />
            新增頁面
          </Button>
        )}
      </div>
      {space.description !== null && space.description !== '' && <p className={styles.description}>{space.description}</p>}
      {hasPages ? (
        <>
          <TopPages spaceKey={space.key} model={model} />
          <RecentPages spaceKey={space.key} pages={model.pages} />
        </>
      ) : (
        <EmptySpace canEdit={canEdit} onNewPage={onNewPage} />
      )}
    </section>
  );
}
