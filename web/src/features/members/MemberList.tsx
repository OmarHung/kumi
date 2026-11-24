import { List, ListRow } from '../../components/List/List';
import { useMemberParts, type MemberPartsProps } from './MemberParts';
import type { MemberRosterProps } from './MemberRoster';

function MemberItem(props: MemberPartsProps) {
  const { avatar, name, role, remove } = useMemberParts(props, 'md');
  return (
    <ListRow
      leading={avatar}
      title={name}
      titleText={props.member.displayName}
      meta={[{ key: 'role', label: '角色', content: role }]}
      trailing={remove ?? undefined}
    />
  );
}

/**
 * 窄螢幕：成員改成清單（components/List），不用橫向捲動的表格（docs/DESIGN.md 7.1）。一筆是頭像、名稱一行、
 * 角色一行，「移除」在右邊；名稱、按鈕與 id 和表格一樣，移除之後的焦點照樣找得到。
 */
export function MemberList({ detail, canManage, idPrefix, lastAdminNoteId, onRemove, onSaved, onFailed }: MemberRosterProps) {
  return (
    <List label="成員">
      {detail.members.map((member) => (
        <MemberItem
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
    </List>
  );
}
