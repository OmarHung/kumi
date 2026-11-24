import type { FocusEvent, KeyboardEvent } from 'react';
import { Button } from '../../../components/Button/Button';
import { CheckboxField } from '../../../components/CheckboxField/CheckboxField';
import { fieldLabelClassName } from '../../../components/Field/Field';
import { cx } from '../../../lib/cx';
import { isCommitEnter } from '../../../lib/keyboard';
import type { EditorApi } from './EditableCell';
import { isInDialog, type SelectOption } from './editors';
import styles from './Cells.module.css';

/*
 * 勾選類的編輯器：多選（一組勾選框）與勾選。送出中都不收新的值（不停用：焦點可能就在上面，停用會掉到頁面本體）。
 */

interface MultiSelectEditorProps {
  readonly api: EditorApi<readonly string[]>;
  readonly options: readonly SelectOption[];
}

function toggled(current: readonly string[], value: string, options: readonly SelectOption[]) {
  const next = current.includes(value) ? current.filter((key) => key !== value) : [...current, value];
  // 照選項定義的順序存，同樣的選擇不會因為勾的順序不同而被當成有改。
  return options.map((option) => option.value).filter((key) => next.includes(key));
}

interface ChoiceProps {
  readonly api: EditorApi<readonly string[]>;
  readonly option: SelectOption;
  readonly options: readonly SelectOption[];
}

/** 一個選項。格子裡是精簡的一列；對話框裡和 CheckboxField 一樣（勾選框加文字、整個標籤可以點、至少 --target-min 高）。 */
function Choice({ api, option, options }: ChoiceProps) {
  const isChecked = api.draft.includes(option.value);
  const toggle = () => {
    if (!api.isSaving) api.setDraft(toggled(api.draft, option.value, options));
  };
  if (isInDialog(api)) {
    return <CheckboxField label={option.label} checked={isChecked} aria-readonly={api.isSaving || undefined} onChange={toggle} />;
  }
  return (
    <label className={styles.choice}>
      <input type="checkbox" checked={isChecked} aria-readonly={api.isSaving || undefined} onChange={toggle} />
      {option.label}
    </label>
  );
}

/** 多選在格子裡的「完成」：儲存並結束編輯。 */
function DoneButton({ api }: { readonly api: EditorApi<readonly string[]> }) {
  return (
    <Button
      size="sm"
      disabled={api.isSaving}
      onClick={() => {
        api.commit('enter');
      }}
    >
      完成
    </Button>
  );
}

/**
 * 多選：一組勾選框（fieldset 加 legend）。Enter、按「完成」或焦點離開這一組時儲存，Esc 取消。
 * 對話框裡 legend 就是看得到的標籤（不另外畫 label），沒有「完成」（和其他欄位一起送），也不自己搶焦點。
 */
export function MultiSelectEditor({ api, options }: MultiSelectEditorProps) {
  const isInCell = !isInDialog(api);
  function handleBlur(event: FocusEvent<HTMLFieldSetElement>) {
    if (!event.currentTarget.contains(event.relatedTarget)) {
      api.commit('blur');
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLFieldSetElement>) {
    if (event.key === 'Escape') {
      event.preventDefault();
      api.cancel();
    } else if (isCommitEnter(event)) {
      event.preventDefault();
      api.commit('enter');
    }
  }

  return (
    <fieldset
      id={api.id}
      className={styles.choices}
      data-placement={isInCell ? undefined : 'dialog'}
      aria-describedby={api.describedBy}
      onBlur={handleBlur}
      onKeyDown={handleKeyDown}
    >
      <legend className={isInCell ? 'visually-hidden' : cx(fieldLabelClassName(), styles.legend)}>{api.label}</legend>
      {options.map((option) => (
        <Choice key={option.value} api={api} option={option} options={options} />
      ))}
      {isInCell && <DoneButton api={api} />}
    </fieldset>
  );
}

/**
 * 勾選：只用在補填對話框（表格裡的勾選欄位不進編輯，勾了就存，見 FieldCells 的 CheckboxFieldCell）。
 * 和 CheckboxField 一樣是勾選框加文字（名稱就是旁邊的字），Enter 和其他欄位一樣送出；輸入法選字的 Enter 不算。
 */
export function CheckboxEditor({ api }: { readonly api: EditorApi<boolean> }) {
  return (
    <CheckboxField
      id={api.id}
      label={api.label}
      checked={api.draft}
      aria-invalid={api.isInvalid || undefined}
      aria-describedby={api.describedBy}
      aria-readonly={api.isSaving || undefined}
      onChange={(event) => {
        if (!api.isSaving) api.setDraft(event.target.checked);
      }}
      onKeyDown={(event) => {
        if (isCommitEnter(event)) {
          event.preventDefault();
          api.commit('enter');
        }
      }}
    />
  );
}
