import { DndContext, DragOverlay } from '@dnd-kit/core';
import { useState, type ReactNode } from 'react';
import { IconButton } from '../../../components/IconButton/IconButton';
import { NewPageInput } from './NewPageInput';
import { PageTreeRow, type RowActions } from './PageTreeRow';
import type { MovePlan } from './treeDrop';
import { visibleRows, type TreeModel, type TreeRow } from './treeModel';
import { useTreeKeyboard } from './useTreeKeyboard';
import { TREE_INSTRUCTIONS, usePageTreeDnd } from './usePageTreeDnd';
import { useTreeExpansion } from './useTreeExpansion';
import styles from './PageTree.module.css';

/** 正在新增的頁面：放在 parentNumber 底下（null 是最上層）。 */
export interface NewPageState {
  readonly parentNumber: number | null;
  readonly isPending: boolean;
  readonly error: string | null;
}

export interface PageTreeProps extends RowActions {
  readonly spaceKey: string;
  readonly model: TreeModel;
  readonly currentNumber: number | null;
  readonly canEdit: boolean;
  readonly newPage: NewPageState | null;
  /** 最上面「新增頁面」的按鈕：新增在最上層。 */
  readonly onStartNewPage: (parentNumber: number | null) => void;
  readonly onSubmitNewPage: (title: string) => void;
  readonly onCancelNewPage: () => void;
  /** 拖曳放下：已經檢查過、真的會換位置的搬移。 */
  readonly onDropMove: (pageNumber: number, plan: MovePlan) => void;
  readonly isMoving: boolean;
  /** 選了一頁之後（窄螢幕用來收起頁面面板）。 */
  readonly onNavigate?: () => void;
}

/** 新頁面的輸入框放在上層的最後一個（看得到的）子孫後面；最上層就是整棵樹的最後。 */
function insertIndexOf(rows: readonly TreeRow[], parentNumber: number | null): number {
  if (parentNumber === null) return rows.length;
  const parentIndex = rows.findIndex((row) => row.node.number === parentNumber);
  if (parentIndex === -1) return rows.length;
  const parentLevel = rows[parentIndex]?.level ?? 0;
  const next = rows.findIndex((row, index) => index > parentIndex && row.level <= parentLevel);
  return next === -1 ? rows.length : next;
}

function levelOfNewPage(rows: readonly TreeRow[], parentNumber: number | null): number {
  return parentNumber === null ? 1 : (rows.find((row) => row.node.number === parentNumber)?.level ?? 0) + 1;
}

interface TreeItemsContext extends PageTreeProps {
  readonly rows: readonly TreeRow[];
  readonly dnd: ReturnType<typeof usePageTreeDnd>;
  readonly keyboard: ReturnType<typeof useTreeKeyboard>;
  readonly expansion: ReturnType<typeof useTreeExpansion>;
}

/** 樹上的列，新增頁面的輸入框插在上層的子孫後面。 */
function treeItems(context: TreeItemsContext): ReactNode[] {
  const { rows, dnd, keyboard, expansion, newPage, spaceKey, currentNumber, canEdit, onNavigate } = context;
  const input: ReactNode = newPage !== null && (
    <NewPageInput
      key="new-page"
      level={levelOfNewPage(rows, newPage.parentNumber)}
      isPending={newPage.isPending}
      error={newPage.error}
      onSubmit={context.onSubmitNewPage}
      onCancel={context.onCancelNewPage}
    />
  );
  const insertIndex = newPage === null ? -1 : insertIndexOf(rows, newPage.parentNumber);
  const items: ReactNode[] = rows.flatMap((row, index) => [
    ...(index === insertIndex ? [input] : []),
    <PageTreeRow
      key={row.node.id}
      row={row}
      spaceKey={spaceKey}
      isCurrent={row.node.number === currentNumber}
      isActive={row.node.number === keyboard.stopNumber}
      canEdit={canEdit}
      isDragDisabled={context.isMoving}
      drop={dnd.hover?.plan != null && dnd.hover.targetNumber === row.node.number ? dnd.hover.zone : null}
      actions={context}
      registerRow={keyboard.registerRow}
      onFocusRow={keyboard.onFocusRow}
      onToggle={expansion.toggle}
      onOpen={() => onNavigate?.()}
    />,
  ]);
  if (insertIndex === rows.length) items.push(input);
  return items;
}

function TreeHeader({ canEdit, onNewPage }: { readonly canEdit: boolean; readonly onNewPage: () => void }) {
  return (
    <header className={styles.header}>
      <h2 className={styles.heading}>頁面</h2>
      {canEdit && <IconButton size="sm" icon="add" label="新增最上層的頁面" onClick={onNewPage} />}
    </header>
  );
}

/**
 * 空間的頁面樹（docs/design/07-components.md「頁面樹」）：ARIA 的 tree，鍵盤見 useTreeKeyboard。展開的記憶、拖曳與列上的動作都在這裡。
 * 搬移、刪除的對話框不在這裡：頁面面板在窄螢幕會收起來，對話框跟著卸下就會不見，所以由外面（SpaceShell）管。
 */
export function PageTree(props: PageTreeProps) {
  const { spaceKey, model, currentNumber, canEdit, newPage, onNavigate } = props;
  // 一開始就掛著、之後才填字：報讀軟體才唸得到（拖曳搬移的結果）。
  const [announcement, setAnnouncement] = useState('');
  const expansion = useTreeExpansion(spaceKey, model, currentNumber);
  // 在某一頁底下新增時，那一頁一定要展開，輸入框才看得到。
  const expanded = newPage?.parentNumber == null ? expansion.expanded : new Set([...expansion.expanded, newPage.parentNumber]);
  const rows = visibleRows(model, expanded);
  const dnd = usePageTreeDnd(model, expanded, props.onDropMove, setAnnouncement);
  const keyboard = useTreeKeyboard({ spaceKey, model, rows, currentNumber, expansion, onNavigate });

  const items = treeItems({ ...props, rows, dnd, keyboard, expansion });
  const draggedTitle = dnd.activeNumber === null ? null : (model.nodeOf(dnd.activeNumber)?.title ?? null);

  return (
    <section className={styles.tree} aria-label="頁面">
      <p role="status" className="visually-hidden">
        {announcement}
      </p>
      <TreeHeader canEdit={canEdit} onNewPage={() => { props.onStartNewPage(null); }} />
      {rows.length === 0 && newPage === null && <p className={styles.empty}>還沒有頁面。</p>}
      <DndContext
        sensors={dnd.sensors}
        collisionDetection={dnd.collisionDetection}
        accessibility={{ announcements: dnd.announcements, screenReaderInstructions: TREE_INSTRUCTIONS }}
        {...dnd.handlers}
      >
        <ul role="tree" aria-label="頁面樹" aria-busy={props.isMoving || undefined} className={styles.list} onKeyDown={keyboard.handleKeyDown}>
          {items}
        </ul>
        <DragOverlay dropAnimation={null}>{draggedTitle !== null && <div className={styles.overlay}>{draggedTitle}</div>}</DragOverlay>
      </DndContext>
    </section>
  );
}
