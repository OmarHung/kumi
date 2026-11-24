import { errorMessage, isApiError, type ApiError } from '../../../api/http';
import type { Item, ProjectDetail } from '../../../api/types';
import { Button } from '../../../components/Button/Button';
import { workflowForType } from '../../../domain/blueprint';
import { fieldLabel } from '../../../domain/fields';
import { projectRoleLabel } from '../../../domain/labels';
import { findTransition } from '../../../domain/workflow';
import { useProjectTable } from '../ProjectTableContext';
import styles from './Cells.module.css';

export const TransitionErrorCodes = {
  notAllowed: 'item.transition_not_allowed',
  roleRequired: 'item.transition_role_required',
  fieldsRequired: 'item.transition_fields_required',
} as const;

interface TransitionProblemProps {
  readonly error: ApiError;
  readonly item: Item;
  readonly to: string | undefined;
  readonly onOpenMembers?: () => void;
}

/** 需要的角色：先用藍圖的定義（有中文名稱），找不到時才用後端細項的文字。 */
function requiredRoles({ error, item, to }: TransitionProblemProps, detail: ProjectDetail): string[] {
  const workflow = workflowForType(detail.blueprint, item.type);
  const transition = workflow && to !== undefined ? findTransition(workflow, item.state, to) : undefined;
  const roles = transition?.allowedRoles ?? [];
  return roles.length > 0
    ? roles.map((key) => projectRoleLabel(key, detail.blueprint.roles))
    : error.details.map((entry) => entry.message);
}

function RoleProblem(props: TransitionProblemProps) {
  const { detail, can, onOpenMembers: openMembers } = useProjectTable();
  const onOpenMembers = props.onOpenMembers ?? openMembers;
  const roles = requiredRoles(props, detail);
  return (
    <>
      <p>{props.error.message}</p>
      <dl className={styles.facts}>
        <dt>需要的角色</dt>
        <dd>{roles.join('、')}</dd>
        <dt>你的角色</dt>
        <dd>{projectRoleLabel(detail.project.myRole, detail.blueprint.roles)}</dd>
      </dl>
      {can.manage && (
        <Button size="sm" onClick={onOpenMembers}>
          到「成員」調整角色
        </Button>
      )}
    </>
  );
}

/**
 * 要先填哪些欄位。能改這個項目的人一般不會看到這裡（改成開補填的對話框，fillFields.tsx）；
 * 看到的是不能改欄位的人，所以說要找誰（docs/DESIGN.md 8.1 第 3 點：不留死路）。
 */
function FieldsProblem({ error }: TransitionProblemProps) {
  const { detail, can } = useProjectTable();
  const fields = error.details.map((entry) => (entry.path === null ? entry.message : fieldLabel(detail.blueprint, entry.path)));
  return (
    <>
      <p>{error.message}</p>
      {fields.length > 0 && (
        <dl className={styles.facts}>
          <dt>要先填的欄位</dt>
          <dd>{fields.join('、')}</dd>
        </dl>
      )}
      {!can.update && <p>你不能修改這個項目的欄位，請有權限修改項目的人先填好。</p>}
    </>
  );
}

interface Props {
  readonly error: unknown;
  readonly item: Item;
  /** 要轉到的狀態：找藍圖裡這條轉換允許的角色。 */
  readonly to: string | undefined;
  /** 「到『成員』調整角色」：預設打開專案的成員對話框；補填對話框裡要先關上自己（不在對話框上再開對話框）。 */
  readonly onOpenMembers?: () => void;
}

/**
 * 改狀態被擋下的說明，表格、項目頁、看板、補填對話框共用：流程沒有這條路（後端的說明）、需要哪些角色（可以去調整）、
 * 要先填哪些欄位。
 */
export function TransitionProblem({ error, item, to, onOpenMembers }: Props) {
  if (!isApiError(error)) {
    return <p>{errorMessage(error)}</p>;
  }
  switch (error.code) {
    case TransitionErrorCodes.roleRequired:
      return <RoleProblem error={error} item={item} to={to} onOpenMembers={onOpenMembers} />;
    case TransitionErrorCodes.fieldsRequired:
      return <FieldsProblem error={error} item={item} to={to} />;
    default:
      return <p>{error.message}</p>;
  }
}
