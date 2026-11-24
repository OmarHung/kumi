import { useDraggable } from '@dnd-kit/core';
import { useId, useLayoutEffect, useRef, type ReactNode, type RefObject } from 'react';
import { Link } from 'react-router';
import type { Item } from '../../api/types';
import { Avatar } from '../../components/Avatar/Avatar';
import { Badge } from '../../components/Badge/Badge';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { itemTypeName, stateLabel } from '../../domain/blueprint';
import { cx } from '../../lib/cx';
import { useItemPanelLink } from '../item-page/ItemPanelContext';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { DueDateText, PriorityText, StateBadge } from '../work-items/ItemCells';
import { NobodyMark } from '../work-items/selectOptionMarks';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import { useBoard } from './BoardContext';
import type { CardData } from './boardDnd';
import { GroupMoveMenu } from './GroupMoveMenu';
import { MoveMenu } from './MoveMenu';
import styles from './BoardCard.module.css';

interface CardContentProps {
  readonly item: Item;
  readonly titleId?: string;
  /** 拖曳中跟著指標走的那一份只是畫面：標題不是連結、沒有選單。 */
  readonly isPreview?: boolean;
  readonly actions?: ReactNode;
}

/**
 * 負責人：頭像加名字（一行，空間不夠時名字先縮成省略號，卡片不變高）。名字是看得到也唸得到的文字，頭像是裝飾；
 * 滑過頭像看得到完整的名字（title 在不唸的頭像上，報讀軟體不會唸兩次）。沒有負責人是虛線的圓，「沒有負責人」只給報讀軟體。
 */
function AssigneeFact({ item }: { readonly item: Item }) {
  const name = item.assignee?.displayName;
  return (
    <div className={cx(styles.cardFact, styles.assignee)}>
      <dt className="visually-hidden">負責人</dt>
      <dd>
        {name === undefined ? (
          <>
            <span className={styles.avatar} aria-hidden="true" title="沒有負責人">
              <NobodyMark />
            </span>
            <span className="visually-hidden">沒有負責人</span>
          </>
        ) : (
          <>
            <span className={styles.avatar} aria-hidden="true" title={name}>
              <Avatar name={name} size="sm" decorative />
            </span>
            <span className={styles.assigneeName}>{name}</span>
          </>
        )}
      </dd>
    </div>
  );
}

/** 底下一列：優先度、到期日（有值才有，沒有值的不佔位置）、負責人的頭像（靠右）。 */
function CardMeta({ item }: { readonly item: Item }) {
  const { todayDate } = useProjectTable();
  return (
    <dl className={styles.cardMeta}>
      {item.priority !== 'none' && (
        <div className={styles.cardFact}>
          <dt className="visually-hidden">優先度</dt>
          <dd>
            <PriorityText priority={item.priority} />
          </dd>
        </div>
      )}
      {item.dueDate !== null && (
        <div className={styles.cardFact}>
          <dt className="visually-hidden">到期日</dt>
          <dd>
            <DueDateText dueDate={item.dueDate} category={item.stateCategory} today={todayDate} />
          </dd>
        </div>
      )}
      <AssigneeFact item={item} />
    </dl>
  );
}

/**
 * 卡片的內容（docs/DESIGN.md 7.2），由上而下：編號（等寬字）與類型（流程有多種類型時）、右上角的「移到…」、
 * 標題（最多三行，連到項目頁；寬螢幕一般的點擊在旁邊開項目面板）、底下一列的優先度、到期日與負責人。
 * 開面板用的是點擊：dnd-kit 要移動 6px 才開始拖，拖完還會擋掉 50ms 內的點擊，兩者分得開。
 */
export function CardContent({ item, titleId, isPreview = false, actions }: CardContentProps) {
  // 刪除項目之後回到這裡（看板與它的條件）。
  const entry = useItemPageEntry();
  const panelLink = useItemPanelLink(item.key);
  const { detail } = useProjectTable();
  const { types, group } = useBoard();
  const title = <span className={styles.cardTitleText}>{item.title}</span>;
  return (
    <>
      <div className={styles.cardTop}>
        <span className={styles.cardKey}>{item.key}</span>
        {types.length > 1 && <Badge>{itemTypeName(detail.blueprint, item.type)}</Badge>}
        {/* 依狀態分欄時狀態就是欄；依負責人、欄位分欄時卡片自己說。 */}
        {group.kind !== 'state' && (
          <span className={styles.cardState}>
            <span className="visually-hidden">狀態：</span>
            <StateBadge label={stateLabel(detail.blueprint, item.type, item.state, item.stateCategory)} category={item.stateCategory} />
          </span>
        )}
        {actions}
      </div>
      <p id={titleId} className={styles.cardTitle}>
        {isPreview ? (
          title
        ) : (
          // 連結原生可以拖（拖出網址）；看板的拖曳由 dnd-kit 處理，不要讓瀏覽器搶走。
          <Link
            to={itemPagePath(item.projectKey, item.key)}
            state={entry}
            className={styles.cardLink}
            draggable={false}
            {...panelLink}
          >
            {title}
          </Link>
        )}
      </p>
      <CardMeta item={item} />
    </>
  );
}

/** 用選單移動時卡片會換到別的欄（元素重新掛上），焦點原本在它身上就跟過去，鍵盤使用者不會迷路。 */
function useFollowFocus(itemKey: string, buttonRef: RefObject<HTMLButtonElement | null>) {
  const { focusAfterMoveRef } = useBoard();
  useLayoutEffect(() => {
    const isLost = document.activeElement === null || document.activeElement === document.body;
    if (focusAfterMoveRef.current === itemKey && isLost) {
      focusAfterMoveRef.current = null;
      buttonRef.current?.focus();
    }
  });
}

interface BoardCardProps {
  readonly item: Item;
  readonly isPending: boolean;
}

/** 看板上的一張卡片。滑鼠與觸控用拖的；鍵盤與螢幕閱讀器用「移到…」。沒有改狀態的權限時兩個都沒有。 */
export function BoardCard({ item, isPending }: BoardCardProps) {
  const { can } = useProjectTable();
  const { failedKeys, landedKey, group } = useBoard();
  const titleId = useId();
  const buttonRef = useRef<HTMLButtonElement>(null);
  const data: CardData = { kind: 'card', item };
  // 依狀態分欄是改狀態（item.transition）；依負責人、欄位分欄是修改項目（item.update）。
  const canMove = group.kind === 'state' ? can.transition : can.update;
  const canDrag = canMove && !isPending;
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({ id: item.key, data, disabled: !canDrag });
  useFollowFocus(item.key, buttonRef);
  const MenuOfGroup = group.kind === 'state' ? MoveMenu : GroupMoveMenu;
  const menu = canMove && <MenuOfGroup item={item} isBusy={isPending} buttonRef={buttonRef} />;
  const className = cx(
    styles.card,
    canDrag && styles.draggable,
    isDragging && styles.dragging,
    isPending && styles.pending,
    failedKeys.has(item.key) && styles.failed,
    landedKey === item.key && !isPending && styles.landed,
  );
  // 不套 dnd-kit 的 role、tabIndex：卡片不是一個要用鍵盤操作的按鈕（鍵盤用「移到…」），只留拖曳的說明。
  return (
    <article
      ref={setNodeRef}
      {...listeners}
      className={className}
      data-card={item.key}
      aria-labelledby={titleId}
      aria-describedby={canMove ? attributes['aria-describedby'] : undefined}
      aria-busy={isPending || undefined}
    >
      <CardContent item={item} titleId={titleId} actions={menu} />
      {isPending && <p className={styles.sending}>送出中…</p>}
    </article>
  );
}

/** 拖曳中跟著指標走的那一份。 */
export function CardPreview({ item }: { readonly item: Item }) {
  return (
    <div className={cx(styles.card, styles.overlay)} aria-hidden="true">
      <CardContent item={item} isPreview />
    </div>
  );
}

/** 一欄第一次載入時：卡片形狀的骨架（一張紙，裡面是幾行灰條），「載入中…」只給報讀軟體。 */
export function CardSkeleton() {
  return (
    <div className={cx(styles.card, styles.skeleton)}>
      <Skeleton variant="text" rows={3} />
    </div>
  );
}
