import { Link } from 'react-router';
import type { Item } from '../../api/types';
import { itemPagePath } from '../work-items/itemLinks';
import styles from './ItemDialogs.module.css';

interface ChildLinksProps {
  readonly projectKey: string;
  /** 要列出的下層編號；known 裡有的另外帶標題。 */
  readonly keys: readonly string[];
  readonly known: readonly Item[];
  /** 每一個後面接的說明，例如類型的名稱。 */
  readonly suffix?: (item: Item) => string;
}

/** 擋下刪除或改類型的下層項目，各自連到項目頁，使用者可以直接去處理。 */
export function ChildLinks({ projectKey, keys, known, suffix }: ChildLinksProps) {
  return (
    <ul className={styles.children} aria-label="下層項目">
      {keys.map((key) => {
        const item = known.find((candidate) => candidate.key === key);
        const text = item === undefined ? key : `${key} ${item.title}`;
        return (
          <li key={key}>
            <Link to={itemPagePath(projectKey, key)}>{text}</Link>
            {item !== undefined && suffix !== undefined && <span className={styles.muted}>{suffix(item)}</span>}
          </li>
        );
      })}
    </ul>
  );
}
