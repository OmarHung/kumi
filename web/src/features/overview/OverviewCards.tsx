import { Link } from 'react-router';
import { StateBar } from './StateBar';
import { NumberCell, OpenFootnote, PlanCell, UpdatedCell } from './OverviewCells';
import { ATTENTION_LABEL, ATTENTION_NUMBERS, DONE_RECENTLY_NUMBER } from './overviewColumns';
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

/** 窄螢幕：一個專案一張卡片（不用橫向捲動的表格，docs/DESIGN.md 6.3），和表格同一套：需要注意一組、其他、進度合成一格。 */
export function OverviewCards({ projects, now }: OverviewListProps) {
  return (
    <ul className={styles.cards} aria-label="專案總覽">
      {projects.map((project, index) => (
        <li key={project.projectKey} className={styles.card} data-content-start={index === 0 || undefined}>
          <CardTitle project={project} />
          {project.ownItemsOnly && <p className={styles.projectMeta}>只算你負責或建立的項目</p>}
          <div className={styles.cardState}>
            <StateBar projectKey={project.projectKey} states={project.states} />
            <OpenFootnote project={project} />
          </div>
          <section className={styles.cardGroup}>
            <h3 className={styles.groupTitle}>{ATTENTION_LABEL}</h3>
            <dl className={styles.numbers}>
              {ATTENTION_NUMBERS.map((number) => (
                <div key={number.id} className={styles.numberItem}>
                  <dt>{number.label}</dt>
                  <dd>
                    <NumberCell project={project} number={number} />
                  </dd>
                </div>
              ))}
            </dl>
          </section>
          <dl className={styles.planning}>
            <div>
              <dt>{DONE_RECENTLY_NUMBER.label}</dt>
              <dd>
                <NumberCell project={project} number={DONE_RECENTLY_NUMBER} />
              </dd>
            </div>
            <div>
              <dt>進度</dt>
              <dd>
                <PlanCell project={project} />
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
