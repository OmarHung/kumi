import { useState } from 'react';
import type { Item } from '../../../api/types';
import { stateLabel, workflowForType } from '../../../domain/blueprint';
import { reachableStates } from '../../../domain/workflow';
import { StateBadge } from '../../work-items/ItemCells';
import { stateOption, stateOptions } from '../../work-items/selectOptionMarks';
import { useProjectTable } from '../ProjectTableContext';
import { EditableCell } from './EditableCell';
import { SelectEditor } from './editors';
import { isModalDialogOpen, useFillableFields } from './fillFields';
import { FillFieldsDialog } from './FillFieldsDialog';
import { useStateAnnouncement } from './stateAnnouncement';
import { TransitionProblem } from './TransitionProblem';
import type { FillRequest } from './useFillSubmit';
import { UNCHANGED, type SaveCommand } from './useItemCommand';
import styles from './Cells.module.css';

/**
 * 補填對話框的開關，以及補填完成後要唸的結果（null：還沒開過，status 區域不必掛著）。
 * 頁面提供了 StateAnnouncementContext（我的工作）時，結果交給頁面合成一句唸，格子自己的 status 不掛。
 */
function useStateFill(item: Item) {
  const { detail } = useProjectTable();
  const announceToPage = useStateAnnouncement();
  const fillableFields = useFillableFields();
  const [request, setRequest] = useState<FillRequest | null>(null);
  const [announcement, setAnnouncement] = useState<string | null>(null);
  return {
    request,
    announcement,
    /** 送出失敗時：是「要先填欄位」而且能在原地補，就開對話框（格子結束編輯、焦點回到格子），否則照舊在格子裡說明。 */
    open: (error: unknown, attempted: SaveCommand): boolean => {
      if (attempted.kind !== 'transition' || isModalDialogOpen()) return false;
      const fields = fillableFields(error, item);
      if (fields === null) return false;
      setRequest({ item, to: attempted.to, fields });
      // 區域先掛著（空的），補填完成時才填字，報讀軟體才唸得到。
      if (announceToPage === null) setAnnouncement('');
      return true;
    },
    close: () => {
      setRequest(null);
    },
    finish: (moved: Item, filled: readonly string[]) => {
      const names = filled.map((name) => `「${name}」`).join('、');
      const state = stateLabel(detail.blueprint, moved.type, moved.state, moved.stateCategory);
      const message = `已填好${names}，${moved.key} 已移到「${state}」。`;
      setRequest(null);
      if (announceToPage === null) setAnnouncement(message);
      else announceToPage({ item: moved, message });
    },
  };
}

/**
 * 狀態：選單只列從目前狀態走得到的。被流程擋下時：缺欄位就開補填的對話框（docs/DESIGN.md 8.3「補填再前進」），
 * 角色不夠、流程沒有這條路照舊在格子裡說明。表格、項目頁（含面板）、我的工作都用這一格。
 */
export function StateCell({ item }: { readonly item: Item }) {
  const { detail, can } = useProjectTable();
  const fill = useStateFill(item);
  const label = stateLabel(detail.blueprint, item.type, item.state, item.stateCategory);
  const workflow = workflowForType(detail.blueprint, item.type);
  const reachable = workflow ? reachableStates(workflow, item.state) : [];
  // 每一個選項前面是狀態類別的形狀（規範 7.3），目前的狀態也是。
  const options = [
    stateOption({ key: item.state, name: `${label}（目前）`, category: item.stateCategory }),
    ...stateOptions(reachable),
  ];
  return (
    <>
      <EditableCell
        item={item}
        label="狀態"
        text={label}
        canEdit={can.transition}
        errorPaths={['state']}
        initialDraft={() => item.state}
        toCommand={(draft) => (draft === item.state ? UNCHANGED : { kind: 'transition', to: draft })}
        renderEditor={(api) => (
          <>
            <SelectEditor api={api} options={options} />
            {reachable.length === 0 && <span className={styles.hint}>從這個狀態不能再轉到別的狀態。</span>}
          </>
        )}
        renderError={(error, attempted) => (
          <TransitionProblem error={error} item={item} to={attempted?.kind === 'transition' ? attempted.to : undefined} />
        )}
        onSaveError={fill.open}
      >
        <StateBadge label={label} category={item.stateCategory} />
      </EditableCell>
      {/* 對話框關上時焦點回到打開它的元素：格子這時已經結束編輯，就是這一格。 */}
      {fill.request !== null && <FillFieldsDialog request={fill.request} onClose={fill.close} onMoved={fill.finish} />}
      {fill.announcement !== null && (
        <span role="status" className="visually-hidden">
          {fill.announcement}
        </span>
      )}
    </>
  );
}
