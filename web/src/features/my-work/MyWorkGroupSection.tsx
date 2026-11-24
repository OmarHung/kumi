import type { ReactNode } from 'react';
import type { Groupable, MyWorkGroup } from './myWorkGroups';
import { MyWorkProjectLink } from './MyWorkProjectLink';
import styles from './MyWork.module.css';

interface MyWorkGroupSectionProps {
  readonly group: MyWorkGroup<Groupable>;
  /** 這一組的表格或清單，名稱也是 group.label（一組一個有名稱的表格或清單）。 */
  readonly children: ReactNode;
}

/**
 * 一組：標題（h2，報讀軟體用標題瀏覽就能在組之間跳）與數量，下面是這一組的表格或清單。
 * 數量畫面上只有數字；給報讀軟體的是「已過期 2 項」。依專案分組時組名連到那個專案（MyWorkProjectLink）。
 * 不用 section 加名稱：那會多一個地標，和裡面同名的表格（TableScroll 的區域）重複。
 */
export function MyWorkGroupSection({ group, children }: MyWorkGroupSectionProps) {
  return (
    <div className={styles.group}>
      <h2 className={styles.heading}>
        {group.projectKey === undefined ? (
          <span>{group.label}</span>
        ) : (
          <MyWorkProjectLink projectKey={group.projectKey} name={group.label} />
        )}{' '}
        <span className={styles.count}>
          {group.items.length}{' '}
          <span className="visually-hidden">項</span>
        </span>
      </h2>
      {children}
    </div>
  );
}
