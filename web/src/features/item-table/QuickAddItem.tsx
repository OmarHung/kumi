import { errorMessage } from '../../api/http';
import { useParentCandidates } from '../../api/queries';
import { Button } from '../../components/Button/Button';
import { SelectField, TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Icon } from '../../components/Icon/Icon';
import type { SelectOption } from '../../components/Select/Select';
import type { ItemFilters } from '../../domain/itemFilters';
import { MAX_TITLE_LENGTH } from './cells/drafts';
import { CreatedStatus } from './CreatedStatus';
import { ItemTypeField } from './ItemTypeField';
import { useProjectTable } from './ProjectTableContext';
import { toolbarFieldProps, type ToolbarFieldProps } from './toolbarField';
import { useQuickAdd, type QuickAdd, type QuickAddState } from './useQuickAdd';
import styles from './QuickAddItem.module.css';

interface ParentFieldProps extends ToolbarFieldProps {
  readonly types: readonly string[];
  readonly value: string;
  readonly error: string | undefined;
  readonly onChange: (parentKey: string) => void;
}

/** 上層項目：沒有上層項目加上讀到的候選（讀到之前清單裡只有「沒有上層項目」，提示說正在載入）。選了只是填好，和標題一起送出。 */
function ParentField({ types, value, error, onChange, isToolbar }: ParentFieldProps) {
  const { detail } = useProjectTable();
  const candidates = useParentCandidates(detail.project.key, types, true);
  const loadProblem = candidates.isError ? `讀不到可以選的項目：${errorMessage(candidates.error)}` : undefined;
  const hint = candidates.isPending ? '正在載入可以選的項目…' : loadProblem;
  const options: SelectOption[] = [
    { value: '', label: '沒有上層項目' },
    ...(candidates.data ?? []).map((candidate) => ({ value: candidate.key, label: `${candidate.key} ${candidate.title}` })),
  ];
  return <SelectField {...toolbarFieldProps(isToolbar)} label="上層項目" value={value} hint={hint} error={error} options={options} onChange={onChange} />;
}

interface QuickAddFieldsProps extends ToolbarFieldProps {
  readonly quickAdd: QuickAdd;
  readonly placeholder: string;
}

/**
 * 快速新增的欄位：類型（多於一種才要選）、上層項目（類型有限制才出現）、標題。表格上方的那一列（isToolbar：
 * 矮一號、標籤視覺上隱藏）與窄螢幕的對話框（標籤看得到）共用。
 */
export function QuickAddFields({ quickAdd, placeholder, isToolbar }: QuickAddFieldsProps) {
  const { itemTypes, parentTypes, hasTypes, hasParent, draft, setDraft, titleRef, create } = quickAdd;
  return (
    <>
      {hasTypes && (
        <ItemTypeField isToolbar={isToolbar} types={itemTypes} value={draft.type} error={create.fieldErrors.type} onChange={(type) => { setDraft({ ...draft, type, parentKey: '' }); }} />
      )}
      {hasParent && (
        <ParentField isToolbar={isToolbar} types={parentTypes} value={draft.parentKey} error={create.fieldErrors.parent} onChange={(parentKey) => { setDraft({ ...draft, parentKey }); }} />
      )}
      <TextField
        {...toolbarFieldProps(isToolbar)}
        ref={titleRef}
        className={styles.title}
        label="標題"
        value={draft.title}
        maxLength={MAX_TITLE_LENGTH}
        autoComplete="off"
        placeholder={placeholder}
        error={create.fieldErrors.title}
        isErrorInside={isToolbar}
        onChange={(event) => {
          setDraft({ ...draft, title: event.target.value });
          create.clearFieldError('title');
        }}
      />
    </>
  );
}

// 「已新增 AGY-25。」加「開啟」搬到 CreatedStatus.tsx；下層項目的快速新增照舊從這裡匯入。
export { CreatedStatus };

interface QuickAddItemProps {
  /** 目前表格的條件：新增的項目放進這份清單的最上面，類型與負責人沿用它。 */
  readonly filters: ItemFilters;
  /** 打到一半的內容（不隨寬窄換掉，見 useQuickAddState）。 */
  readonly state: QuickAddState;
}

/**
 * 表格上方的一列快速新增（docs/DESIGN.md 8.3）：不是卡片，高度和表格的一列一樣。
 * 欄位的標籤視覺上隱藏（名稱還在），標題框的提示文字說明怎麼用；標題的錯誤畫在標題框裡（這一列不變高），
 * 對不到欄位的錯誤出現在這一列上方。
 */
export function QuickAddItem({ filters, state }: QuickAddItemProps) {
  const quickAdd = useQuickAdd(filters, state);
  const { create } = quickAdd;
  return (
    <form className={styles.form} aria-label="新增項目" noValidate onSubmit={quickAdd.handleSubmit}>
      <FormAlert message={create.formError} />
      <div className={styles.row}>
        <Icon name="add" className={styles.icon} />
        <QuickAddFields isToolbar quickAdd={quickAdd} placeholder="新增項目：輸入標題後按 Enter" />
        <Button type="submit" variant="primary" size="sm" disabled={create.isPending}>
          {create.isPending ? '新增中…' : '新增'}
        </Button>
        <CreatedStatus created={create.created} className={styles.status} />
      </div>
    </form>
  );
}
