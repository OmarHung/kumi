import { useDraggable } from '@dnd-kit/core';
import { useId, useLayoutEffect, useRef, type ReactNode, type RefObject } from 'react';
import { Link } from 'react-router';
import type { Item } from '../../../api/types';
import { stateLabel } from '../../../domain/blueprint';
import { cx } from '../../../lib/cx';
import type { CardData } from '../../board/boardDnd';
import { useItemPanelLink } from '../../item-page/ItemPanelContext';
import { abilitiesOf } from '../../item-table/ProjectTableContext';
import { DueDateText, PriorityText, StateBadge } from '../../work-items/ItemCells';
import { itemPagePath, useItemPageEntry } from '../../work-items/itemLinks';
import { useMyWorkBoard } from './MyWorkBoardContext';
import { MyWorkMoveMenu, StatePickerMenu } from './MyWorkMoveMenu';
import cardStyles from '../../board/BoardCard.module.css';
import styles from './MyWorkBoard.module.css';

/**
 * 底下的事實：專案、狀態（類別裡有好幾個狀態，要看得出是哪一個；名稱是這張卡片自己專案的藍圖，
 * 還沒讀到時先寫類別）、優先度、到期日（有值才有）。看「我建立的」時多一個負責人（負責人不一定是自己；沒有負責人也寫出來，
 * 那正是追開出去的項目時要看的）。放不下時換行，不縮短。
 */
function CardMeta({ item }: { readonly item: Item }) {
  const { projects, todayDate, showAssignee } = useMyWorkBoard();
  const detail = projects.get(item.projectKey);
  return (
    <dl className={cx(cardStyles.cardMeta, styles.meta)}>
      <div className={cardStyles.cardFact}>
        <dt className="visually-hidden">專案</dt>
        <dd className={styles.projectName}>{detail?.project.name ?? item.projectKey}</dd>
      </div>
      <div className={cardStyles.cardFact}>
        <dt className="visually-hidden">狀態</dt>
        <dd>
          <StateBadge label={stateLabel(detail?.blueprint, item.type, item.state, item.stateCategory)} category={item.stateCategory} />
        </dd>
      </div>
      {showAssignee && (
        <div className={cardStyles.cardFact}>
          <dt className="visually-hidden">負責人</dt>
          <dd>{item.assignee?.displayName ?? '沒有負責人'}</dd>
        </div>
      )}
      {item.priority !== 'none' && (
        <div className={cardStyles.cardFact}>
          <dt className="visually-hidden">優先度</dt>
          <dd>
            <PriorityText priority={item.priority} />
          </dd>
        </div>
      )}
      {item.dueDate !== null && (
        <div className={cardStyles.cardFact}>
          <dt className="visually-hidden">到期日</dt>
          <dd>
            <DueDateText dueDate={item.dueDate} category={item.stateCategory} today={todayDate} />
          </dd>
        </div>
      )}
    </dl>
  );
}

interface CardContentProps {
  readonly item: Item;
  readonly titleId?: string;
  /** 拖曳中跟著指標走的那一份只是畫面：標題不是連結、沒有選單。 */
  readonly isPreview?: boolean;
  readonly actions?: ReactNode;
}

/** 卡片的內容，由上而下：編號、右上角的「移到…」、標題（連到項目頁；寬螢幕一般的點擊在旁邊開項目面板）、底下的事實。 */
function CardContent({ item, titleId, isPreview = false, actions }: CardContentProps) {
  // 刪除項目之後回到這裡（「我的工作」的看板與它的條件）。
  const entry = useItemPageEntry();
  const panelLink = useItemPanelLink(item.key);
  const title = <span className={cardStyles.cardTitleText}>{item.title}</span>;
  return (
    <>
      <div className={cardStyles.cardTop}>
        <span className={cardStyles.cardKey}>{item.key}</span>
        {actions}
      </div>
      <p id={titleId} className={cardStyles.cardTitle}>
        {isPreview ? (
          title
        ) : (
          // 連結原生可以拖（拖出網址）；看板的拖曳由 dnd-kit 處理，不要讓瀏覽器搶走。
          <Link to={itemPagePath(item.projectKey, item.key)} state={entry} className={cardStyles.cardLink} draggable={false} {...panelLink}>
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
  const { focusAfterMoveRef } = useMyWorkBoard();
  useLayoutEffect(() => {
    const isLost = document.activeElement === null || document.activeElement === document.body;
    if (focusAfterMoveRef.current === itemKey && isLost) {
      focusAfterMoveRef.current = null;
      buttonRef.current?.focus();
    }
  });
}

interface MyWorkCardProps {
  readonly item: Item;
  readonly isPending: boolean;
}

/**
 * 「我的工作」看板上的一張卡片。滑鼠與觸控用拖的；鍵盤與螢幕閱讀器用「移到…」。
 * 能不能移動看那個項目自己專案的 item.transition（專案內容還沒讀到時先不能動）；沒有權限時兩個都沒有。
 */
export function MyWorkCard({ item, isPending }: MyWorkCardProps) {
  const { projects, failedKeys, landedKey, picker } = useMyWorkBoard();
  const detail = projects.get(item.projectKey);
  const canTransition = detail !== undefined && abilitiesOf(detail).transition;
  const titleId = useId();
  const buttonRef = useRef<HTMLButtonElement>(null);
  const data: CardData = { kind: 'card', item };
  const canDrag = canTransition && !isPending;
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({ id: item.key, data, disabled: !canDrag });
  useFollowFocus(item.key, buttonRef);
  const actions = canTransition && (
    <>
      <MyWorkMoveMenu item={item} isBusy={isPending} buttonRef={buttonRef} />
      {picker?.item.key === item.key && <StatePickerMenu picker={picker} buttonRef={buttonRef} />}
    </>
  );
  const className = cx(
    cardStyles.card,
    canDrag && cardStyles.draggable,
    isDragging && cardStyles.dragging,
    isPending && cardStyles.pending,
    failedKeys.has(item.key) && cardStyles.failed,
    landedKey === item.key && !isPending && cardStyles.landed,
  );
  // 不套 dnd-kit 的 role、tabIndex：卡片不是一個要用鍵盤操作的按鈕（鍵盤用「移到…」），只留拖曳的說明。
  return (
    <article
      ref={setNodeRef}
      {...listeners}
      className={className}
      data-card={item.key}
      aria-labelledby={titleId}
      aria-describedby={canTransition ? attributes['aria-describedby'] : undefined}
      aria-busy={isPending || undefined}
    >
      <CardContent item={item} titleId={titleId} actions={actions} />
      {isPending && <p className={cardStyles.sending}>送出中…</p>}
    </article>
  );
}

/** 拖曳中跟著指標走的那一份。 */
export function MyWorkCardPreview({ item }: { readonly item: Item }) {
  return (
    <div className={cx(cardStyles.card, cardStyles.overlay)} aria-hidden="true">
      <CardContent item={item} isPreview />
    </div>
  );
}
