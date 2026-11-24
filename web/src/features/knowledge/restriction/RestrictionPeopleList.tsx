import type { RefObject } from 'react';
import { KNOWLEDGE_MAX_RESTRICTED_PEOPLE } from '../../../api/knowledge';
import type { Person, PersonRef } from '../../../api/types';
import { Avatar } from '../../../components/Avatar/Avatar';
import { Button } from '../../../components/Button/Button';
import { SelectField } from '../../../components/Field/Field';
import { IconButton } from '../../../components/IconButton/IconButton';
import type { SelectOption } from '../../../components/Select/Select';
import type { Candidates } from './restrictionPeople';
import styles from './RestrictionDialog.module.css';

const MEMBERS_GROUP = '專案成員';
const OTHERS_GROUP = '其他人';

function labelOf(person: Person): string {
  return person.email === null ? person.displayName : `${person.displayName}（${person.email}）`;
}

function optionOf(person: Person, groupLabel?: string): SelectOption {
  return {
    value: person.personId,
    label: labelOf(person),
    leading: <Avatar name={person.displayName} size="sm" decorative />,
    keywords: person.email === null ? undefined : [person.email],
    groupLabel,
  };
}

/** 專案成員排前面；兩組都有人時各自標上組名，組織空間（沒有成員那一組）不分組。 */
export function pickerOptions({ members, others }: Candidates): readonly SelectOption[] {
  const isGrouped = members.length > 0 && others.length > 0;
  return [
    ...members.map((person, index) => optionOf(person, isGrouped && index === 0 ? MEMBERS_GROUP : undefined)),
    ...others.map((person, index) => optionOf(person, isGrouped && index === 0 ? OTHERS_GROUP : undefined)),
  ];
}

interface RowProps {
  readonly person: PersonRef;
  readonly isSelf: boolean;
  readonly canEdit: boolean;
  readonly problem: string | undefined;
  readonly onRemove: (personId: string) => void;
}

function PersonRow({ person, isSelf, canEdit, problem, onRemove }: RowProps) {
  return (
    <li className={styles.row}>
      <Avatar name={person.displayName} size="sm" decorative />
      <span className={styles.name}>
        {person.displayName}
        {isSelf && <span className={styles.self}>（你）</span>}
        {problem !== undefined && <span className={styles.problem}>{problem}</span>}
      </span>
      {canEdit && !isSelf && <IconButton icon="close" size="sm" label={`移除 ${person.displayName}`} onClick={() => { onRemove(person.personId); }} />}
    </li>
  );
}

interface PeopleListProps {
  readonly people: readonly PersonRef[];
  readonly selfId: string;
  readonly canEdit: boolean;
  /** null：人員清單還在讀。 */
  readonly candidates: Candidates | null;
  readonly loadError: string | null;
  readonly problems: ReadonlyMap<string, string>;
  readonly pickerRef: RefObject<HTMLButtonElement | null>;
  readonly onAdd: (personId: string) => void;
  readonly onRemove: (personId: string) => void;
  readonly onRemoveAll: (() => void) | null;
}

/**
 * 這一頁自己的名單：一個人一列（自己固定在最前面、不能移除），下面是選人的下拉（人多時有篩選框）和人數。
 * 選了就加進名單、下拉回到空白，可以接著選下一個。
 */
export function RestrictionPeopleList({ people, selfId, canEdit, candidates, loadError, problems, pickerRef, onAdd, onRemove, onRemoveAll }: PeopleListProps) {
  // 自己不在名單上（組織管理員）時，儲存會把他補進去：要把那一個算進去，不然 100 人再加一個就變成 101 人被擋下。
  const willAddSelf = !people.some((person) => person.personId === selfId);
  const isFull = people.length + (willAddSelf ? 1 : 0) >= KNOWLEDGE_MAX_RESTRICTED_PEOPLE;
  const count = `${String(people.length)} / ${String(KNOWLEDGE_MAX_RESTRICTED_PEOPLE)} 人`;
  return (
    <section className={styles.list} aria-label="名單">
      <ul className={styles.rows} aria-label="看得到這一頁的人">
        {people.map((person) => (
          <PersonRow key={person.personId} person={person} isSelf={person.personId === selfId} canEdit={canEdit} problem={problems.get(person.personId)} onRemove={onRemove} />
        ))}
      </ul>
      {canEdit && (
        <SelectField
          ref={pickerRef}
          label="加入人員"
          value=""
          placeholder={candidates === null && loadError === null ? '載入中…' : '選擇人員'}
          disabled={candidates === null || isFull}
          options={candidates === null ? [] : pickerOptions(candidates)}
          hint={isFull ? `名單最多 ${String(KNOWLEDGE_MAX_RESTRICTED_PEOPLE)} 人。` : loadError ?? undefined}
          onChange={onAdd}
        />
      )}
      <div className={styles.footer}>
        <span className={styles.count}>{count}</span>
        {canEdit && onRemoveAll !== null && (
          <Button size="sm" variant="ghost" onClick={onRemoveAll}>
            拿掉限制
          </Button>
        )}
      </div>
    </section>
  );
}
