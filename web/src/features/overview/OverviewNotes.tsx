import { OVERVIEW_NUMBERS } from './overviewColumns';
import styles from './Overview.module.css';

/**
 * 表格下面的說明：每個數字的定義（表頭只放得下名稱）。只算沒有刪除的項目；只看得到自己項目的專案只算自己的。
 * 預設收起：常用的人不需要每次看到定義（漸進揭露），要查的人點開；表頭的 title 照舊帶同一句。
 */
export function OverviewNotes() {
  return (
    <details className={styles.notes}>
      <summary className={styles.notesSummary}>數字怎麼算</summary>
      <ul>
        {OVERVIEW_NUMBERS.map((number) => (
          <li key={number.id}>
            <strong>{number.label}</strong>：{number.description}。
          </li>
        ))}
      </ul>
    </details>
  );
}
