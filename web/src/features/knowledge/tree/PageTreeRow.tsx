import { useId } from 'react';
import { useDraggable, useDroppable } from '@dnd-kit/core';
import { Link } from 'react-router';
import { KNOWLEDGE_MAX_DEPTH } from '../../../api/knowledge';
import type { KnowledgePageNode } from '../../../api/knowledgeTypes';
import { Icon } from '../../../components/Icon/Icon';
import { IconButton } from '../../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../../components/Menu/Menu';
import { cx } from '../../../lib/cx';
import { knowledgePageUrl } from '../knowledgeLinks';
import { RESTRICTED_ICON } from '../restrictedIcon';
import type { DropZone } from './treeDrop';
import type { TreeRow } from './treeModel';
import styles from './PageTree.module.css';

export interface RowActions {
  readonly onAddChild: (node: KnowledgePageNode) => void;
  readonly onMove: (node: KnowledgePageNode) => void;
  readonly onDelete: (node: KnowledgePageNode) => void;
  /** 選單裡「搬移…」與「刪除…」之間多出來的選項（頁面的「限制…」由第二批從外面傳進來）。 */
  readonly extraItems?: (node: KnowledgePageNode) => readonly MenuItem[];
}

function menuItemsOf(row: TreeRow, actions: RowActions): readonly MenuItem[] {
  const { node } = row;
  return [
    {
      key: 'add',
      label: '新增子頁面',
      icon: 'add',
      disabledReason: row.level >= KNOWLEDGE_MAX_DEPTH ? '已經是最深的一層' : undefined,
      onSelect: () => {
        actions.onAddChild(node);
      },
    },
    {
      key: 'move',
      label: '搬移…',
      icon: 'move',
      onSelect: () => {
        actions.onMove(node);
      },
    },
    ...(actions.extraItems?.(node) ?? []),
    {
      key: 'delete',
      label: '刪除…',
      icon: 'delete',
      tone: 'danger',
      startsGroup: true,
      onSelect: () => {
        actions.onDelete(node);
      },
    },
  ];
}

interface RowButtonsProps {
  readonly row: TreeRow;
  readonly actions: RowActions;
  /** 只有走到的那一列的按鈕在 Tab 的順序裡：整棵樹只有一個停靠點，後面接著這一列的兩顆按鈕。 */
  readonly isTabStop: boolean;
}

function RowButtons({ row, actions, isTabStop }: RowButtonsProps) {
  const { node } = row;
  const tabIndex = isTabStop ? 0 : -1;
  const moreLabel = `「${node.title}」的更多動作`;
  return (
    <div className={styles.actions}>
      <IconButton
        size="sm"
        icon="add"
        label={`在「${node.title}」底下新增頁面`}
        tabIndex={tabIndex}
        disabled={row.level >= KNOWLEDGE_MAX_DEPTH}
        onClick={() => {
          actions.onAddChild(node);
        }}
      />
      <Menu
        menuLabel={moreLabel}
        align="end"
        items={menuItemsOf(row, actions)}
        renderTrigger={(props) => <IconButton {...props} size="sm" icon="more" label={moreLabel} tabIndex={tabIndex} />}
      />
    </div>
  );
}

export interface PageTreeRowProps {
  readonly row: TreeRow;
  readonly spaceKey: string;
  readonly isCurrent: boolean;
  readonly isActive: boolean;
  readonly canEdit: boolean;
  /** 搬移送出中不能再拖。 */
  readonly isDragDisabled: boolean;
  readonly drop: DropZone | null;
  readonly actions: RowActions;
  readonly registerRow: (pageNumber: number, element: HTMLLIElement | null) => void;
  readonly onFocusRow: (pageNumber: number) => void;
  readonly onToggle: (pageNumber: number) => void;
  readonly onOpen: () => void;
}

/** 展開的箭頭：只給滑鼠與觸控（tabindex -1），鍵盤用 → 與 ←。沒有子頁面的列只留同寬的空位，標題才對得齊。 */
function RowToggle({ row, onToggle }: { readonly row: TreeRow; readonly onToggle: (pageNumber: number) => void }) {
  if (!row.hasChildren) return <span className={styles.toggleSpacer} aria-hidden="true" />;
  return (
    <button
      type="button"
      tabIndex={-1}
      className={styles.toggle}
      aria-label={`${row.isExpanded ? '收合' : '展開'}「${row.node.title}」`}
      onClick={() => {
        onToggle(row.node.number);
      }}
    >
      <Icon name={row.isExpanded ? 'chevronDown' : 'chevronRight'} />
    </button>
  );
}

interface RowLinkProps {
  readonly row: TreeRow;
  readonly spaceKey: string;
  readonly titleId: string;
  /** 有限制時，名稱後面接的視覺隱藏的字（treeitem 的名稱用 aria-labelledby 指到標題與它）。 */
  readonly restrictedId: string;
  readonly onOpen: () => void;
}

function RowLink({ row, spaceKey, titleId, restrictedId, onOpen }: RowLinkProps) {
  return (
    <Link to={knowledgePageUrl(spaceKey, row.node.number)} tabIndex={-1} className={styles.link} onClick={onOpen}>
      <span id={titleId} className={styles.title}>
        {row.node.title}
      </span>
      {row.node.isRestricted && (
        <>
          <span id={restrictedId} className="visually-hidden">
            （限制）
          </span>
          <span role="img" aria-label="限制" className={styles.restricted}>
            <Icon name={RESTRICTED_ICON} />
          </span>
        </>
      )}
    </Link>
  );
}

/**
 * 樹上的一列（ARIA 的 treeitem，攤平的清單用 aria-level 表示層級）。焦點在 li 上（roving tabindex），
 * 標題的連結與展開的箭頭只給滑鼠與觸控（tabindex -1）：鍵盤用方向鍵與 Enter。
 * 整列既是被拖的、也是放的地方（@dnd-kit/core）；不套 dnd-kit 的 role 與 tabindex，只留拖曳的說明。
 */
export function PageTreeRow({ row, spaceKey, isCurrent, isActive, canEdit, isDragDisabled, drop, actions, registerRow, onFocusRow, onToggle, onOpen }: PageTreeRowProps) {
  const id = row.node.number;
  const idBase = useId();
  const titleId = `${idBase}-title`;
  const restrictedId = `${idBase}-restricted`;
  const draggable = useDraggable({ id, disabled: !canEdit || isDragDisabled });
  const droppable = useDroppable({ id });
  const setRef = (element: HTMLLIElement | null) => {
    draggable.setNodeRef(element);
    droppable.setNodeRef(element);
    registerRow(id, element);
  };
  return (
    <li
      ref={setRef}
      {...(canEdit ? draggable.listeners : undefined)}
      role="treeitem"
      // 名稱只取標題（與有限制時的「（限制）」）：不然內容裡的展開、新增、更多的按鈕名稱都會串進來。
      aria-labelledby={row.node.isRestricted ? `${titleId} ${restrictedId}` : titleId}
      aria-level={row.level}
      aria-posinset={row.position}
      aria-setsize={row.setSize}
      aria-expanded={row.hasChildren ? row.isExpanded : undefined}
      aria-current={isCurrent ? 'page' : undefined}
      aria-describedby={canEdit ? draggable.attributes['aria-describedby'] : undefined}
      tabIndex={isActive ? 0 : -1}
      data-page={id}
      data-level={row.level}
      data-drop={drop ?? undefined}
      className={cx(styles.item, draggable.isDragging && styles.dragging, canEdit && styles.draggable)}
      onFocus={(event) => {
        if (event.target === event.currentTarget) onFocusRow(id);
      }}
    >
      <div className={styles.row}>
        <RowToggle row={row} onToggle={onToggle} />
        <RowLink row={row} spaceKey={spaceKey} titleId={titleId} restrictedId={restrictedId} onOpen={onOpen} />
        {canEdit && <RowButtons row={row} actions={actions} isTabStop={isActive} />}
      </div>
    </li>
  );
}
