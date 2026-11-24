import type { AiConnection, AiEffort, AiSelection } from '../../api/aiTypes';
import { DEFAULT_EFFORT_LABEL, effortLabel } from '../ai/aiLabels';

/** 預設的「模型」選單的一個選項：開放給成員的模型（連線＋模型代號決定是哪一個）。 */
export interface ModelChoice {
  readonly value: string;
  readonly label: string;
  readonly selection: Pick<AiSelection, 'connectionId' | 'model'>;
  readonly efforts: readonly AiEffort[];
}

const SEPARATOR = '\u0000';

export function choiceValue(connectionId: string, model: string): string {
  return `${connectionId}${SEPARATOR}${model}`;
}

/** 只有一個連線時只寫模型名稱；有好幾個時寫「連線名稱 · 模型名稱」才分得出來。 */
export function modelChoices(connections: readonly AiConnection[]): readonly ModelChoice[] {
  const isSingle = connections.length === 1;
  return connections.flatMap((connection) =>
    connection.models
      .filter((model) => model.enabled)
      .map((model) => ({
        value: choiceValue(connection.id, model.id),
        label: isSingle ? model.displayName : `${connection.name} · ${model.displayName}`,
        selection: { connectionId: connection.id, model: model.id },
        efforts: model.efforts,
      })),
  );
}

export function findChoice(choices: readonly ModelChoice[], selection: AiSelection | null): ModelChoice | undefined {
  if (selection === null) return undefined;
  const value = choiceValue(selection.connectionId, selection.model);
  return choices.find((choice) => choice.value === value);
}

/** 思考深度的選項：「預設」（不指定）加這個模型能選的，由淺到深。 */
export function effortOptions(choice: ModelChoice | undefined): readonly { readonly value: string; readonly label: string }[] {
  const efforts = choice?.efforts ?? [];
  return [{ value: '', label: DEFAULT_EFFORT_LABEL }, ...efforts.map((effort) => ({ value: effort, label: effortLabel(effort) }))];
}

/** 換了模型：原本的思考深度新的模型也能選就留著，不能選就回到「預設」。 */
export function selectionFor(choice: ModelChoice, previousEffort: AiEffort | null): AiSelection {
  const effort = previousEffort !== null && choice.efforts.includes(previousEffort) ? previousEffort : null;
  return { ...choice.selection, effort };
}

export function sameSelection(left: AiSelection | null, right: AiSelection | null): boolean {
  if (left === null || right === null) return left === right;
  return left.connectionId === right.connectionId && left.model === right.model && left.effort === right.effort;
}

/** 畫面上有效的預設：選的模型已經不在開放的清單裡（連線刪了、模型關了）就是沒選；模型不能選這個思考深度時回到「預設」。 */
export function effectiveSelection(choices: readonly ModelChoice[], selection: AiSelection | null): AiSelection | null {
  const choice = findChoice(choices, selection);
  if (choice === undefined || selection === null) return null;
  return selectionFor(choice, selection.effort);
}
