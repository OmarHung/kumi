import { useState } from 'react';
import type { KnowledgeSpace } from '../../api/knowledgeTypes';
import { Badge } from '../../components/Badge/Badge';
import { Button } from '../../components/Button/Button';
import { List, ListRow, type ListMeta } from '../../components/List/List';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { EmptyState, ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { CreateSpaceDialog } from './CreateSpaceDialog';
import { knowledgeSpaceUrl } from './knowledgeLinks';
import { useKnowledgeSpaces } from './useKnowledgeSpaces';
import { useSpaceCreation, type SpaceCreation } from './useSpaceCreation';

const SKELETON_ROWS = 4;

function metaOf(space: KnowledgeSpace): readonly ListMeta[] {
  return [
    ...(space.project === null ? [] : [{ key: 'project', label: '專案', content: `${space.project.key} ${space.project.name}` }]),
    ...(space.archivedAt === null ? [] : [{ key: 'archived', content: <Badge>已封存</Badge> }]),
    ...(space.description === null || space.description === ''
      ? []
      : [{ key: 'description', label: '簡介', content: space.description }]),
  ];
}

function SpaceList({ spaces }: { readonly spaces: readonly KnowledgeSpace[] }) {
  return (
    <List label="空間">
      {spaces.map((space, index) => (
        <ListRow
          key={space.id}
          to={knowledgeSpaceUrl(space.key)}
          code={space.key}
          title={space.name}
          meta={metaOf(space)}
          isContentStart={index === 0}
        />
      ))}
    </List>
  );
}

function SpacesContent({ creation }: { readonly creation: SpaceCreation }) {
  const spaces = useKnowledgeSpaces();
  if (spaces.isPending) {
    return <Skeleton label="正在載入空間…" rows={SKELETON_ROWS} />;
  }
  // 只有第一次就讀不到才換成錯誤；已經有清單時重抓失敗，留著清單、多一行提示。
  if (spaces.isLoadingError) {
    return (
      <ErrorState
        error={spaces.error}
        onRetry={() => {
          void spaces.refetch();
        }}
      />
    );
  }
  const notice = <RefetchErrorNotice query={spaces} message="無法更新空間清單，畫面上可能是舊的。" />;
  if (spaces.data.length === 0) {
    const hint = creation.canCreate ? '按「新增空間」，開一個放手冊或專案文件的地方。' : '請組織管理員或專案的管理員開一個空間，開好之後會出現在這裡。';
    return (
      <>
        {notice}
        <EmptyState title="還沒有空間">
          知識庫用空間整理頁面：組織的手冊、決策紀錄，或某個專案的文件。{hint}
        </EmptyState>
      </>
    );
  }
  return (
    <>
      {notice}
      <SpaceList spaces={spaces.data} />
    </>
  );
}

/**
 * 空間清單（/knowledge）：看得到的空間，沒封存的在前（伺服器已經排好）。
 * 「新增空間」：組織管理員，或有專案能建專案空間的人（useSpaceCreation）；沒有權限的人沒有入口。
 */
export function KnowledgeHomePage() {
  const creation = useSpaceCreation();
  const [isCreating, setIsCreating] = useState(false);
  const createButton = creation.canCreate ? (
    <Button
      variant="primary"
      onClick={() => {
        setIsCreating(true);
      }}
    >
      新增空間
    </Button>
  ) : undefined;
  return (
    <PageLayout variant="list" bar={<PageBar title="知識庫" actions={createButton} />}>
      <SpacesContent creation={creation} />
      {isCreating && (
        <CreateSpaceDialog
          creation={creation}
          onClose={() => {
            setIsCreating(false);
          }}
        />
      )}
    </PageLayout>
  );
}
