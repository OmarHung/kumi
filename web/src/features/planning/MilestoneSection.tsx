import { useMilestones, usePlanningMutations, type Milestone } from '../../api/planning';
import { Badge } from '../../components/Badge/Badge';
import { formatDate } from '../../domain/dates';
import { useToday } from '../../lib/useToday';
import { projectPagePath } from '../work-items/itemLinks';
import { useRowDialog } from './dialogState';
import { MilestoneDialogs } from './MilestoneDialogs';
import { PlanningRow } from './PlanningRow';
import { PlanningSection } from './PlanningSection';
import { isMilestoneOverdue } from './planningRules';
import styles from './Planning.module.css';

interface MilestoneSectionProps {
  readonly projectKey: string;
  readonly canEdit: boolean;
}

/** 里程碑一節：依到期日列出；過了到期日還沒做完的標「已過期」（顏色之外，意思寫在文字裡）。 */
export function MilestoneSection({ projectKey, canEdit }: MilestoneSectionProps) {
  const query = useMilestones(projectKey);
  const mutations = usePlanningMutations(projectKey);
  const { dialog, openCreate, openEdit, openDelete, close } = useRowDialog<Milestone>();
  const today = useToday();

  return (
    <>
      <PlanningSection title="里程碑" query={query} canEdit={canEdit} emptyHint="里程碑是有到期日的目標，例如「v1.0 上線」。項目歸到里程碑之後，這裡看得到離目標還差多少。" onAdd={openCreate}>
        {(rows) => (
          <ul className={styles.list} aria-label="里程碑">
            {rows.map((milestone) => (
              <PlanningRow
                key={milestone.id}
                name={milestone.name}
                to={`${projectPagePath(projectKey)}?milestone=${encodeURIComponent(milestone.id)}`}
                badges={isMilestoneOverdue(milestone, today) ? <Badge tone="danger">已過期</Badge> : undefined}
                dates={`到期 ${formatDate(milestone.dueDate)}`}
                description={milestone.description}
                progress={milestone.progress}
                menu={canEdit ? { onEdit: () => { openEdit(milestone); }, onDelete: () => { openDelete(milestone); } } : undefined}
              />
            ))}
          </ul>
        )}
      </PlanningSection>
      <MilestoneDialogs dialog={dialog} today={today} mutations={mutations} onClose={close} />
    </>
  );
}
