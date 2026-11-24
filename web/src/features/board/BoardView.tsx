import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Button, buttonClassName } from '../../components/Button/Button';
import { EmptyState } from '../../components/StatusViews/StatusViews';
import { boardGroupParam } from '../../domain/boardGroup';
import { hasNoCurrentCycle } from '../../domain/planningFilter';
import { PlanningLoadError, NoCurrentCycleHint } from '../item-table/PlanningStates';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { isPlanningFilterBlocked, usePlanning } from '../item-table/usePlanning';
import { projectSettingsPath } from '../work-items/itemLinks';
import type { BoardFilterState } from './useBoardFilters';
import { GroupBoard } from './GroupBoard';
import { useBoardMoves } from './useBoardMoves';
import { useGroupMoves } from './useGroupMoves';
import { WorkflowBoard } from './WorkflowBoard';
import styles from './Board.module.css';

interface BoardViewProps {
  /** 流程與篩選：放在上一層（ProjectWorkspace），頁面的工具列與看板看到同一份。 */
  readonly board: BoardFilterState;
}

/** 還沒有流程：說明之後會有什麼，看得到設定的人直接給到設定的路，其他人知道要找誰（規範 8.1 第 3 點）。 */
function NoWorkflow() {
  const { detail } = useProjectTable();
  const canReadSettings = detail.myPermissions.includes('blueprint.read');
  const action = canReadSettings && (
    <Link to={projectSettingsPath(detail.project.key)} className={buttonClassName('secondary')}>
      到設定加上流程
    </Link>
  );
  return (
    <EmptyState title="這個專案還沒有流程" action={action}>
      {canReadSettings
        ? '設定裡有項目類型與流程之後，這裡才會有欄。'
        : '請有設定權限的人在設定裡加上項目類型與流程，這裡才會有欄。'}
    </EmptyState>
  );
}

/**
 * 篩了週期或里程碑時，看板的每一欄都要靠週期與里程碑的清單才送得出請求：
 * 清單讀不到就整個看板說讀取失敗（不送請求）；篩「目前的週期」而現在沒有進行中的週期，整個看板是空白狀態
 * （一欄一欄都空著沒有說明，看不出為什麼）。都沒有的話回傳 null，照常畫欄。
 */
function usePlanningBlock(board: BoardFilterState): ReactNode {
  const { detail, todayDate } = useProjectTable();
  const planning = usePlanning(detail.project.key);
  const { filters } = board;
  if (isPlanningFilterBlocked(planning, filters)) {
    return (
      <div className={styles.message}>
        <PlanningLoadError planning={planning} />
      </div>
    );
  }
  if (!planning.isCyclesPending && hasNoCurrentCycle(filters.cycle, planning.cycles, todayDate)) {
    return (
      <EmptyState
        title="沒有符合條件的項目"
        action={
          <Button
            onClick={() => {
              board.clearFilters();
            }}
          >
            清除條件
          </Button>
        }
      >
        <NoCurrentCycleHint />
      </EmptyState>
    );
  }
  return null;
}

/**
 * 專案的看板檢視的內容區：依狀態分欄，撐滿頁列與工具列以下的高度，每一欄自己直向捲、看板自己橫向捲。
 * 是有名稱的區域「看板」，只包內容（和表格的「項目」一樣；篩選在頁面的工具列上，旁邊的項目面板不在裡面）。
 * 看板是 lazy 載入的：區域和它的欄一起出現，不會先有一個只寫著「正在載入看板…」的「看板」。
 * 專案有多個流程時一次看一個（預設是第一個類型用的流程），記在網址上；流程、搜尋、負責人在頁面的工具列（BoardToolbar）。
 * 專案的內容、權限、「今天」與成員對話框來自外層的 ProjectTableProvider（和表格同一份）。
 */
export function BoardView({ board }: BoardViewProps) {
  const { detail } = useProjectTable();
  const moves = useBoardMoves(detail.project.key);
  const groupMoves = useGroupMoves(detail.project.key);
  const { workflow, filters } = board;
  const planningBlock = usePlanningBlock(board);
  const { group } = filters;
  const focusFallback = [board.searchRef, board.filterButtonId];
  return (
    <div role="region" aria-label="看板" className={styles.view}>
      {group.kind !== 'state' ? (
        // 依負責人、欄位分欄：不分流程，沒有流程的專案也看得到（只是沒有項目）。
        (planningBlock ?? <GroupBoard key={boardGroupParam(group)} group={group} filters={filters} moves={groupMoves} focusFallback={focusFallback} />)
      ) : workflow === undefined ? (
        <NoWorkflow />
      ) : planningBlock !== null ? (
        planningBlock
      ) : (
        <WorkflowBoard
          key={workflow.key}
          workflow={workflow}
          filters={filters}
          moves={moves}
          focusFallback={focusFallback}
        />
      )}
    </div>
  );
}
