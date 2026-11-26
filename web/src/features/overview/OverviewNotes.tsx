import { OVERVIEW_NUMBERS } from './overviewColumns';
import styles from './Overview.module.css';

/** 表格下面的說明：每個數字的定義（表頭只放得下名稱）。只算沒有刪除的項目；只看得到自己項目的專案只算自己的。 */
export function OverviewNotes() {
  return (
    <section className={styles.notes} aria-label="數字的算法">
      <ul>
        {OVERVIEW_NUMBERS.map((number) => (
          <li key={number.id}>
            <strong>{number.label}</strong>：{number.description}。
          </li>
        ))}
      </ul>
    </section>
  );
}
