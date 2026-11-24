import { useRef, useState, type Dispatch, type RefObject, type SetStateAction } from 'react';
import type { Item, ItemPatch, ProjectDetail } from '../../../api/types';
import { updateThenFocus } from '../../../lib/updateThenFocus';
import { useProjectTable } from '../ProjectTableContext';
import { fieldFocusTarget, fillFieldsFor, type FillField } from './fillFields';
import { clearedFields, fieldErrorsOf, missingFieldPaths, planFill, type FieldMessages } from './fillPlan';
import { useItemCommand, type CellCommand } from './useItemCommand';

/** 一次被「要先填欄位」擋下的改狀態：這個項目（被擋下時的樣子）要移到 to，後端說要先填 fields（path）。 */
export interface FillRequest {
  readonly item: Item;
  readonly to: string;
  readonly fields: readonly string[];
}

/** 送改狀態的方式：表格與項目頁用 useItemCommand；看板用 useBoardMoves（成功時走看板自己的路：更新快取、落點閃一下）。 */
export type SendTransition = (item: Item, to: string) => Promise<Item>;

/** 對話框上方的說明：欄位沒存好（對不到欄位的訊息），或欄位存好了、改狀態又被別的原因擋下。 */
export type FillProblem =
  | { readonly kind: 'patch'; readonly message: string | null }
  | { readonly kind: 'blocked'; readonly error: unknown };

interface FillState {
  /** 最後一次存好的樣子：再按一次時拿它比對有沒有改、拿它送改狀態。 */
  readonly latest: Item;
  readonly paths: readonly string[];
  /** 這次已經存好的欄位：沒再改就不必重送。 */
  readonly saved: ReadonlySet<string>;
  readonly errors: FieldMessages;
  readonly problem: FillProblem | null;
  readonly isSending: boolean;
  /** 又因缺欄位被擋下的次數：欄位重新掛上，草稿回到（清空的）基準。 */
  readonly round: number;
}

interface FillContext {
  readonly fields: readonly FillField[];
  readonly to: string;
  readonly detail: ProjectDetail;
  readonly patch: (patch: ItemPatch) => Promise<Item>;
  readonly send: SendTransition;
  readonly setState: Dispatch<SetStateAction<FillState>>;
  /** 存好之後：草稿和存好的值一樣了，再按一次不必重送 PATCH。 */
  readonly clearCommands: () => void;
  readonly onMoved: (moved: Item, filled: readonly string[]) => void;
  /** 欄位的 id（第一個有問題的欄位要拿到焦點）。 */
  readonly idOf: (key: string) => string;
}

/** 先讓畫面更新完，再把焦點放到這個欄位（多選是它的第一個勾選框）。 */
function updateThenFocusField(context: FillContext, update: () => void, key: string | undefined): void {
  updateThenFocus(update, key === undefined ? [] : [fieldFocusTarget(context.idOf(key))]);
}

async function savePatch(context: FillContext, patch: ItemPatch): Promise<Item | null> {
  try {
    const updated = await context.patch(patch);
    const keys = context.fields.map((field) => field.key);
    context.clearCommands();
    context.setState((current) => ({ ...current, latest: updated, saved: new Set([...current.saved, ...keys]) }));
    return updated;
  } catch (error) {
    const mapped = fieldErrorsOf(error, context.fields);
    context.setState((current) => ({
      ...current,
      isSending: false,
      errors: mapped.fieldErrors,
      problem: { kind: 'patch', message: mapped.formError },
    }));
    return null;
  }
}

/**
 * 又是「要先填欄位」（例如流程剛改過、多要了一個，或剛存的值後端還是當成空的）：清單換成新缺的欄位，以後端為準
 * 把它們當成空的（基準清空、從「已經存好」拿掉、欄位重新掛上），焦點到新清單的第一個欄位。
 */
function showRefill(context: FillContext, error: unknown, item: Item, paths: readonly string[]): void {
  const baseline = clearedFields(item, paths);
  const fields = fillFieldsFor(paths, baseline, context.detail) ?? [];
  context.clearCommands();
  updateThenFocusField(
    context,
    () => {
      context.setState((current) => ({
        ...current,
        isSending: false,
        problem: { kind: 'blocked', error },
        latest: baseline,
        paths,
        saved: new Set([...current.saved].filter((key) => !paths.includes(key))),
        errors: fieldErrorsOf(error, fields).fieldErrors,
        round: current.round + 1,
      }));
    },
    paths[0],
  );
}

/**
 * 欄位存好了，改狀態卻又被擋下：說明留在對話框。又是缺欄位而且都有編輯器時換成新的清單（showRefill）；
 * 別的原因（角色、流程）照表格的說明，焦點留在按鈕上。
 */
function showBlocked(context: FillContext, error: unknown, item: Item): void {
  const paths = missingFieldPaths(error);
  if (paths !== null && paths.length > 0 && fillFieldsFor(paths, item, context.detail) !== null) {
    showRefill(context, error, item, paths);
    return;
  }
  context.setState((current) => ({ ...current, isSending: false, problem: { kind: 'blocked', error }, errors: {} }));
}

/** 先 PATCH 欄位（有修改才送），成功才送改狀態。兩個請求不是一次完成的，每一步失敗都留在對話框說明。 */
async function runFill(context: FillContext, patch: ItemPatch | null, latest: Item): Promise<void> {
  const item = patch === null ? latest : await savePatch(context, patch);
  if (item === null) return;
  try {
    const moved = await context.send(item, context.to);
    context.onMoved(
      moved,
      context.fields.map((field) => field.label),
    );
  } catch (error) {
    showBlocked(context, error, item);
  }
}

interface SubmitRefs {
  readonly commands: RefObject<ReadonlyMap<string, CellCommand>>;
  readonly isBusy: RefObject<boolean>;
}

function submitFill(context: FillContext, state: FillState, refs: SubmitRefs): void {
  if (refs.isBusy.current) return;
  const plan = planFill(context.fields, refs.commands.current, state.saved);
  if (plan.kind === 'invalid') {
    const first = context.fields.find((field) => plan.errors[field.key] !== undefined);
    // 還沒填、填錯：焦點到第一個有問題的欄位，報讀軟體接著唸它的說明（aria-describedby）。
    // 上方的說明（例如欄位已經存好、還要多填一個）留著：它說的事還是真的。
    updateThenFocusField(
      context,
      () => {
        context.setState((current) => ({ ...current, errors: plan.errors }));
      },
      first?.key,
    );
    return;
  }
  refs.isBusy.current = true;
  context.setState((current) => ({ ...current, isSending: true, errors: {}, problem: null }));
  void runFill(context, plan.patch, state.latest).finally(() => {
    refs.isBusy.current = false;
  });
}

interface FillSubmitOptions {
  readonly request: FillRequest;
  readonly transition: SendTransition | undefined;
  readonly onMoved: (moved: Item, filled: readonly string[]) => void;
  /** 欄位的 id（第一個有問題的欄位要拿到焦點）。 */
  readonly idOf: (key: string) => string;
}

/** 補填對話框的狀態與送出：欄位（照後端回的順序）、每個欄位的錯誤、上方的說明、送出中。 */
export function useFillSubmit({ request, transition, onMoved, idOf }: FillSubmitOptions) {
  const { detail } = useProjectTable();
  const command = useItemCommand(request.item.key);
  const [state, setState] = useState<FillState>(() => ({
    latest: request.item,
    paths: request.fields,
    saved: new Set(),
    errors: {},
    problem: null,
    isSending: false,
    round: 0,
  }));
  const commands = useRef<ReadonlyMap<string, CellCommand>>(new Map());
  const isBusy = useRef(false);
  const fields = fillFieldsFor(state.paths, state.latest, detail) ?? [];
  const context: FillContext = {
    fields,
    to: request.to,
    detail,
    patch: (patch) => command.mutateAsync({ kind: 'patch', patch }),
    send: transition ?? ((_item, to) => command.mutateAsync({ kind: 'transition', to })),
    setState,
    clearCommands: () => {
      commands.current = new Map();
    },
    onMoved,
    idOf,
  };
  return {
    fields,
    errors: state.errors,
    problem: state.problem,
    isSending: state.isSending,
    hasSaved: state.saved.size > 0,
    latest: state.latest,
    round: state.round,
    setCommand: (key: string, next: CellCommand) => {
      commands.current = new Map([...commands.current, [key, next]]);
      if (state.errors[key] !== undefined) {
        setState((current) => ({ ...current, errors: Object.fromEntries(Object.entries(current.errors).filter(([other]) => other !== key)) }));
      }
    },
    submit: () => {
      submitFill(context, state, { commands, isBusy });
    },
  };
}
