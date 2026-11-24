import type { ProjectDetail } from '../../../api/types';
import { errorMessage } from '../../../api/http';
import { Notice } from '../../../components/Notice/Notice';
import { findState, stateLabel } from '../../../domain/blueprint';
import type { FocusTarget } from '../../../lib/updateThenFocus';
import { dismissKeepingFocus } from '../../board/MoveProblems';
import { TransitionProblem } from '../../item-table/cells/TransitionProblem';
import { ProjectScope } from './ProjectScope';
import type { MyWorkMoveProblem } from './useMyWorkBoardMoves';
import boardStyles from '../../board/Board.module.css';

interface MyWorkMoveProblemsProps {
  readonly problems: readonly MyWorkMoveProblem[];
  readonly projects: ReadonlyMap<string, ProjectDetail>;
  readonly onDismiss: (itemKey: string) => void;
  /** 卡片與別的說明都不在畫面上時，焦點的去處（頁面工具列的搜尋框）。 */
  readonly focusFallback: readonly FocusTarget[];
}

function Explanation({ problem, detail }: { readonly problem: MyWorkMoveProblem; readonly detail: ProjectDetail | undefined }) {
  if (detail === undefined) {
    return <p>{errorMessage(problem.error)}</p>;
  }
  return (
    <ProjectScope detail={detail}>
      <TransitionProblem error={problem.error} item={problem.item} to={problem.to === '' ? undefined : problem.to} />
    </ProjectScope>
  );
}

/**
 * 被後端擋下來的移動：卡片已經回到原欄，這裡說明原因（和表格、專案的看板同一套：流程沒有這條路、需要哪些角色、
 * 要先填哪些欄位；專案的藍圖、權限是那張卡片自己專案的）。放在看板上方，每一張被擋的卡片一則危險的 Notice
 * （role="alert"，螢幕閱讀器馬上唸），「知道了（CM-12）」帶上對象。
 */
export function MyWorkMoveProblems({ problems, projects, onDismiss, focusFallback }: MyWorkMoveProblemsProps) {
  if (problems.length === 0) {
    return null;
  }
  return (
    <div className={boardStyles.problems}>
      {problems.map((problem) => {
        const { item, to, label, error } = problem;
        const detail = projects.get(item.projectKey);
        const from = stateLabel(detail?.blueprint, item.type, item.state, item.stateCategory);
        const target = label ?? (detail === undefined ? to : (findState(detail.blueprint, item.type, to)?.name ?? to));
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
              <div className={boardStyles.problemBody}>
                <Explanation problem={{ ...problem, error }} detail={detail} />
              </div>
            </Notice>
          </div>
        );
      })}
    </div>
  );
}
