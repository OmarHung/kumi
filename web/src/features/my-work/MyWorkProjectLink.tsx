import { Link } from 'react-router';
import { useProjectPageLink } from '../projects/projectViewPreference';
import styles from './MyWork.module.css';

interface MyWorkProjectLinkProps {
  readonly projectKey: string;
  /** 畫面上的名稱（專案內容還沒讀到時是代號）。 */
  readonly name: string;
}

/**
 * 連到專案頁的專案名稱（表格的專案格、依專案分組的組名）：照這個專案上次用的檢視開，和側欄、麵包屑一樣
 * （features/projects/projectViewPreference.ts）。平常是一般的字色，滑過才是連結的樣子：它是位置的說明，不搶標題。
 * 放不下時省略號，完整的名稱在 title。
 */
export function MyWorkProjectLink({ projectKey, name }: MyWorkProjectLinkProps) {
  const to = useProjectPageLink(projectKey);
  return (
    <Link to={to} className={styles.projectLink} title={name}>
      {name}
    </Link>
  );
}
