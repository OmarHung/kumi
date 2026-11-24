import type { ProjectBlueprint, Workflow } from '../../api/blueprint';
import type { SelectOption } from '../../components/Select/Select';
import { workflowForType } from '../../domain/blueprint';
import { boardWorkflows, workflowTypes } from '../../domain/workflow';
import { stateOption } from './selectOptionMarks';

/** 狀態選項與選項對應的狀態 key（同一個 key 在不同流程名稱不同時，選項的值要分得開）。 */
export interface StateChoices {
  readonly options: readonly SelectOption[];
  /** 選項的值 → 狀態 key；不是這份選項的值是 undefined。 */
  readonly stateKeyOf: (value: string) => string | undefined;
}

/** 同 key 不同名稱的第二個起，選項的值是 key 加上流程的 key，中間隔一個 NUL（藍圖的 key 不會有）。 */
const SEPARATOR = '\u0000';

/** 選項的值 → 狀態 key，不必查表：「我的工作」的值另外帶前綴，拆開前綴之後手上沒有這份選項。 */
export function stateKeyOfValue(value: string): string {
  return value.split(SEPARATOR, 1)[0] ?? value;
}

export interface StateGroup {
  readonly workflow: Workflow;
  /** 組的名字：多於一組時畫在每組第一項前面。 */
  readonly label: string;
}

/** 每組留下前面的組沒列過的狀態（同 key 同名稱），列不出狀態的組整組不要。 */
function freshGroups(groups: readonly StateGroup[]) {
  const seen = new Set<string>();
  return groups
    .map((group) => ({
      ...group,
      states: group.workflow.states.filter((state) => {
        const identity = `${state.key}${SEPARATOR}${state.name}`;
        if (seen.has(identity)) return false;
        seen.add(identity);
        return true;
      }),
    }))
    .filter((group) => group.states.length > 0);
}

/**
 * 依流程分組的狀態選項（批次改狀態與篩選共用）：只有一組時就是那組的狀態、不畫組名；多於一組時每組第一項帶組名。
 * 同 key 同名稱的狀態只列一次（放在第一個有它的流程底下）；同 key 不同名稱的各列一個，值分得開、對回同一個 key。
 */
export function groupedStateChoices(groups: readonly StateGroup[]): StateChoices {
  const fresh = freshGroups(groups);
  const isGrouped = fresh.length > 1;
  const stateKeys = new Map<string, string>();
  const options = fresh.flatMap(({ workflow, label, states }) =>
    states.map((state, index): SelectOption => {
      const value = stateKeys.has(state.key) ? `${state.key}${SEPARATOR}${workflow.key}` : state.key;
      stateKeys.set(value, state.key);
      return { ...stateOption(state), value, ...(isGrouped && index === 0 ? { groupLabel: label } : {}) };
    }),
  );
  return { options, stateKeyOf: (value) => stateKeys.get(value) };
}

/** 組名是用這個流程的類型（「任務、子任務」）：清單上看得到的是類型，對得起來。沒有類型在用時用流程的名稱。 */
function typeNamesOf(blueprint: ProjectBlueprint, workflow: Workflow): string {
  const names = workflowTypes(blueprint, workflow.key).map((itemType) => itemType.name);
  return names.length === 0 ? workflow.name : names.join('、');
}

/**
 * 篩選的狀態選項。選了類型時是那個類型的流程；沒選時是所有流程，多於一個就依流程分組
 * （使用者 2026-11-20：清單上的「完成」「取消」和篩選開頭的「需求確認」「報價」對不起來，兩個「內部驗收」也分不出來）。
 * 有類型在用的流程照類型的順序排前面，沒有類型在用的接在後面（網址上的狀態還是選得到）。
 * 後端用 key 篩選、不分流程，所以同 key 的兩個選項篩出來一樣。
 */
export function stateFilterChoices(blueprint: ProjectBlueprint, typeKey: string | null): StateChoices {
  if (typeKey !== null) {
    const workflow = workflowForType(blueprint, typeKey);
    return groupedStateChoices(workflow === undefined ? [] : [{ workflow, label: workflow.name }]);
  }
  const used = boardWorkflows(blueprint);
  const unused = blueprint.workflows.filter((workflow) => !used.some((candidate) => candidate.key === workflow.key));
  return groupedStateChoices([...used, ...unused].map((workflow) => ({ workflow, label: typeNamesOf(blueprint, workflow) })));
}
