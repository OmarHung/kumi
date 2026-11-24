import { useState } from 'react';
import type { FieldDefinition, OptionColor } from '../../../api/blueprint';
import type { FieldValue, Item, Member } from '../../../api/types';
import { Badge, type BadgeTone } from '../../../components/Badge/Badge';
import { Icon } from '../../../components/Icon/Icon';
import { formatFieldValue, isOptionList, NOT_A_MEMBER, optionKeys, safeHttpUrl } from '../../../domain/fields';
import { EmptyValue } from '../../work-items/ItemCells';
import { useProjectTable } from '../ProjectTableContext';
import { memberOptions } from './CoreCells';
import {
  fieldCommand,
  fieldDraft,
  fieldValueCommand,
  MAX_LONG_TEXT_LENGTH,
  MAX_TEXT_LENGTH,
} from './drafts';
import { cellErrorMessage, EditableCell, type EditorApi, type FieldEditSpec } from './EditableCell';
import { CheckboxEditor, MultiSelectEditor } from './choiceEditors';
import { DateEditor } from './DateEditor';
import { InputEditor, SelectEditor, TextAreaEditor, type SelectOption } from './editors';
import { useItemCommand } from './useItemCommand';
import styles from './Cells.module.css';

interface FieldCellProps {
  readonly item: Item;
  readonly definition: FieldDefinition;
}

// 選項的顏色只是輔助，意思在文字裡。藍與紫有自己的色調（強調色是紫藍，借它的話藍會變成紫、兩者分不出來）；
// 其他對到既有的幾種標籤色調。
const OPTION_TONES: Readonly<Record<OptionColor, BadgeTone>> = {
  gray: 'neutral',
  red: 'danger',
  orange: 'warning',
  yellow: 'warning',
  green: 'success',
  blue: 'blue',
  purple: 'purple',
};

function optionsOf(definition: FieldDefinition): SelectOption[] {
  return (definition.options ?? []).map((option) => ({ value: option.key, label: option.name }));
}

function OptionBadges({ definition, value }: { readonly definition: FieldDefinition; readonly value: FieldValue }) {
  return (
    <span className={styles.badges}>
      {optionKeys(value).map((key) => {
        const option = definition.options?.find((candidate) => candidate.key === key);
        return (
          <Badge key={key} tone={option?.color ? OPTION_TONES[option.color] : 'neutral'}>
            {option?.name ?? key}
          </Badge>
        );
      })}
    </span>
  );
}

function FieldDisplay({ definition, value, text }: FieldCellProps & { readonly value: FieldValue | undefined; readonly text: string }) {
  if (value === undefined) {
    return <EmptyValue label="未填" />;
  }
  if (definition.type === 'single_select' || definition.type === 'multi_select') {
    return <OptionBadges definition={definition} value={value} />;
  }
  return <span className={styles.clamp}>{text}</span>;
}

function renderStringEditor(definition: FieldDefinition, api: EditorApi<string>, options: SelectOption[]) {
  switch (definition.type) {
    case 'long_text':
      return <TextAreaEditor api={api} maxLength={MAX_LONG_TEXT_LENGTH} />;
    case 'url':
      return <InputEditor api={api} type="url" maxLength={MAX_TEXT_LENGTH} />;
    case 'date':
      return <DateEditor api={api} />;
    case 'number':
    case 'currency':
      return <InputEditor api={api} inputMode="decimal" />;
    case 'single_select':
    case 'person':
      return <SelectEditor api={api} options={options} />;
    default:
      return <InputEditor api={api} maxLength={MAX_TEXT_LENGTH} />;
  }
}

/*
 * 自訂欄位的編輯方式（*FieldSpec）：格子與補填對話框（fillFields.tsx）共用。
 */

export function multiSelectFieldSpec(item: Item, definition: FieldDefinition): FieldEditSpec<readonly string[]> {
  const value = item.fields[definition.key];
  const current = isOptionList(value) ? value : [];
  return {
    label: definition.name,
    errorPaths: [definition.key],
    initialDraft: () => current,
    toCommand: (draft) => fieldValueCommand(definition.key, value, draft.length > 0 ? draft : null),
    renderEditor: (api) => <MultiSelectEditor api={api} options={optionsOf(definition)} />,
    hasOwnLabel: true,
  };
}

function MultiSelectFieldCell({ item, definition }: FieldCellProps) {
  const { detail, can } = useProjectTable();
  const value = item.fields[definition.key];
  return (
    <EditableCell
      item={item}
      text={formatFieldValue(definition, value, detail.members)}
      canEdit={can.update}
      {...multiSelectFieldSpec(item, definition)}
    >
      <FieldDisplay item={item} definition={definition} value={value} text="" />
    </EditableCell>
  );
}

/** 勾選欄位在補填對話框裡（表格裡不進編輯，見下面的 CheckboxFieldCell）：沒勾等於沒有值，送 null。 */
export function checkboxFieldSpec(item: Item, definition: FieldDefinition): FieldEditSpec<boolean> {
  const value = item.fields[definition.key];
  return {
    label: definition.name,
    errorPaths: [definition.key],
    initialDraft: () => value === true,
    toCommand: (draft) => fieldValueCommand(definition.key, value, draft ? true : null),
    renderEditor: (api) => <CheckboxEditor api={api} />,
    hasOwnLabel: true,
  };
}

/** 勾選不必進入編輯：勾或取消就儲存。沒勾等於沒有值，送 null 清掉。 */
function CheckboxFieldCell({ item, definition }: FieldCellProps) {
  const { can } = useProjectTable();
  const command = useItemCommand(item.key);
  const [hasTried, setHasTried] = useState(false);
  const isChecked = item.fields[definition.key] === true;
  return (
    <div className={styles.static}>
      {/* 包住勾選框的標籤是點擊範圍（至少 --target-min）；名稱照舊用 aria-label。 */}
      <label className={styles.checkTarget}>
        <input
          type="checkbox"
          aria-label={definition.name}
          checked={isChecked}
          disabled={!can.update}
          aria-busy={command.isPending}
          onChange={() => {
            if (command.isPending) return;
            setHasTried(true);
            command.mutate({ kind: 'patch', patch: { fields: { [definition.key]: isChecked ? null : true } } });
          }}
        />
      </label>
      {hasTried && command.isError && (
        <p role="alert" className={styles.problem}>
          <Icon name="error" className={styles.problemIcon} />
          <span className={styles.problemBody}>{cellErrorMessage(command.error, [definition.key])}</span>
        </p>
      )}
    </div>
  );
}

function UrlLink({ value, label }: { readonly value: FieldValue | undefined; readonly label: string }) {
  const href = typeof value === 'string' ? safeHttpUrl(value) : undefined;
  if (href === undefined) {
    return null;
  }
  return (
    <a className={styles.open} href={href} target="_blank" rel="noopener noreferrer" aria-label={`開啟${label}`}>
      開啟
    </a>
  );
}

/**
 * 下拉選單的選項。成員欄位的值是被移出專案的人時，選項最後接上他（名字查不到，標「不是成員的人」，和負責人一樣），
 * 選單才選得到目前的值、不會看起來像「沒有人」；選回他就是沒改，不會送出。
 */
function selectOptions(definition: FieldDefinition, value: FieldValue | undefined, members: readonly Member[]): SelectOption[] {
  if (definition.type !== 'person') {
    return [{ value: '', label: '未選' }, ...optionsOf(definition)];
  }
  const current = value === undefined ? null : { personId: String(value), displayName: NOT_A_MEMBER };
  return memberOptions(members, '沒有人', current);
}

/** 值是一個字串的自訂欄位（勾選、多選以外的型別）：依型別選編輯器。 */
export function textFieldSpec(item: Item, definition: FieldDefinition, members: readonly Member[]): FieldEditSpec<string> {
  const value = item.fields[definition.key];
  const options = selectOptions(definition, value, members);
  return {
    label: definition.name,
    errorPaths: [definition.key],
    initialDraft: () => fieldDraft(definition, value),
    toCommand: (draft) => fieldCommand(definition, value, draft),
    renderEditor: (api) => renderStringEditor(definition, api, options),
  };
}

/** 藍圖定義的自訂欄位。依型別選編輯器；值的形狀見 api/types 的 FieldValue。 */
export function FieldCell({ item, definition }: FieldCellProps) {
  const { detail, can } = useProjectTable();
  if (definition.type === 'checkbox') {
    return <CheckboxFieldCell item={item} definition={definition} />;
  }
  if (definition.type === 'multi_select') {
    return <MultiSelectFieldCell item={item} definition={definition} />;
  }
  const value = item.fields[definition.key];
  const text = formatFieldValue(definition, value, detail.members);
  const cell = (
    <EditableCell item={item} text={text} canEdit={can.update} {...textFieldSpec(item, definition, detail.members)}>
      <FieldDisplay item={item} definition={definition} value={value} text={text} />
    </EditableCell>
  );
  if (definition.type !== 'url') {
    return cell;
  }
  return (
    <div className={styles.withAction}>
      {cell}
      <UrlLink value={value} label={definition.name} />
    </div>
  );
}
