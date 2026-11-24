import type { ProjectDetail } from '../../api/types';
import { errorMessage } from '../../api/http';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { useUnarchiveProject } from './useProjectLifecycle';
import { focusPageHeading } from '../../lib/pageTop';

const ARCHIVED_MESSAGE = '這個專案已封存：只能看，不能新增或修改。';

interface ArchivedNoticeProps {
  readonly detail: ProjectDetail;
  /** 這一頁有不受封存影響的編輯（資訊頁的專案說明）時，換掉「只能看，不能新增或修改」這句，免得提示和能做的事互相矛盾。 */
  readonly message?: string;
}

/**
 * 封存的專案底下每一頁最上面的提示（docs/design/08-interaction.md 8.3「封存的專案」）：中性的說明，不是錯誤；
 * 有 project.manage 的人旁邊多一顆「取消封存」。專案沒封存時什麼都不畫。
 * 一開始就在的說明，不必唸出來（role="none"）；取消封存失敗時才是 alert。
 */
export function ArchivedNotice({ detail, message = ARCHIVED_MESSAGE }: ArchivedNoticeProps) {
  const unarchive = useUnarchiveProject(detail.project.key);
  if (detail.project.archivedAt === null) return null;
  const canUnarchive = detail.myPermissions.includes('project.manage');
  const action = canUnarchive ? (
    <Button
      size="sm"
      aria-disabled={unarchive.isPending || undefined}
      onClick={() => {
        if (unarchive.isPending) return;
        // 成功後這顆按鈕跟著提示不見：焦點移到頁面的標題，不掉到 body（DESIGN 8.3「焦點」）。
        unarchive.mutate(undefined, {
          onSuccess: () => {
            focusPageHeading(document.querySelector<HTMLElement>('h1'));
          },
        });
      }}
    >
      {unarchive.isPending ? '取消封存中…' : '取消封存'}
    </Button>
  ) : undefined;
  return (
    <>
      <Notice tone="info" role="none" actions={action}>
        {message}
      </Notice>
      {unarchive.isError && (
        <Notice tone="danger" onDismiss={unarchive.reset}>
          {errorMessage(unarchive.error)}
        </Notice>
      )}
    </>
  );
}
