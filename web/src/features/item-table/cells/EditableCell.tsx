import { useEffect, useId, useRef, useState, type ReactNode, type RefObject } from 'react';
import { useReportHandBusy } from '../../live/handActivity';
import { errorMessage, isApiError } from '../../../api/http';
import type { Item } from '../../../api/types';
import { EditorFrame } from './EditorFrame';
import { useItemCommand, type CellCommand, type SaveCommand } from './useItemCommand';
import styles from './Cells.module.css';

export type CommitReason = 'enter' | 'blur';

/** 編輯器拿到的操作。值的形狀由每種欄位自己決定（字串或字串陣列）。 */
export interface EditorApi<TDraft> {
  readonly label: string;
  readonly draft: TDraft;
  readonly setDraft: (draft: TDraft) => void;
  /** 傳入 value 時直接用它儲存，不必等畫面更新（例如下拉選了選項，草稿還沒換成新的值）。 */
  readonly commit: (reason: CommitReason, value?: TDraft) => void;
  readonly cancel: () => void;
  readonly isSaving: boolean;
  readonly isInvalid: boolean;
  readonly describedBy: string | undefined;
  /**
   * 編輯器放在哪裡，預設是格子。dialog（補填對話框，docs/DESIGN.md 8.3「補填再前進」）：一般表單的高度，
   * 不自己搶焦點、不自己打開下拉（對話框把焦點放在第一個欄位），下拉選了不馬上存（Enter 或按鈕才送）。
   */
  readonly placement?: 'cell' | 'dialog';
  /** 對話框裡看得到的標籤（label 的 htmlFor）連到編輯器。 */
  readonly id?: string;
  /**
   * 格子是用滑鼠（或觸控）點開的（按鈕的 click 的 detail 大於 0）；Enter、F2 進入的是 false。
   * 日期用它決定要不要直接打開日曆（docs/DESIGN.md 8.3「就地編輯」）。對話框裡沒有「進入編輯」，不給。
   */
  readonly isStartedByPointer?: boolean;
  /**
   * 草稿還是目前的值時，格子平常的樣子（EditableCell 的 children）；選了別的值（儲存中、存不進去）時沒有。
   * 下拉的按鈕裡顯示它，不顯示選項的文字（「待辦（目前）」）與箭頭：浮起來的按鈕和格子長得一樣，欄寬不變
   * （docs/DESIGN.md 8.3「就地編輯」）。對話框裡不給。
   */
  readonly display?: ReactNode;
}

/**
 * 一個欄位怎麼編輯：標籤、對到錯誤的 path、草稿、要送什麼、用哪個編輯器。
 * 表格的格子（EditableCell）與補填對話框（FillFieldsDialog）用同一份，編輯器與送出的內容不會兩邊各寫一套。
 */
export interface FieldEditSpec<TDraft> {
  /** 欄位名稱，例如「到期日」。 */
  readonly label: string;
  /** 後端錯誤 details 的 path 對到這一格的名字，例如 due_date 與 dueDate。 */
  readonly errorPaths: readonly string[];
  readonly initialDraft: () => TDraft;
  readonly toCommand: (draft: TDraft) => CellCommand;
  readonly renderEditor: (api: EditorApi<TDraft>) => ReactNode;
  /**
   * 對話框裡編輯器自己畫看得到的標籤（多選的 legend、勾選框旁邊的字）：對話框不再畫 label。
   * 格子裡用不到（格子的名稱在按鈕與編輯器的 aria-label）。
   */
  readonly hasOwnLabel?: boolean;
}

export interface EditableCellProps<TDraft> extends Omit<FieldEditSpec<TDraft>, 'errorPaths'> {
  readonly item: Item;
  /** 目前的值的文字，給輔助科技；空字串是沒有值。 */
  readonly text: string;
  readonly canEdit: boolean;
  /** 檢視時的樣子。 */
  readonly children: ReactNode;
  readonly errorPaths?: readonly string[];
  readonly renderError?: (error: unknown, attempted: SaveCommand | undefined) => ReactNode;
  /**
   * 送出失敗時先問呼叫端：回 true 表示呼叫端接手（例如改狀態缺欄位，開補填的對話框），
   * 格子就結束編輯、焦點回到這一格，不在格子裡顯示錯誤。
   */
  readonly onSaveError?: (error: unknown, attempted: SaveCommand) => boolean;
  /** 儲存成功、格子結束編輯之後，帶著伺服器回的項目呼叫（例如負責人改給別人之後，只看自己的人看不到這個項目了）。 */
  readonly onSaved?: (saved: Item) => void;
}

export const EMPTY_TEXT = '未填';

/** 後端的訊息已經是給使用者看的；有對到這一格的細項就用細項，比較具體。 */
export function cellErrorMessage(error: unknown, paths: readonly string[]): string {
  if (!isApiError(error)) {
    return errorMessage(error);
  }
  const detail = error.details.find((candidate) => candidate.path !== null && paths.includes(candidate.path));
  return detail?.message ?? error.message;
}

/** 編輯模式的開關。用鍵盤結束編輯時，焦點回到這一格，才能接著用方向鍵（表格）或 Tab（項目頁）往下走。 */
function useEditMode(triggerRef: RefObject<HTMLButtonElement | null>) {
  const [isEditing, setIsEditing] = useState(false);
  // 只在事件裡讀寫：離開格子（blur）和 Enter 可能前後腳發生，用 ref 判斷才不會處理兩次。
  const isActive = useRef(false);
  const shouldRefocus = useRef(false);

  useEffect(() => {
    if (!isEditing && shouldRefocus.current) {
      shouldRefocus.current = false;
      // preventScroll：標題是固定欄，瀏覽器自己的捲動會把橫向捲過去的表格捲回最左邊；表格自己捲（lib/revealCell.ts）。
      triggerRef.current?.focus({ preventScroll: true });
    }
  }, [isEditing, triggerRef]);

  return {
    isEditing,
    isActive: () => isActive.current,
    open: () => {
      isActive.current = true;
      setIsEditing(true);
    },
    close: (refocus: boolean) => {
      isActive.current = false;
      shouldRefocus.current = refocus;
      setIsEditing(false);
    },
  };
}

/** 送出一格的修改；送出中再按 Enter 或離開格子不會重送。 */
function useSaveRunner(itemKey: string, onError: (error: unknown, attempted: SaveCommand) => void) {
  const command = useItemCommand(itemKey);
  const isBusy = useRef(false);
  return {
    command,
    isBusy: () => isBusy.current,
    run: (next: SaveCommand, onSuccess: (saved: Item) => void) => {
      isBusy.current = true;
      command.mutate(next, {
        onSuccess,
        onError: (error) => {
          onError(error, next);
        },
        onSettled: () => {
          isBusy.current = false;
        },
      });
    },
  };
}

function isSameFailedAttempt(
  command: ReturnType<typeof useItemCommand>,
  next: SaveCommand,
): boolean {
  return command.isError && JSON.stringify(command.variables) === JSON.stringify(next);
}

function useCellEditing<TDraft>(
  { item, initialDraft, toCommand, onSaveError, onSaved }: EditableCellProps<TDraft>,
  triggerRef: RefObject<HTMLButtonElement | null>,
) {
  const mode = useEditMode(triggerRef);
  const saver = useSaveRunner(item.key, (error, attempted) => {
    if (onSaveError?.(error, attempted) === true) finish(true);
  });
  const [draft, setDraftState] = useState<TDraft>(initialDraft);
  const [localError, setLocalError] = useState<string | null>(null);
  const [isStartedByPointer, setIsStartedByPointer] = useState(false);

  function finish(refocus: boolean) {
    mode.close(refocus);
    setLocalError(null);
    saver.command.reset();
  }

  function commit(reason: CommitReason, value: TDraft = draft) {
    if (!mode.isActive() || saver.isBusy()) {
      return;
    }
    const next = toCommand(value);
    if (next.kind === 'invalid') {
      setLocalError(next.message);
    } else if (next.kind === 'unchanged') {
      finish(reason === 'enter');
    } else if (reason === 'blur' && isSameFailedAttempt(saver.command, next)) {
      // 剛失敗的同一個值，焦點移到錯誤說明裡的按鈕（或別處）時不重送；要再試就按 Enter。
    } else {
      setLocalError(null);
      saver.run(next, (saved) => { finish(reason === 'enter'); onSaved?.(saved); });
    }
  }

  function start(isPointer: boolean) {
    setIsStartedByPointer(isPointer);
    setDraftState(initialDraft());
    setLocalError(null);
    saver.command.reset();
    mode.open();
  }

  function setDraft(next: TDraft) {
    setDraftState(next);
    setLocalError(null);
    if (!saver.isBusy()) saver.command.reset();
  }

  const cancel = () => {
    if (!saver.isBusy()) finish(true);
  };
  // 輸入開著的時候，即時更新的失效先記著（features/live）：那一列被重抓或篩掉時，沒送出的輸入不會跟著不見。
  useReportHandBusy(mode.isEditing);
  return { command: saver.command, isEditing: mode.isEditing, isStartedByPointer, draft, localError, commit, start, setDraft, cancel };
}

/**
 * 可以在格子裡直接改的值。平常是一個按鈕（表格裡用方向鍵、項目頁用 Tab 走得到，Enter、F2（表格）或點一下開始編輯）；
 * 編輯時 Enter 或離開格子儲存、Esc 取消；失敗時留著使用者輸入的值，訊息顯示在格子裡。
 * 編輯中按鈕留在原位撐住格子的大小（inert、aria-hidden：點不到、報讀軟體不唸），編輯器浮在它上面（EditorFrame），
 * 進入編輯、儲存中、出錯時欄寬與列高都不變；結束時焦點回到同一顆按鈕。
 */
export function EditableCell<TDraft>(props: EditableCellProps<TDraft>) {
  const { label, text, canEdit, children, errorPaths = [], renderEditor, renderError, toCommand } = props;
  const triggerRef = useRef<HTMLButtonElement>(null);
  const editing = useCellEditing(props, triggerRef);
  const errorId = useId();

  if (!canEdit) {
    return <div className={styles.static}>{children}</div>;
  }
  const { command, isEditing } = editing;
  const failure = command.isError
    ? (renderError?.(command.error, command.variables) ?? cellErrorMessage(command.error, errorPaths))
    : null;
  const problem = editing.localError ?? failure;
  const api: EditorApi<TDraft> = {
    label,
    draft: editing.draft,
    setDraft: editing.setDraft,
    commit: editing.commit,
    cancel: editing.cancel,
    isSaving: command.isPending,
    isInvalid: problem !== null,
    describedBy: problem === null ? undefined : errorId,
    isStartedByPointer: editing.isStartedByPointer,
    display: toCommand(editing.draft).kind === 'unchanged' ? children : undefined,
  };
  return (
    <span className={styles.editable}>
      <button
        ref={triggerRef}
        type="button"
        className={styles.trigger}
        aria-label={`${label}：${text === '' ? EMPTY_TEXT : text}`}
        aria-hidden={isEditing || undefined}
        inert={isEditing}
        onClick={(event) => {
          // 鍵盤（Enter、空白鍵）與表格的 F2（element.click()）的 click 沒有點擊次數，detail 是 0。
          if (!isEditing) editing.start(event.detail > 0);
        }}
      >
        {children}
      </button>
      {isEditing && (
        <EditorFrame isSaving={command.isPending} problem={problem} problemId={errorId}>
          {renderEditor(api)}
        </EditorFrame>
      )}
    </span>
  );
}
