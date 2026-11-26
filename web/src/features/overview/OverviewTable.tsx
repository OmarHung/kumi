import { Link } from 'react-router';
import type { ProjectOverview } from '../../api/overview';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { cx } from '../../lib/cx';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { StateBar } from './StateBar';
import { NumberCell, OpenFootnote, PlanCell, UpdatedCell } from './OverviewCells';
import { ATTENTION_LABEL, ATTENTION_NUMBERS, DONE_RECENTLY_NUMBER, type OverviewNumber } from './overviewColumns';
import type { OverviewListProps } from './overviewTypes';
import styles from './Overview.module.css';

/** 專案的名稱列：代號、名稱（連到專案）；下面一行小字：更新於、只算你負責或建立的項目（只看得到自己項目的專案，數字只算那些）。 */
export function ProjectName({ project, now }: { readonly project: ProjectOverview; readonly now?: number }) {
  // 名稱照上次用的版面開（和側欄一樣）；數字的連結固定開表格。
  const to = useProjectPageLink(project.projectKey);
  const hasUpdated = now !== undefined && project.lastUpdatedAt !== null;
  return (
    <>
      <Link to={to} className={cx(tableStyles.rowLink, styles.projectLink)}>
        <span className={tableStyles.mono}>{project.projectKey}</span> <span>{project.name}</span>
      </Link>
      {(hasUpdated || project.ownItemsOnly) && (
        <span className={styles.projectMeta}>
          {hasUpdated && (
            <span>
              更新於 <UpdatedCell at={project.lastUpdatedAt} now={now} />
            </span>
          )}
          {project.ownItemsOnly && <span>只算你負責或建立的項目</span>}
        </span>
      )}
    </>
  );
}

/** 數字欄的表頭；isSpanning 是不在「需要注意」底下、跨兩列的那一欄（近 7 天完成）。 */
function NumberHead({ number, isSpanning = false }: { readonly number: OverviewNumber; readonly isSpanning?: boolean }) {
  return (
    <th scope="col" rowSpan={isSpanning ? 2 : undefined} className={cx(styles.numberHead, styles.numberCol)} title={number.description}>
      {number.head.map((line) => (
        <span key={line} className={styles.headLine}>
          {line}
        </span>
      ))}
    </th>
  );
}

function OverviewHead() {
  return (
    <thead>
      <tr>
        <th scope="col" rowSpan={2}>
          專案
        </th>
        <th scope="col" rowSpan={2}>
          狀態
        </th>
        <th scope="colgroup" colSpan={ATTENTION_NUMBERS.length} className={styles.groupHead}>
          {ATTENTION_LABEL}
        </th>
        <NumberHead number={DONE_RECENTLY_NUMBER} isSpanning />
        <th scope="col" rowSpan={2} className={styles.planCol}>
          進度
        </th>
      </tr>
      <tr>
        {ATTENTION_NUMBERS.map((number) => (
          <NumberHead key={number.id} number={number} />
        ))}
      </tr>
    </thead>
  );
}

function OverviewRow({ project, now, isFirst }: { readonly project: ProjectOverview; readonly now: number; readonly isFirst: boolean }) {
  return (
    // 第一列標上 data-content-start：給 scripts/ui-audit.js 量第一筆內容離頂端多遠（docs/DESIGN.md 6.2）。
    <tr data-content-start={isFirst || undefined}>
      <th scope="row" className={styles.projectCell}>
        <ProjectName project={project} now={now} />
      </th>
      <td className={styles.barCell}>
        <StateBar projectKey={project.projectKey} states={project.states} />
        <OpenFootnote project={project} />
      </td>
      {[...ATTENTION_NUMBERS, DONE_RECENTLY_NUMBER].map((number) => (
        <td key={number.id} className={cx(styles.numberCell, styles.numberCol)}>
          <NumberCell project={project} number={number} />
        </td>
      ))}
      <td className={styles.planCol}>
        <PlanCell project={project} />
      </td>
    </tr>
  );
}

/**
 * 寬螢幕：一個專案一列，七欄：專案、狀態（橫條加還沒結束的註腳）、需要注意（兩層表頭，底下四個數字）、近 7 天完成、進度（週期與里程碑合成一欄）。
 * 專案與狀態欄吃掉多出來的寬度，數字欄與進度欄固定寬。表頭的斷行是寫死的（overviewColumns 的 head）。
 */
export function OverviewTable({ projects, now }: OverviewListProps) {
  return (
    <TableScroll label="專案總覽" layout="page">
      <table className={cx(tableStyles.table, styles.table)}>
        <caption className="visually-hidden">專案總覽</caption>
        <OverviewHead />
        <tbody>
          {projects.map((project, index) => (
            <OverviewRow key={project.projectKey} project={project} now={now} isFirst={index === 0} />
          ))}
        </tbody>
      </table>
    </TableScroll>
  );
}
