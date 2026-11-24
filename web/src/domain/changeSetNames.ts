import type { ProjectBlueprint } from '../api/blueprint';
import type { ChangeResource } from '../api/types';
import { projectRoleLabel } from './labels';

/**
 * 變更集的差異與影響只給 key；名稱要從藍圖找。新增的看變更集想要的藍圖（after），刪除的看基準版本的藍圖（before，
 * 變更集的 baseBlueprint），修改的先看 before（說「把「舊名」改名為…」），都找不到才用 key。
 * before 一定要用基準版本的：已套用的變更集，專案目前的藍圖已經是 after，拿它來比會說成「從 X 改成 X」。
 */
export interface ChangeSetBlueprints {
  readonly before: ProjectBlueprint;
  readonly after: ProjectBlueprint;
}

function workflowOf(blueprint: ProjectBlueprint, key: string | null) {
  return blueprint.workflows.find((workflow) => workflow.key === key);
}

function lookup(blueprint: ProjectBlueprint, resource: ChangeResource, key: string, parentKey: string | null): string | undefined {
  switch (resource) {
    case 'field':
      return blueprint.fields.find((field) => field.key === key)?.name;
    case 'field_option':
      return blueprint.fields.find((field) => field.key === parentKey)?.options?.find((option) => option.key === key)?.name;
    case 'workflow':
      return workflowOf(blueprint, key)?.name;
    case 'state':
      return workflowOf(blueprint, parentKey)?.states.find((state) => state.key === key)?.name;
    case 'item_type':
      return blueprint.itemTypes.find((type) => type.key === key)?.name;
    case 'view':
      return blueprint.views?.find((view) => view.key === key)?.name;
    case 'automation':
      return blueprint.automations?.find((automation) => automation.key === key)?.name;
    case 'role':
      return blueprint.roles?.find((role) => role.key === key)?.name;
    case 'metadata':
      return blueprint.metadata.name;
    default:
      return undefined;
  }
}

/** 先看 first，再看 second，都找不到用 key。 */
export function nameIn(
  first: ProjectBlueprint,
  second: ProjectBlueprint,
  resource: ChangeResource,
  key: string,
  parentKey: string | null = null,
): string {
  return lookup(first, resource, key, parentKey) ?? lookup(second, resource, key, parentKey) ?? key;
}

/** 角色：內建的用中文，自訂的先看基準版本的藍圖、再看變更集的。 */
export function roleName({ before, after }: ChangeSetBlueprints, role: string): string {
  const label = projectRoleLabel(role, before.roles);
  return label === role ? projectRoleLabel(role, after.roles) : label;
}

/** 專案有好幾個流程時，狀態與轉換後面帶流程的名稱，才分得出是哪一個流程的。 */
export function workflowSuffix({ before, after }: ChangeSetBlueprints, workflowKey: string | null): string {
  if (workflowKey === null || Math.max(before.workflows.length, after.workflows.length) < 2) {
    return '';
  }
  return `（${nameIn(after, before, 'workflow', workflowKey)}）`;
}
