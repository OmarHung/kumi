import { useId, type ReactNode } from 'react';
import { Link } from 'react-router';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import type { FieldDefinition } from '../../api/blueprint';
import type { ItemDetail, ProjectDetail } from '../../api/types';
import { useItem } from '../../api/queries';
import { isApiError } from '../../api/http';
import { readsAllItems } from '../../domain/projectPermissions';
import { findItemType } from '../../domain/blueprint';
import { fieldDefinition, typeAllowsParent } from '../../domain/fields';
import {
  AssigneeCell,
  CreatedByCell,
  DateCell,
  EstimateCell,
  ParentCell,
  PriorityCell,
  TypeCell,
} from '../item-table/cells/CoreCells';
import { FieldCell } from '../item-table/cells/FieldCells';
import { CycleCell, MilestoneCell } from '../item-table/cells/PlanningCells';
import { StateCell } from '../item-table/cells/StateCell';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { usePlanning } from '../item-table/usePlanning';
import { itemPagePath } from '../work-items/itemLinks';
import { useLeaveWhenHidden } from './useLeaveWhenHidden';
import { SectionHeading, useItemPanelLink } from './ItemPanelContext';
import styles from './ItemFieldsPanel.module.css';

const HTTP_NOT_FOUND = 404;

interface FieldRowProps {
  readonly label: string;
  /** 窄的時候一組只有一半寬；值旁邊還有連結（上層項目的「開啟」）的要一整列，編號才不會被折斷。 */
  readonly isWide?: boolean;
  readonly children: ReactNode;
}

function FieldRow({ label, isWide = false, children }: FieldRowProps) {
  return (
    <div className={styles.row} data-wide={isWide || undefined}>
      <dt className={styles.label}>{label}</dt>
      <dd className={styles.value}>{children}</dd>
    </div>
  );
}

/**
 * 上層項目讀不到（只看自己的人，上層不是他負責也不是他建立的）：後端只給編號，讀那個項目是 404。
 * 看得到全部的人不會有這種事，不多發請求；404 以外的失敗（斷線）照舊給連結，點進去再說。
 */
function useIsParentHidden(parentKey: string | null): boolean {
  const { detail } = useProjectTable();
  const isScoped = parentKey !== null && !readsAllItems(detail.myPermissions);
  const parent = useItem(parentKey ?? '', isScoped);
  return isScoped && isApiError(parent.error) && parent.error.status === HTTP_NOT_FOUND;
}

function ParentRow({ item }: { readonly item: ItemDetail }) {
  const { detail } = useProjectTable();
  const { parentKey } = item;
  const isParentHidden = useIsParentHidden(parentKey);
  // 在項目面板裡：「開啟」把面板換成上層項目；整頁上照樣換到上層的項目頁。
  const panelLink = useItemPanelLink(parentKey ?? '', true);
  // 類型不允許上層項目、但資料裡還留著（藍圖改過）時，只顯示不改。
  const value = typeAllowsParent(detail.blueprint, item.type) ? (
    <ParentCell item={item} />
  ) : (
    <span className={styles.plain}>{parentKey}</span>
  );
  return (
    <FieldRow label="上層項目" isWide>
      <div className={styles.withLink}>
        {value}
        {isParentHidden && <span className={styles.hiddenNote}>（你看不到這個項目）</span>}
        {parentKey !== null && !isParentHidden && (
          <Link
            to={itemPagePath(item.projectKey, parentKey)}
            className={styles.link}
            aria-label={`開啟上層項目 ${parentKey}`}
            onClick={panelLink.onClick}
          >
            開啟
          </Link>
        )}
      </div>
    </FieldRow>
  );
}

/** 週期與里程碑。專案還沒有週期（里程碑）時不佔位子；項目指著已經刪除的那種資料時照樣顯示，才看得到、改得掉。 */
function PlanningRows({ item }: { readonly item: ItemDetail }) {
  const { cycles, milestones } = usePlanning(item.projectKey);
  return (
    <>
      {(cycles.length > 0 || item.cycleId !== null) && (
        <FieldRow label="週期">
          <CycleCell item={item} />
        </FieldRow>
      )}
      {(milestones.length > 0 || item.milestoneId !== null) && (
        <FieldRow label="里程碑">
          <MilestoneCell item={item} />
        </FieldRow>
      )}
    </>
  );
}

/** 網址（旁邊有「開啟」）與長文字要一整列：和其他欄位一樣半欄寬的話，只剩幾個字。 */
const WIDE_FIELD_TYPES: ReadonlySet<FieldDefinition['type']> = new Set(['url', 'long_text']);

/** 這個類型的自訂欄位，照類型定義裡的順序。 */
function customFields(item: ItemDetail, detail: ProjectDetail): FieldDefinition[] {
  const keys = findItemType(detail.blueprint, item.type)?.fields ?? [];
  return keys.flatMap((key) => {
    const definition = fieldDefinition(detail.blueprint, key);
    return definition === undefined ? [] : [definition];
  });
}

/** 欄位那一列一列（狀態、核心欄位、週期與里程碑、這個類型的自訂欄位）。 */
function FieldList({ item }: { readonly item: ItemDetail }) {
  const { detail } = useProjectTable();
  const leaveWhenHidden = useLeaveWhenHidden();
  const showsParent = typeAllowsParent(detail.blueprint, item.type) || item.parentKey !== null;
  return (
    <dl className={styles.list}>
      <FieldRow label="狀態">
        <StateCell item={item} />
      </FieldRow>
      <FieldRow label="類型">
        <TypeCell item={item} />
      </FieldRow>
      <FieldRow label="負責人">
        <AssigneeCell item={item} onAssigned={leaveWhenHidden} />
      </FieldRow>
      <FieldRow label="建立者">
        <CreatedByCell item={item} />
      </FieldRow>
      <FieldRow label="優先度">
        <PriorityCell item={item} />
      </FieldRow>
      <FieldRow label="開始日">
        <DateCell item={item} field="startDate" />
      </FieldRow>
      <FieldRow label="到期日">
        <DateCell item={item} field="dueDate" />
      </FieldRow>
      <FieldRow label="估計">
        <EstimateCell item={item} />
      </FieldRow>
      {showsParent && <ParentRow item={item} />}
      <PlanningRows item={item} />
      {customFields(item, detail).map((definition) => (
        <FieldRow key={definition.key} label={definition.name} isWide={WIDE_FIELD_TYPES.has(definition.type)}>
          <FieldCell item={item} definition={definition} />
        </FieldRow>
      ))}
    </dl>
  );
}

/**
 * 欄位面板：狀態、核心欄位與這個類型的自訂欄位。編輯器與表格的格子是同一套，權限也一樣看 myPermissions。
 * 寬的時候是右邊的一欄，窄的時候排在標題下面（原始碼在說明之前）：兩種排法裡它都在第一筆內容的位置
 * （寬螢幕和說明同一列、窄螢幕最上面），所以 data-content-start 標在這裡（docs/DESIGN.md 第 14 節）。
 * 這個項目自己專案的週期與里程碑（收件匣、我的工作開的面板也是）載入完才畫列：專案有沒有它們決定欄位區有幾列，
 * 載入後才出現會把下面往下推。
 */
export function ItemFieldsPanel({ item }: { readonly item: ItemDetail }) {
  const headingId = useId();
  const planning = usePlanning(item.projectKey);
  return (
    <section className={styles.panel} aria-labelledby={headingId} data-content-start>
      <SectionHeading id={headingId} className={styles.heading}>
        欄位
      </SectionHeading>
      {planning.isPending ? <LoadingState /> : <FieldList item={item} />}
    </section>
  );
}
