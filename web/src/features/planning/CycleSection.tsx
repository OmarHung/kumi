import { useCycles, usePlanningMutations, type Cycle } from '../../api/planning';
import { Badge } from '../../components/Badge/Badge';
import { formatDate } from '../../domain/dates';
import { useToday } from '../../lib/useToday';
import { projectPagePath } from '../work-items/itemLinks';
import { CycleDialogs } from './CycleDialogs';
import { useRowDialog } from './dialogState';
import { PlanningRow } from './PlanningRow';
import { PlanningSection } from './PlanningSection';
import { cycleStatusOf, type CycleStatus } from './planningRules';
import styles from './Planning.module.css';

const STATUS_LABELS: Readonly<Record<CycleStatus, string>> = { active: '進行中', upcoming: '之後', ended: '已結束' };

interface CycleSectionProps {
  readonly projectKey: string;
  readonly canEdit: boolean;
}

/** 週期一節：依開始日列出，狀態用使用者的「今天」判斷（伺服器不知道使用者的時區）。 */
export function CycleSection({ projectKey, canEdit }: CycleSectionProps) {
  const query = useCycles(projectKey);
  const mutations = usePlanningMutations(projectKey);
  const { dialog, openCreate, openEdit, openDelete, close } = useRowDialog<Cycle>();
  const today = useToday();
  const cycles = query.data ?? [];

  return (
    <>
      <PlanningSection title="週期" query={query} canEdit={canEdit} emptyHint="週期是一段有開始日與結束日的時間，例如兩週一輪的工作。項目排進週期之後，這裡看得到每個週期做完了多少。" isContentStart onAdd={openCreate}>
        {(rows) => (
          <ul className={styles.list} aria-label="週期">
            {rows.map((cycle) => (
              <PlanningRow
                key={cycle.id}
                name={cycle.name}
                to={`${projectPagePath(projectKey)}?cycle=${encodeURIComponent(cycle.id)}`}
                badges={<Badge tone={cycleStatusOf(cycle, today) === 'active' ? 'accent' : 'neutral'}>{STATUS_LABELS[cycleStatusOf(cycle, today)]}</Badge>}
                dates={`${formatDate(cycle.startDate)} – ${formatDate(cycle.endDate)}`}
                progress={cycle.progress}
                menu={canEdit ? { onEdit: () => { openEdit(cycle); }, onDelete: () => { openDelete(cycle); } } : undefined}
              />
            ))}
          </ul>
        )}
      </PlanningSection>
      <CycleDialogs dialog={dialog} cycles={cycles} today={today} mutations={mutations} onClose={close} />
    </>
  );
}
