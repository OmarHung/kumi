import { ErrorState } from '../../components/StatusViews/StatusViews';
import { NO_CURRENT_CYCLE_TEXT } from './PlanningFilterFields';
import type { Planning } from './usePlanning';

/** 篩了週期或里程碑、而清單讀不到：不送項目的請求（結果會是錯的），說明並給「重試」（重抓清單）。表格與看板共用。 */
export function PlanningLoadError({ planning }: { readonly planning: Planning }) {
  return <ErrorState error={planning.error} title="週期與里程碑載入失敗" onRetry={planning.retry} />;
}

/** 篩了「目前的週期」、而現在沒有進行中的週期：空白是因為這個，不是沒有項目。表格與看板的空白狀態共用。 */
export function NoCurrentCycleHint() {
  return (
    <>
      <span>{NO_CURRENT_CYCLE_TEXT}</span>
      選別的週期，或清除條件看全部的項目。
    </>
  );
}
