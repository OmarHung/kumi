import { Link } from 'react-router';
import { StateBar } from './StateBar';
import { CycleCell, MilestoneCell, NumberCell, UpdatedCell } from './OverviewCells';
import { OVERVIEW_NUMBERS } from './overviewColumns';
import type { ProjectOverview } from '../../api/overview';
import { useProjectPageLink } from '../projects/projectViewPreference';
import type { OverviewListProps } from './overviewTypes';
import styles from './Overview.module.css';

/** 卡片的標題：照上次用的版面開專案（和側欄、表格的名稱一樣）。 */
function CardTitle({ project }: { readonly project: ProjectOverview }) {
  const to = useProjectPageLink(project.projectKey);
  return (
    <h2 className={styles.cardTitle}>
      <Link to={to} className={styles.projectLink}>
        <span className={styles.code}>{project.projectKey}</span> <span>{project.name}</span>
      </Link>
    </h2>
  );
}

/** 窄螢幕：一個專案一張卡片（不用橫向捲動的表格，docs/DESIGN.md 6.3）；數字一格一格換行排。 */
export function OverviewCards({ projects, now }: OverviewListProps) {
  return (
    <ul className={styles.cards} aria-label="專案總覽">
      {projects.map((project, index) => (
        <li key={project.projectKey} className={styles.card} data-content-start={index === 0 || undefined}>
          <CardTitle project={project} />
          {project.ownItemsOnly && <p className={styles.projectMeta}>只算你負責或建立的項目</p>}
          <StateBar projectKey={project.projectKey} states={project.states} />
          <dl className={styles.numbers}>
            {OVERVIEW_NUMBERS.map((number) => (
              <div key={number.id} className={styles.numberItem}>
                <dt>{number.label}</dt>
                <dd>
                  <NumberCell project={project} number={number} />
                </dd>
              </div>
            ))}
          </dl>
          <dl className={styles.planning}>
            <div>
              <dt>這期週期</dt>
              <dd>
                <CycleCell projectKey={project.projectKey} cycle={project.currentCycle} />
              </dd>
            </div>
            <div>
              <dt>下一個里程碑</dt>
              <dd>
                <MilestoneCell projectKey={project.projectKey} milestone={project.nextMilestone} />
              </dd>
            </div>
            <div>
              <dt>最近更新</dt>
              <dd>
                <UpdatedCell at={project.lastUpdatedAt} now={now} />
              </dd>
            </div>
          </dl>
        </li>
      ))}
    </ul>
  );
}
