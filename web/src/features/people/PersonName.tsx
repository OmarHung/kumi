import type { Person } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Badge } from '../../components/Badge/Badge';
import styles from './PeoplePage.module.css';

interface PersonNameProps {
  readonly person: Person;
  /** 正在重新啟用他：「已停用」換成「啟用中…」，看得出這一列在等結果。 */
  readonly isEnabling?: boolean;
}

/**
 * 人員的名稱與後面的標籤：自己標「你」，停用的人標「已停用」、名稱用灰字（internal/HANDOFF-security-local-ops.md 第 7 節第 2 點）。
 * 灰字只是輔助，「已停用」的文字才是意思（不只靠顏色）。表格與窄螢幕的清單共用。
 */
export function PersonName({ person, isEnabling = false }: PersonNameProps) {
  const user = useCurrentUser();
  return (
    <>
      <span className={person.disabled ? styles.disabledName : undefined}>{person.displayName}</span>
      {person.personId === user.personId && <> <Badge>你</Badge></>}
      {person.disabled && <> <Badge>{isEnabling ? '啟用中…' : '已停用'}</Badge></>}
    </>
  );
}
