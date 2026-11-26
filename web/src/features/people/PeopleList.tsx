import type { Person } from '../../api/types';
import { Avatar } from '../../components/Avatar/Avatar';
import { List, ListRow, type ListMeta } from '../../components/List/List';
import { ORGANIZATION_ROLE_LABELS } from '../../domain/labels';
import type { RenderPersonActions } from './PersonActions';
import { PersonName } from './PersonName';
import { profileText } from './personProfile';
import styles from './PeoplePage.module.css';

interface PeopleListProps {
  readonly people: readonly Person[];
  /** 擁有者與管理員：每一筆的組織角色後面是動作的選單（和寬螢幕的表格一樣）。 */
  readonly renderActions?: RenderPersonActions;
  /** 正在重新啟用的人：名稱後面寫「啟用中…」。 */
  readonly enablingIds?: ReadonlySet<string>;
}

/** 第二行：Email，有職稱或負責範圍時前面多一段「職稱 · 負責範圍」。 */
function metaOf(person: Person): ListMeta[] {
  const profile = profileText(person);
  return [
    ...(profile === null ? [] : [{ key: 'profile', label: '職稱與負責範圍', content: profile }]),
    ...(person.email === null ? [] : [{ key: 'email', content: person.email }]),
  ];
}

/**
 * 窄螢幕：人員改成清單，不用橫向捲動的表格（docs/DESIGN.md 7.1）。一筆是頭像、名稱與 Email 兩行，組織角色在右邊；
 * 內容和表格一樣（自己標「你」、停用的人標「已停用」），沒有 Email 的人就只有名稱。「組織角色：」只給報讀軟體，表格裡它是欄的標題。
 * 每一筆是靜態的（沒有 to），動作的選單才放得進來。
 */
export function PeopleList({ people, renderActions, enablingIds }: PeopleListProps) {
  return (
    <List label="人員">
      {people.map((person, index) => (
        <ListRow
          key={person.personId}
          leading={<Avatar name={person.displayName} decorative />}
          title={<PersonName person={person} isEnabling={enablingIds?.has(person.personId) === true} />}
          titleText={person.displayName}
          meta={metaOf(person)}
          trailing={
            <>
              <span>
                <span className="visually-hidden">組織角色：</span>
                {ORGANIZATION_ROLE_LABELS[person.role]}
              </span>
              {renderActions !== undefined && <span className={styles.listAction}>{renderActions(person)}</span>}
            </>
          }
          isContentStart={index === 0}
        />
      ))}
    </List>
  );
}
