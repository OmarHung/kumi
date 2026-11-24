import { useRef, useState, type SubmitEvent } from 'react';
import type { ItemTypeDefinition } from '../../api/blueprint';
import { Button } from '../../components/Button/Button';
import { TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Icon } from '../../components/Icon/Icon';
import { MAX_TITLE_LENGTH } from '../item-table/cells/drafts';
import { ItemTypeField } from '../item-table/ItemTypeField';
import { CreatedStatus } from '../item-table/QuickAddItem';
import { useCreateItem, type CreateItemDraft, type CreateItemField } from '../item-table/useCreateItem';
import { cx } from '../../lib/cx';
import quickAddStyles from '../item-table/QuickAddItem.module.css';
import styles from './ChildItems.module.css';

interface ChildQuickAddProps {
  readonly parentKey: string;
  /** 可以放在這個項目底下的類型。 */
  readonly childTypes: readonly ItemTypeDefinition[];
  /** 下層項目清單的查詢鍵：新增的項目接在它最後面。 */
  readonly listKey: readonly unknown[];
}

/**
 * 新增下層項目：清單下面一列，不是卡片。長相直接用表格上方快速新增的 class（item-table/QuickAddItem.module.css，docs/DESIGN.md 8.3），
 * 只多了可以折行（那一列只在寬螢幕出現，這裡手機上也有）。
 * 類型（多於一種才要選）加標題，上層就是這個項目；欄位的標籤視覺上隱藏（名稱還在），標題框的提示文字說明怎麼用。
 */
export function ChildQuickAdd({ parentKey, childTypes, listKey }: ChildQuickAddProps) {
  const titleRef = useRef<HTMLInputElement>(null);
  const [draft, setDraft] = useState<CreateItemDraft>({ type: childTypes[0]?.key ?? '', parentKey, title: '' });
  const hasTypes = childTypes.length > 1;
  const fields: CreateItemField[] = hasTypes ? ['type', 'title'] : ['title'];
  const create = useCreateItem({ listKey, position: 'last', fields, titleRef });

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    create.submit(draft, () => {
      setDraft((current) => ({ ...current, title: '' }));
    });
  }

  return (
    <form className={quickAddStyles.form} aria-label="新增下層項目" noValidate onSubmit={handleSubmit}>
      <FormAlert message={create.formError} />
      <div className={cx(quickAddStyles.row, styles.quickAddRow)}>
        <Icon name="add" className={quickAddStyles.icon} />
        {hasTypes && (
          <ItemTypeField isToolbar types={childTypes} value={draft.type} error={create.fieldErrors.type} onChange={(type) => { setDraft({ ...draft, type }); }} />
        )}
        <TextField
          ref={titleRef}
          size="sm"
          isLabelHidden
          className={quickAddStyles.title}
          label="標題"
          value={draft.title}
          maxLength={MAX_TITLE_LENGTH}
          autoComplete="off"
          placeholder="輸入標題後按 Enter 新增"
          error={create.fieldErrors.title}
          isErrorInside
          onChange={(event) => {
            setDraft({ ...draft, title: event.target.value });
            create.clearFieldError('title');
          }}
        />
        <Button type="submit" variant="primary" size="sm" disabled={create.isPending}>
          {create.isPending ? '新增中…' : '新增'}
        </Button>
        <CreatedStatus createdKey={create.createdKey} className={quickAddStyles.status} />
      </div>
    </form>
  );
}
