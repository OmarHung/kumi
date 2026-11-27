import { Fragment, type MouseEvent, type ReactNode } from 'react';
import { Link, type To } from 'react-router';
import { cx } from '../../lib/cx';
import styles from './List.module.css';

/**
 * 清單放在哪一種版面（和 TableScroll 的 layout 同一個意思，docs/DESIGN.md 6.2、7.1）：
 * - fill：工作區頁。撐滿剩下的高度、自己直向捲。
 * - page：清單頁或對話框。跟著整頁（或對話框）捲，自己不捲。
 */
export type ListLayout = 'fill' | 'page';

interface ListProps {
  /** 清單的名稱（aria-label），和寬螢幕的表格同一個，例如「項目」「我的工作」。 */
  readonly label: string;
  readonly layout?: ListLayout;
  /** 換了條件、新的結果還沒回來，暫時顯示舊的結果（aria-busy、變淡）。 */
  readonly isBusy?: boolean;
  /** 清單下面的東西（「已載入 N 個項目」與「載入更多」）；fill 時跟著清單一起捲。 */
  readonly footer?: ReactNode;
  /**
   * 48rem 以下往左右伸出內容區的留白、細線畫到畫面的邊，每一筆自己的內距讓文字回到和標題、工具列同一條左緣
   * （只給直接放在內容區的整頁清單用；放在卡片或設定頁右欄裡的不要）。寬螢幕不變。
   */
  readonly isFlush?: boolean;
  /** 一筆一個 ListRow。 */
  readonly children: ReactNode;
}

/**
 * 窄螢幕取代表格的清單（docs/DESIGN.md 7.1）：細線分隔、一筆至少 --row-height。
 * 外層是隱藏文字的定位基準（position: relative），絕對定位的隱藏文字不會把頁面撐寬。
 */
export function List({ label, layout = 'page', isBusy = false, isFlush = false, footer, children }: ListProps) {
  return (
    <div className={styles.frame} data-layout={layout} data-flush={isFlush || undefined}>
      <ul className={styles.list} aria-label={label} aria-busy={isBusy}>
        {children}
      </ul>
      {footer}
    </div>
  );
}

/** 第二行的一個欄位。label 只給報讀軟體（「優先度：」），畫面上靠位置與樣子就看得懂。 */
export interface ListMeta {
  readonly key: string;
  readonly label?: string;
  readonly content: ReactNode;
  /** 整個欄位只給報讀軟體（例如「沒有負責人」）：畫面上不佔位置，也不多一段間隔。 */
  readonly isVisuallyHidden?: boolean;
}

interface ListRowProps {
  /** 有值時整筆是一個連結（點擊範圍就是整筆）；沒有時是靜態的一筆，裡面才可以放控制項。 */
  readonly to?: To;
  /** 連結帶著的 navigate state（例如刪除之後要回到哪裡）。 */
  readonly linkState?: unknown;
  /** 最前面（頭像）。 */
  readonly leading?: ReactNode;
  /** 等寬的編號或代號（AGY-12、#3）。 */
  readonly code?: ReactNode;
  /** 主要的文字：一行，放不下用省略號。 */
  readonly title: ReactNode;
  /** 完整的標題（title 屬性）；title 是字串時預設就用它。 */
  readonly titleText?: string;
  /** 第二行：狀態、負責人、到期日…（有才出現）。 */
  readonly meta?: readonly ListMeta[];
  /** 最後面：角色、「移除」之類的控制項（控制項只放在沒有 to 的一筆裡）。 */
  readonly trailing?: ReactNode;
  /** 第一筆內容：標 data-content-start，給 scripts/ui-audit.js 量高度預算（docs/DESIGN.md 6.2）。 */
  readonly isContentStart?: boolean;
  /** 選取的那一筆（例如旁邊的項目面板開著它）：連結標 aria-current="true"，長相是選取的底色、標題是強調色，沒有色條（7.1）。 */
  readonly isCurrent?: boolean;
  /** 連結的點擊（有 to 才有）：例如一般的點擊改成開面板（呼叫端 preventDefault），href 照舊。 */
  readonly onLinkClick?: (event: MouseEvent<HTMLAnchorElement>) => void;
  /** 連結的非主要按鍵點擊（中鍵開新分頁；onClick 不會來）。 */
  readonly onLinkAuxClick?: (event: MouseEvent<HTMLAnchorElement>) => void;
}

function MetaLine({ meta }: { readonly meta: readonly ListMeta[] }) {
  return (
    <span className={styles.meta}>
      {meta.map((entry, index) => (
        <Fragment key={entry.key}>
          {/* 欄位之間的空白：畫面上是 gap，連結的名稱與報讀軟體靠它分開（「進行中 接案示範」）。 */}
          {index > 0 && ' '}
          <span className={entry.isVisuallyHidden === true ? 'visually-hidden' : styles.metaItem}>
            {entry.label !== undefined && <span className="visually-hidden">{`${entry.label}：`}</span>}
            {entry.content}
          </span>
        </Fragment>
      ))}
    </span>
  );
}

function RowContent({ leading, code, title, titleText, meta = [], trailing }: ListRowProps) {
  const fullTitle = titleText ?? (typeof title === 'string' ? title : undefined);
  return (
    <>
      {leading !== undefined && <span className={styles.leading}>{leading}</span>}
      <span className={styles.body}>
        <span className={styles.head}>
          {code !== undefined && <span className={styles.code}>{code}</span>}
          {code !== undefined && ' '}
          <span className={styles.title} title={fullTitle}>
            {title}
          </span>
        </span>
        {meta.length > 0 && ' '}
        {meta.length > 0 && <MetaLine meta={meta} />}
      </span>
      {trailing !== undefined && ' '}
      {trailing !== undefined && <span className={styles.trailing}>{trailing}</span>}
    </>
  );
}

/**
 * 清單的一筆：[頭像] 編號 標題／第二行 [右邊的東西]。有 to 時整筆是連結（名稱就是這一筆所有的文字，
 * 所以第二行的欄位要帶 label），沒有 to 時是靜態的一筆。
 */
export function ListRow(props: ListRowProps) {
  const { to, linkState, isContentStart = false, isCurrent = false, onLinkClick, onLinkAuxClick } = props;
  return (
    <li className={styles.row} data-content-start={isContentStart || undefined}>
      {to === undefined ? (
        <div className={styles.content}>
          <RowContent {...props} />
        </div>
      ) : (
        <Link
          to={to}
          state={linkState}
          className={cx(styles.content, styles.link)}
          aria-current={isCurrent ? 'true' : undefined}
          onClick={onLinkClick}
          onAuxClick={onLinkAuxClick}
        >
          <RowContent {...props} />
        </Link>
      )}
    </li>
  );
}
