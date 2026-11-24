import { useEffect, useId, useRef, useState, type KeyboardEvent, type RefObject, type SubmitEvent } from 'react';
import type { ItemTypeDefinition } from '../../api/blueprint';
import type { FieldValue } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Button } from '../../components/Button/Button';
import { TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { IconButton } from '../../components/IconButton/IconButton';
import { matchesBoardFilters } from '../../domain/projectView';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { MAX_TITLE_LENGTH } from '../item-table/cells/drafts';
import { assigneeFromFilter, fieldDefaultsFromFilters, itemDefaultsFromFilters } from '../item-table/createDefaults';
import { usePlanning } from '../item-table/usePlanning';
import { CreatedStatus } from '../item-table/CreatedStatus';
import { ItemTypeField } from '../item-table/ItemTypeField';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { useCreateItem, type CreateItemDraft, type CreateItemField } from '../item-table/useCreateItem';
import { useBoard } from './BoardContext';
import styles from './Board.module.css';
import { useWeekStart } from '../../lib/weekStart';

/** 第一欄的快速新增開著沒有。按鈕在欄的標題列、表單在標題下面，兩邊看同一份。 */
export interface BoardQuickAddState {
  readonly isOpen: boolean;
  /** 表單的 id：按鈕用 aria-controls 指過去。 */
  readonly formId: string;
  /** 標題列按鈕的 id：表單收起來時焦點回到這裡。 */
  readonly toggleId: string;
  readonly toggle: () => void;
  /** 收起來，焦點回到標題列的按鈕。 */
  readonly close: () => void;
}

export function useBoardQuickAdd(): BoardQuickAddState {
  const [isOpen, setIsOpen] = useState(false);
  const formId = useId();
  const toggleId = useId();

  // 表單跟著不見（焦點可能就在裡面）：先畫完再把焦點放回按鈕，不會掉到頁面本體（lib/updateThenFocus.ts）。
  function close() {
    updateThenFocus(() => {
      setIsOpen(false);
    }, [toggleId]);
  }

  return {
    isOpen,
    formId,
    toggleId,
    toggle: () => {
      if (isOpen) {
        close();
      } else {
        setIsOpen(true);
      }
    },
    close,
  };
}

/** 標題列上的「新增項目」：圖示按鈕，aria-expanded 說表單開著沒有；開著時再按一次收起來。 */
export function QuickAddToggle({ quickAdd }: { readonly quickAdd: BoardQuickAddState }) {
  return (
    <IconButton
      id={quickAdd.toggleId}
      icon="add"
      label="新增項目"
      size="sm"
      className={styles.addToggle}
      aria-expanded={quickAdd.isOpen}
      aria-controls={quickAdd.isOpen ? quickAdd.formId : undefined}
      onClick={quickAdd.toggle}
    />
  );
}

function FormActions({ isPending, onClose }: { readonly isPending: boolean; readonly onClose: () => void }) {
  return (
    <div className={styles.addActions}>
      <Button type="submit" size="sm" variant="primary" disabled={isPending}>
        {isPending ? '新增中…' : '新增'}
      </Button>
      <Button size="sm" variant="ghost" onClick={onClose}>
        取消
      </Button>
    </div>
  );
}

/** 依負責人、欄位分欄時，這一欄的值：新增的項目預設帶著，不會一新增就跑到別欄去。 */
export interface ColumnSeed {
  readonly assigneeId?: string;
  readonly fields?: Readonly<Record<string, FieldValue>>;
}

interface QuickAddFormProps {
  /** 第一欄的查詢鍵：新的項目放在這一欄的最上面。 */
  readonly listKey: readonly unknown[];
  readonly quickAdd: BoardQuickAddState;
  /** 分組時這一欄的值。 */
  readonly seed?: ColumnSeed;
  /** 能選的類型；沒給是這個流程的類型。依欄位分欄時只有帶這個欄位的類型。 */
  readonly types?: readonly ItemTypeDefinition[];
}

/** 負責人、優先度、「今天到期」的到期日、週期與里程碑帶看板的條件；不符合搜尋或負責人時，「已新增」的訊息多說一句（docs/DESIGN.md 8.1 第 7 點）。 */
function useBoardCreate(
  listKey: readonly unknown[],
  fields: readonly CreateItemField[],
  titleRef: RefObject<HTMLInputElement | null>,
  seed: ColumnSeed | undefined,
) {
  const { filters } = useBoard();
  const { detail, todayDate } = useProjectTable();
  const me = useCurrentUser().personId;
  const { cycles, milestones } = usePlanning(detail.project.key);
  const weekStart = useWeekStart();
  return useCreateItem({
    listKey,
    position: 'first',
    fields,
    titleRef,
    assigneeId: seed?.assigneeId ?? assigneeFromFilter(detail, filters.assignee, me),
    // 那一欄的值蓋過篩選的值：在「ACME」那一欄新增，就是 ACME 的事。
    defaults: {
      ...itemDefaultsFromFilters(filters, todayDate, cycles, milestones),
      fieldsFor: (type) => ({ ...fieldDefaultsFromFilters(detail.blueprint, filters.fields, type, me, detail.members), ...seed?.fields }),
    },
    matches: (item) => matchesBoardFilters(item, filters, me, todayDate, weekStart, cycles),
  });
}

/**
 * 第一欄的快速新增（新項目一律從流程的第一個狀態開始）。打開時焦點到標題，可以連續新增；
 * Esc 或「取消」收起來，焦點回到標題列的按鈕。類型只能選用這個流程的（多於一種才要選）。
 */
export function QuickAddForm({ listKey, quickAdd, seed, types: typesOfColumn }: QuickAddFormProps) {
  const board = useBoard();
  const types = typesOfColumn ?? board.types;
  const titleRef = useRef<HTMLInputElement>(null);
  const [draft, setDraft] = useState<CreateItemDraft>({ type: types[0]?.key ?? '', parentKey: '', title: '' });
  const hasTypes = types.length > 1;
  const fields: CreateItemField[] = hasTypes ? ['type', 'title'] : ['title'];
  const create = useBoardCreate(listKey, fields, titleRef, seed);

  useEffect(() => {
    titleRef.current?.focus();
  }, []);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    create.submit(draft, () => {
      setDraft((current) => ({ ...current, title: '' }));
    });
  }

  function handleKeyDown(event: KeyboardEvent<HTMLFormElement>) {
    if (event.key === 'Escape') {
      event.stopPropagation();
      quickAdd.close();
    }
  }

  return (
    <form id={quickAdd.formId} className={styles.addForm} aria-label="新增項目" noValidate onSubmit={handleSubmit} onKeyDown={handleKeyDown}>
      <FormAlert message={create.formError} />
      {hasTypes && (
        <ItemTypeField types={types} value={draft.type} error={create.fieldErrors.type} onChange={(type) => { setDraft({ ...draft, type }); }} />
      )}
      <TextField
        ref={titleRef}
        label="標題"
        value={draft.title}
        maxLength={MAX_TITLE_LENGTH}
        autoComplete="off"
        placeholder="輸入標題後按 Enter"
        error={create.fieldErrors.title}
        onChange={(event) => { setDraft({ ...draft, title: event.target.value }); }}
      />
      <FormActions isPending={create.isPending} onClose={quickAdd.close} />
      <CreatedStatus created={create.created} className={styles.addStatus} />
    </form>
  );
}
