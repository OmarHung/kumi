import { Link } from 'react-router';
import type { Project } from '../../api/types';
import { buttonClassName } from '../../components/Button/Button';
import { List, ListRow } from '../../components/List/List';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { formatDate, toCalendarDate } from '../../domain/dates';
import { projectRoleLabel } from '../../domain/labels';
import { formatInstant } from '../../domain/time';
import { useIsCompact } from '../../lib/useMediaQuery';
import { projectInfoPath } from '../work-items/itemLinks';
import styles from './ProjectListPage.module.css';
import { projectPageLink } from './projectViewPreference';

/** 專案連結照這個專案上次用的檢視開（表格或看板，projectViewPreference.ts）。 */
function projectPath(project: Project): string {
  return projectPageLink(project.key);
}

/** 封存的日期（使用者所在地的日曆日）；完整的時刻放在 title。 */
function ArchivedDate({ at }: { readonly at: string }) {
  return (
    <time dateTime={at} title={formatInstant(at)}>
      {formatDate(toCalendarDate(new Date(at)))}
    </time>
  );
}

export interface ProjectGroupProps {
  readonly projects: readonly Project[];
  /** 這一組是頁面上第一筆內容（給 scripts/ui-audit.js 量離頂端多遠）。 */
  readonly isFirstGroup: boolean;
  /** 表格與清單的名稱：「專案」「已封存的專案」「符合的專案」。 */
  readonly label?: string;
  /** 多一欄封存的日期（已封存的那一組，或搜尋結果裡有封存的專案）；沒封存的專案那一格是空的。 */
  readonly hasArchivedDate?: boolean;
  /**
   * 搜尋結果：專案代號對說明裡比對到的摘錄（代號或名稱就比對到的是 null，那一格是空的）。
   * 有給才多一欄「說明裡的」（窄螢幕在第二行）。
   */
  readonly snippets?: ReadonlyMap<string, string | null>;
}

function ProjectTable({ projects, isFirstGroup, label = '專案', hasArchivedDate = false, snippets }: ProjectGroupProps) {
  return (
    <TableScroll label={label} layout="page">
      <table className={tableStyles.table}>
        <caption className="visually-hidden">{label}</caption>
        <thead>
          <tr>
            <th scope="col">代號</th>
            <th scope="col">名稱</th>
            <th scope="col">我的角色</th>
            {snippets !== undefined && <th scope="col">說明裡的</th>}
            {hasArchivedDate && <th scope="col">封存於</th>}
            <th scope="col">
              <span className="visually-hidden">動作</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {projects.map((project, index) => (
            // 第一列標上 data-content-start：給 scripts/ui-audit.js 量第一筆內容離頂端多遠（docs/DESIGN.md 6.2）。
            <tr key={project.id} data-content-start={(isFirstGroup && index === 0) || undefined}>
              <td className={tableStyles.mono}>{project.key}</td>
              <td className={tableStyles.primaryCell}>
                <Link to={projectPath(project)} className={tableStyles.rowLink}>
                  {project.name}
                </Link>
              </td>
              <td className={tableStyles.muted}>{projectRoleLabel(project.myRole)}</td>
              {snippets !== undefined && <td className={tableStyles.muted}>{snippets.get(project.key)}</td>}
              {hasArchivedDate && (
                <td className={tableStyles.nowrap}>{project.archivedAt === null ? '' : <ArchivedDate at={project.archivedAt} />}</td>
              )}
              <td className={styles.actionCell}>
                {/* 看資訊是常做的事：不必先進專案再按頁列的「資訊」。名稱帶專案，報讀軟體才分得出是哪一列。 */}
                <Link
                  to={projectInfoPath(project.key)}
                  className={buttonClassName('secondary', 'sm')}
                  aria-label={`${project.name} 的資訊`}
                >
                  資訊
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </TableScroll>
  );
}

/** 窄螢幕第二行的欄位：說明裡的摘錄、封存的日期（有才有）。 */
function metaOf(project: Project, snippets: ProjectGroupProps['snippets']) {
  const snippet = snippets?.get(project.key) ?? null;
  return [
    ...(snippet === null ? [] : [{ key: 'snippet', label: '說明裡的', content: snippet }]),
    ...(project.archivedAt === null
      ? []
      : [
          {
            key: 'archived',
            label: '封存於',
            content: <ArchivedDate at={project.archivedAt} />,
          },
        ]),
  ];
}

/**
 * 窄螢幕：專案改成清單，不用橫向捲動的表格（docs/DESIGN.md 7.1）。整筆是連到專案頁的連結（代號、名稱、我的角色）；
 * 「我的角色：」只給報讀軟體，表格裡它是欄的標題。第二行是說明的摘錄與封存的日期。
 */
function ProjectList({ projects, isFirstGroup, label = '專案', snippets }: ProjectGroupProps) {
  return (
    <List label={label}>
      {projects.map((project, index) => {
        const meta = metaOf(project, snippets);
        return (
          <ListRow
            key={project.id}
            to={projectPath(project)}
            code={project.key}
            title={project.name}
            meta={meta.length === 0 ? undefined : meta}
            trailing={
              <>
                <span className="visually-hidden">我的角色：</span>
                {projectRoleLabel(project.myRole)}
              </>
            }
            isContentStart={isFirstGroup && index === 0}
          />
        );
      })}
    </List>
  );
}

/** 窄螢幕換成清單（換元件，不是把表格擠小：docs/DESIGN.md 6.3）。 */
export function ProjectGroup(props: ProjectGroupProps) {
  const isCompact = useIsCompact();
  return isCompact ? <ProjectList {...props} /> : <ProjectTable {...props} />;
}
