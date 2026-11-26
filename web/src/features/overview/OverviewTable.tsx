import { Link } from 'react-router';
import type { ProjectOverview } from '../../api/overview';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { cx } from '../../lib/cx';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { StateBar } from './StateBar';
import { CycleCell, MilestoneCell, NumberCell, UpdatedCell } from './OverviewCells';
import { OVERVIEW_NUMBERS } from './overviewColumns';
import type { OverviewListProps } from './overviewTypes';
import styles from './Overview.module.css';

/** 專案的名稱列：代號、名稱（連到專案）。只看得到自己項目的專案多一句，說明數字只算那些。 */
export function ProjectName({ project, now }: { readonly project: ProjectOverview; readonly now?: number }) {
  // 名稱照上次用的版面開（和側欄一樣）；數字的連結固定開表格。
  const to = useProjectPageLink(project.projectKey);
  return (
    <>
      <Link to={to} className={cx(tableStyles.rowLink, styles.projectLink)}>
        <span className={tableStyles.mono}>{project.projectKey}</span> <span>{project.name}</span>
      </Link>
      {project.ownItemsOnly && <span className={styles.projectMeta}>只算你負責或建立的項目</span>}
      {now !== undefined && project.lastUpdatedAt !== null && (
        <span className={styles.projectMeta}>
          更新於 <UpdatedCell at={project.lastUpdatedAt} now={now} />
        </span>
      )}
    </>
  );
}

/** 寬螢幕：一個專案一列，欄是狀態分布、各個數字、這期週期、下一個里程碑；最近更新寫在專案名稱下面（少一欄才放得下）。 */
export function OverviewTable({ projects, now }: OverviewListProps) {
  return (
    <TableScroll label="專案總覽" layout="page">
      <table className={tableStyles.table}>
        <caption className="visually-hidden">專案總覽</caption>
        <thead>
          <tr>
            <th scope="col">專案</th>
            <th scope="col">狀態分布</th>
            {OVERVIEW_NUMBERS.map((number) => (
              <th key={number.id} scope="col" className={styles.numberHead} title={number.description}>
                {number.head.map((line) => (
                  <span key={line} className={styles.headLine}>
                    {line}
                  </span>
                ))}
              </th>
            ))}
            <th scope="col">這期週期</th>
            <th scope="col">下一個里程碑</th>
          </tr>
        </thead>
        <tbody>
          {projects.map((project, index) => (
            // 第一列標上 data-content-start：給 scripts/ui-audit.js 量第一筆內容離頂端多遠（docs/DESIGN.md 6.2）。
            <tr key={project.projectKey} data-content-start={index === 0 || undefined}>
              <th scope="row" className={styles.projectCell}>
                <ProjectName project={project} now={now} />
              </th>
              <td className={styles.barCell}>
                <StateBar projectKey={project.projectKey} states={project.states} />
              </td>
              {OVERVIEW_NUMBERS.map((number) => (
                <td key={number.id} className={styles.numberCell}>
                  <NumberCell project={project} number={number} />
                </td>
              ))}
              <td className={styles.plannedCell}>
                <CycleCell projectKey={project.projectKey} cycle={project.currentCycle} />
              </td>
              <td className={styles.plannedCell}>
                <MilestoneCell projectKey={project.projectKey} milestone={project.nextMilestone} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </TableScroll>
  );
}
