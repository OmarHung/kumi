import { useId, useRef } from 'react';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { useRovingGrid } from '../../lib/useRovingGrid';
import { tableHint } from '../item-table/tableHint';
import { useMemberParts, type MemberPartsProps } from './MemberParts';
import type { MemberRosterProps } from './MemberRoster';
import styles from './MembersDialog.module.css';

function MemberRow(props: MemberPartsProps) {
  const { avatar, name, role, remove } = useMemberParts(props, 'sm');
  return (
    // data-row-key：停駐點那一列被移除時，照原本的順序找到相鄰的一列（lib/useRovingGrid.ts）。
    <tr data-row-key={props.member.personId}>
      <th scope="row" className={tableStyles.primaryCell}>
        <span className={styles.nameCell}>
          {avatar}
          <span>{name}</span>
        </span>
      </th>
      <td className={styles.roleCell}>{role}</td>
      {remove !== null && <td className={styles.actionCell}>{remove}</td>}
    </tr>
  );
}

/**
 * 寬螢幕：成員與角色的表格，一列一位、一行（列高 --row-height）。成員多時表格自己直向捲動、表頭黏住；
 * 捲動的容器是 TableScroll（有名稱）。整張表格只有一個 Tab 停駐點（docs/DESIGN.md 8.3「清單的鍵盤操作」，
 * lib/useRovingGrid.ts）：方向鍵在格子之間移動，捲動的區塊不再多一個停駐點（走到哪捲到哪）。
 * 角色那一格是下拉：關著時 ↑／↓ 打開清單（不換值），開著時方向鍵都是清單的，表格不跟著動。
 */
export function MemberTable({ detail, canManage, idPrefix, lastAdminNoteId, onRemove, onSaved, onFailed }: MemberRosterProps) {
  const hintId = useId();
  const tableRef = useRef<HTMLTableElement>(null);
  const { gridProps } = useRovingGrid(tableRef);
  return (
    <TableScroll label="成員" className={styles.tableWrap} isFocusable={false}>
      <table ref={tableRef} className={tableStyles.table} aria-describedby={hintId} {...gridProps}>
        <caption className="visually-hidden">成員</caption>
        <thead>
          <tr>
            <th scope="col">名稱</th>
            <th scope="col">角色</th>
            {canManage && (
              <th scope="col">
                <span className="visually-hidden">操作</span>
              </th>
            )}
          </tr>
        </thead>
        <tbody>
          {detail.members.map((member) => (
            <MemberRow
              key={member.personId}
              detail={detail}
              member={member}
              canManage={canManage}
              idPrefix={idPrefix}
              lastAdminNoteId={lastAdminNoteId}
              onRemove={onRemove}
              onSaved={onSaved}
              onFailed={onFailed}
            />
          ))}
        </tbody>
      </table>
      {/* 進到表格時唸一次（表格的說明），不在每一格重複。hidden：只給 aria-describedby 引用，逐字瀏覽時不會再唸一次。 */}
      <p id={hintId} hidden>
        {tableHint(canManage)}
      </p>
    </TableScroll>
  );
}
