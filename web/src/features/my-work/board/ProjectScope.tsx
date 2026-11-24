import type { ReactNode } from 'react';
import type { ProjectDetail } from '../../../api/types';
import { useItemScope } from '../../item-page/ItemContent';
import { ProjectTableProvider } from '../../item-table/ProjectTableContext';

/**
 * 那個項目自己專案的範圍：補填的對話框、被擋下來的說明要用那個專案的藍圖、權限與成員對話框
 * （「到「成員」調整角色」）——「我的工作」跨專案，每張卡片的專案都不一樣。
 */
export function ProjectScope({ detail, children }: { readonly detail: ProjectDetail; readonly children: ReactNode }) {
  const { value, membersDialog } = useItemScope(detail);
  return (
    <ProjectTableProvider value={value}>
      {children}
      {membersDialog}
    </ProjectTableProvider>
  );
}
