import { createContext, use, type ReactNode } from 'react';
import type { CalendarDate, ProjectDetail } from '../../api/types';

/** 依 detail.myPermissions 決定的操作。畫面只是不開放，真正的檢查在後端。 */
export interface ProjectAbilities {
  readonly create: boolean;
  readonly update: boolean;
  readonly transition: boolean;
  readonly delete: boolean;
  readonly manage: boolean;
}

export function abilitiesOf(detail: ProjectDetail): ProjectAbilities {
  const has = (permission: ProjectDetail['myPermissions'][number]) =>
    detail.myPermissions.includes(permission);
  return {
    create: has('item.create'),
    update: has('item.update'),
    transition: has('item.transition'),
    delete: has('item.delete'),
    manage: has('project.manage'),
  };
}

export interface ProjectTableContextValue {
  readonly detail: ProjectDetail;
  readonly can: ProjectAbilities;
  /** 整張表用同一個「今天」，過期的標示才一致。 */
  readonly todayDate: CalendarDate;
  /** 改狀態被角色擋下時，讓有權限的人直接打開成員對話框調整。 */
  readonly onOpenMembers: () => void;
}

const ProjectTableContext = createContext<ProjectTableContextValue | null>(null);

interface ProjectTableProviderProps {
  readonly value: ProjectTableContextValue;
  readonly children: ReactNode;
}

/** 表格的格子由 TanStack Table 渲染，專案的資料用 Context 傳，不必經過每一層欄位定義。 */
export function ProjectTableProvider({ value, children }: ProjectTableProviderProps) {
  return <ProjectTableContext value={value}>{children}</ProjectTableContext>;
}

export function useProjectTable(): ProjectTableContextValue {
  const value = use(ProjectTableContext);
  if (value === null) {
    throw new Error('useProjectTable 只能用在專案的表格裡。');
  }
  return value;
}
