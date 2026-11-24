import type { ProjectBlueprint } from '../../api/blueprint';
import { projectRoleLabel } from '../../domain/labels';
import type { MoveAvailability } from '../../domain/workflow';

/** 預先判斷走不到的原因，給「移到…」選單與拖曳時的欄用；走得到是 null。 */
export function blockedReason(availability: MoveAvailability, blueprint: ProjectBlueprint): string | null {
  switch (availability.kind) {
    case 'open':
      return null;
    case 'no_path':
      return '流程沒有這條路';
    case 'role':
      return `需要的角色：${availability.roles.map((role) => projectRoleLabel(role, blueprint.roles)).join('、')}`;
  }
}
