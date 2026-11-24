import { useState, type ReactNode, type RefObject } from 'react';
import type { FieldDefinition } from '../../../api/blueprint';
import type { Item, ProjectDetail } from '../../../api/types';
import { FieldShell } from '../../../components/Field/Field';
import { fieldDefinition, typeAllowsParent } from '../../../domain/fields';
import { useProjectTable } from '../ProjectTableContext';
import { assigneeSpec, dateSpec, estimateSpec, parentSpec, prioritySpec } from './CoreCells';
import { MAX_DESCRIPTION_LENGTH } from './drafts';
import type { FieldEditSpec } from './EditableCell';
import { TextAreaEditor } from './editors';
import { checkboxFieldSpec, multiSelectFieldSpec, textFieldSpec } from './FieldCells';
import { missingFieldPaths, type FillFieldRef } from './fillPlan';
import { patchCommand, UNCHANGED, type CellCommand } from './useItemCommand';

/** 補填對話框畫一個欄位時給的東西。草稿由欄位自己管，每次改都回報目前要送的修改（onCommand）。 */
export interface FillFieldRowProps {
  readonly id: string;
  readonly error: string | undefined;
  readonly isSaving: boolean;
  readonly onCommand: (command: CellCommand) => void;
  /** 在欄位裡按 Enter（長文字是 Ctrl+Enter）：和按「填好並移到…」一樣。 */
  readonly onSubmit: () => void;
}

export interface FillField extends FillFieldRef {
  readonly render: (props: FillFieldRowProps) => ReactNode;
}

const ignoreCancel = (): void => undefined;

/**
 * 一個欄位：表單欄位的外框（components/Field 的 FieldShell：看得到的標籤、錯誤用 aria-describedby 連到編輯器），
 * 裡面是表格的那個編輯器。多選與勾選自己帶標籤（legend、勾選框旁邊的字），外框不再畫 label。
 */
function FillFieldRow<TDraft>({ spec, id, error, isSaving, onCommand, onSubmit }: FillFieldRowProps & { readonly spec: FieldEditSpec<TDraft> }) {
  const [draft, setDraft] = useState<TDraft>(spec.initialDraft);
  const editor = (describedBy: string | undefined) =>
    spec.renderEditor({
      label: spec.label,
      draft,
      setDraft: (next) => {
        setDraft(next);
        onCommand(spec.toCommand(next));
      },
      // 格子裡離開就存；對話框裡離開不算，Enter 才是送出。
      commit: (reason) => {
        if (reason === 'enter') onSubmit();
      },
      // Esc 交給對話框（關上），編輯器自己不必做什麼。
      cancel: ignoreCancel,
      isSaving,
      isInvalid: error !== undefined,
      describedBy,
      placement: 'dialog',
      id,
    });
  return (
    <FieldShell label={spec.label} error={error} id={id} hasOwnLabel={spec.hasOwnLabel === true}>
      {(control) => editor(control.describedBy)}
    </FieldShell>
  );
}

/** 把有型別的編輯方式包成對話框用的欄位（每個欄位的草稿型別不同：字串、字串陣列、勾選）。 */
function bindSpec<TDraft>(key: string, spec: FieldEditSpec<TDraft>): FillField {
  return {
    key,
    label: spec.label,
    errorPaths: spec.errorPaths,
    render: (props) => <FillFieldRow spec={spec} {...props} />,
  };
}

/** 說明不是表格的格子（在項目頁），這裡用同一個長文字的編輯器。後端說缺，就是現在沒有說明。 */
function descriptionSpec(): FieldEditSpec<string> {
  return {
    label: '說明',
    errorPaths: ['description'],
    initialDraft: () => '',
    toCommand: (draft) => (draft.trim() === '' ? UNCHANGED : patchCommand({ description: draft })),
    renderEditor: (api) => <TextAreaEditor api={api} maxLength={MAX_DESCRIPTION_LENGTH} />,
  };
}

function customField(key: string, item: Item, definition: FieldDefinition, detail: ProjectDetail): FillField {
  switch (definition.type) {
    case 'checkbox':
      return bindSpec(key, checkboxFieldSpec(item, definition));
    case 'multi_select':
      return bindSpec(key, multiSelectFieldSpec(item, definition));
    default:
      return bindSpec(key, textFieldSpec(item, definition, detail.members));
  }
}

/** 後端的 path（轉換的 requireFields 用的名字）對到表格的編輯方式；對不到的回 undefined。 */
function fillFieldFor(path: string, item: Item, detail: ProjectDetail): FillField | undefined {
  switch (path) {
    case 'assignee':
      return bindSpec(path, assigneeSpec(item, detail.members));
    case 'priority':
      return bindSpec(path, prioritySpec(item));
    case 'start_date':
      return bindSpec(path, dateSpec(item, 'startDate'));
    case 'due_date':
      return bindSpec(path, dateSpec(item, 'dueDate'));
    case 'estimate':
      return bindSpec(path, estimateSpec(item));
    case 'description':
      return bindSpec(path, descriptionSpec());
    case 'parent':
      return typeAllowsParent(detail.blueprint, item.type) ? bindSpec(path, parentSpec(item)) : undefined;
    default: {
      const definition = fieldDefinition(detail.blueprint, path);
      return definition === undefined ? undefined : customField(path, item, definition, detail);
    }
  }
}

/** 每個缺的欄位都有編輯器才回清單；有一個對不到（例如藍圖剛改過）就回 null，照舊只說明缺什麼。 */
export function fillFieldsFor(paths: readonly string[], item: Item, detail: ProjectDetail): readonly FillField[] | null {
  const fields = paths.map((path) => fillFieldFor(path, item, detail));
  return fields.every((field) => field !== undefined) ? fields : null;
}

const MODAL_DIALOG = '[role="dialog"][aria-modal="true"]';

/**
 * 已經有強制回應的對話框開著：不在上面再開一個（docs/DESIGN.md 6.2），照舊在原地說明。
 * 由呼叫端判斷：看板自己的補填對話框開著時，後面被擋下的排隊等它關上，不算「別的對話框」。
 */
export function isModalDialogOpen(): boolean {
  return document.querySelector(MODAL_DIALOG) !== null;
}

/**
 * 欄位的焦點目標（關上之後才找）：一般的編輯器就是 id 那個元素；多選的 id 在 fieldset 上，
 * fieldset 拿不到焦點，交給它裡面的第一個勾選框（報讀軟體進到這一組時會唸 legend 與錯誤）。
 */
export function fieldFocusTarget(id: string): RefObject<HTMLElement | null> {
  return {
    get current() {
      const element = document.getElementById(id);
      return element instanceof HTMLFieldSetElement ? element.querySelector<HTMLElement>('input') : element;
    },
  };
}

/**
 * 改狀態失敗時能不能開補填的對話框：是「要先填欄位」、我能改這個項目、每個欄位都有編輯器。
 * 可以時回缺的欄位（後端回的 path），不行時回 null。有沒有別的對話框開著由呼叫端看（isModalDialogOpen）。
 */
export function useFillableFields(): (error: unknown, item: Item) => readonly string[] | null {
  const { detail, can } = useProjectTable();
  return (error, item) => {
    const paths = missingFieldPaths(error);
    if (!can.update || paths === null || paths.length === 0) {
      return null;
    }
    return fillFieldsFor(paths, item, detail) === null ? null : paths;
  };
}
