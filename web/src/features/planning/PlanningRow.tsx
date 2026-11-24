import type { ReactNode } from 'react';
import { Link } from 'react-router';
import type { ItemProgress } from '../../api/planning';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu } from '../../components/Menu/Menu';
import { ProgressMeter } from './ProgressMeter';
import styles from './Planning.module.css';

interface PlanningRowProps {
  readonly name: string;
  /** 連到專案的表格並篩這一個。 */
  readonly to: string;
  readonly badges?: ReactNode;
  /** 日期的說法（日期範圍、到期日）。 */
  readonly dates: string;
  readonly description?: string | null;
  readonly progress: ItemProgress;
  /** 沒有 item.update 的人沒有更多選單（入口不顯示，docs/DESIGN.md 8.3「權限」）。 */
  readonly menu?: { readonly onEdit: () => void; readonly onDelete: () => void };
}

/** 週期與里程碑共用的一列：名稱（連結）、狀態、日期、說明、進度、更多選單。 */
export function PlanningRow({ name, to, badges, dates, description, progress, menu }: PlanningRowProps) {
  return (
    <li className={styles.row}>
      <div className={styles.main}>
        <p className={styles.head}>
          <Link to={to} className={styles.name}>
            {name}
          </Link>
          {badges}
        </p>
        <p className={styles.dates}>{dates}</p>
        {description !== null && description !== undefined && description !== '' && <p className={styles.description}>{description}</p>}
      </div>
      <ProgressMeter name={name} progress={progress} />
      {menu !== undefined && (
        <div className={styles.menu}>
          <Menu
            menuLabel={`${name} 的更多動作`}
            align="end"
            renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={`${name} 的更多動作`} size="sm" />}
            items={[
              { key: 'edit', label: '編輯', icon: 'edit', onSelect: menu.onEdit },
              { key: 'delete', label: '刪除', icon: 'delete', tone: 'danger', startsGroup: true, onSelect: menu.onDelete },
            ]}
          />
        </div>
      )}
    </li>
  );
}
