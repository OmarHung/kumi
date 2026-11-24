import { Notice } from '../../components/Notice/Notice';
import { stateLabel } from '../../domain/blueprint';
import { updateThenFocus, type FocusTarget } from '../../lib/updateThenFocus';
import { TransitionProblem } from '../item-table/cells/TransitionProblem';
import { useProjectTable } from '../item-table/ProjectTableContext';
import type { MoveProblem } from './useBoardMoves';
import styles from './Board.module.css';

interface MoveProblemsProps {
  readonly problems: readonly MoveProblem[];
  readonly onDismiss: (itemKey: string) => void;
  /** 卡片與別的說明都不在畫面上時，焦點的去處（工具列的搜尋框；窄螢幕是「篩選」）。 */
  readonly focusFallback: readonly FocusTarget[];
  /** 說明裡「沒有移到…，留在…」的兩個名字；依狀態分欄時是狀態的名稱（預設），依負責人、欄位分欄時是欄的名稱。 */
  readonly labelsOf?: (problem: MoveProblem) => { readonly from: string; readonly target: string };
}

/** 已經在畫面上的元素當成焦點的候選（updateThenFocus 收 ref 或 id）。 */
function present(element: Element | null | undefined): FocusTarget | null {
  return element instanceof HTMLElement ? { current: element } : null;
}

/** 一則說明的「知道了」：Notice 裡最後一個按鈕（前面可能有「到『成員』調整角色」）。 */
function dismissButtonOf(notice: Element | null): Element | undefined {
  return notice === null ? undefined : [...notice.querySelectorAll('button')].at(-1);
}

/**
 * 「知道了」按了，那則說明連同按鈕一起不見：焦點原本在說明裡的話，依序交給那張卡片的「移到…」
 * （卡片已經回到原欄、還在畫面上）、下一則（沒有就上一則）說明的「知道了」、工具列的搜尋框。
 * 焦點不在說明裡（例如用滑鼠點、焦點在別處）就不搬動。
 */
export function dismissKeepingFocus(itemKey: string, onDismiss: (itemKey: string) => void, fallback: readonly FocusTarget[]) {
  const notice = document.querySelector(`[data-move-problem="${itemKey}"]`);
  if (notice === null || !notice.contains(document.activeElement)) {
    onDismiss(itemKey);
    return;
  }
  const candidates = [
    present(document.querySelector(`[data-card="${itemKey}"] button[aria-haspopup="menu"]`)),
    present(dismissButtonOf(notice.nextElementSibling ?? notice.previousElementSibling)),
    ...fallback,
  ].filter((target): target is FocusTarget => target !== null);
  updateThenFocus(() => {
    onDismiss(itemKey);
  }, candidates);
}

/**
 * 被後端擋下來的移動：卡片已經回到原欄，這裡說明原因（和表格同一套：流程沒有這條路、需要哪些角色、
 * 要先填哪些欄位）。放在看板上方，不會被捲出畫面的欄擋住；每一張被擋的卡片一則危險的 Notice（role="alert"，
 * 螢幕閱讀器馬上唸），「知道了（AGY-4）」帶上對象。
 */
export function MoveProblems({ problems, onDismiss, focusFallback, labelsOf }: MoveProblemsProps) {
  const { detail } = useProjectTable();
  if (problems.length === 0) {
    return null;
  }
  return (
    <div className={styles.problems}>
      {problems.map((problem) => {
        const { item, to, error } = problem;
        const { from, target } = labelsOf?.(problem) ?? {
          from: stateLabel(detail.blueprint, item.type, item.state, item.stateCategory),
          target: stateLabel(detail.blueprint, item.type, to, item.stateCategory),
        };
        return (
          // 外層記著是哪一張卡片的說明：「知道了」之後找焦點的去處用。
          <div key={item.key} data-move-problem={item.key}>
            <Notice
              tone="danger"
              role="alert"
              title={`${item.key}「${item.title}」沒有移到「${target}」，留在「${from}」。`}
              dismissLabel={`知道了（${item.key}）`}
              onDismiss={() => {
                dismissKeepingFocus(item.key, onDismiss, focusFallback);
              }}
            >
              {/* 說明裡的按鈕（「到『成員』調整角色」）照自己的寬度，不撐滿整則提示。 */}
              <div className={styles.problemBody}>
                <TransitionProblem error={error} item={item} to={to} />
              </div>
            </Notice>
          </div>
        );
      })}
    </div>
  );
}
