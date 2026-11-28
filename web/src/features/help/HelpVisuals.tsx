import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Icon, type IconName } from '../../components/Icon/Icon';
import styles from './HelpVisuals.module.css';

/*
 * 使用說明的圖像元件（docs/DESIGN.md 6.2「使用說明」）：少字、多圖，但圖只用 Kumi 自己的圖示與介面元素畫，
 * 不放插畫與 emoji（第 1 節）。每一種都有文字的結構（清單、details），報讀軟體照樣讀得懂；畫面的示意框一律 aria-hidden，意思寫在旁邊。
 */

export interface FlowStep {
  readonly key: string;
  readonly icon: IconName;
  readonly title: ReactNode;
  readonly detail?: ReactNode;
}

/** 步驟流程：圖示 → 箭頭 → 圖示。寬的時候排成一列，窄的時候直排（容器查詢），本質是一個有序清單。 */
export function StepFlow({ label, steps }: { readonly label: string; readonly steps: readonly FlowStep[] }) {
  return (
    <ol className={styles.flow} aria-label={label}>
      {steps.map((step, index) => (
        <li key={step.key} className={styles.step}>
          {index > 0 && (
            <span className={styles.arrow} aria-hidden="true">
              <Icon name="chevronRight" />
            </span>
          )}
          <span className={styles.stepCard}>
            <span className={styles.stepIcon}>
              <Icon name={step.icon} size={20} />
            </span>
            <span className={styles.stepText}>
              <span className={styles.stepTitle}>{step.title}</span>
              {step.detail !== undefined && <span className={styles.stepDetail}>{step.detail}</span>}
            </span>
          </span>
        </li>
      ))}
    </ol>
  );
}

export interface IconListItem {
  readonly key: string;
  readonly icon: IconName;
  readonly children: ReactNode;
}

/** 一行一件事，前面一個圖示：取代一長串的項目符號。 */
export function IconList({ label, items }: { readonly label?: string; readonly items: readonly IconListItem[] }) {
  return (
    <ul className={styles.iconList} aria-label={label}>
      {items.map((item) => (
        <li key={item.key}>
          <span className={styles.iconListIcon}>
            <Icon name={item.icon} />
          </span>
          <span>{item.children}</span>
        </li>
      ))}
    </ul>
  );
}

interface UiButtonProps {
  readonly icon?: IconName;
  readonly tone?: 'primary' | 'secondary';
  readonly children: ReactNode;
}

/** 文字裡提到的按鈕畫成按鈕的樣子（看得出要按的是哪一個）；只是字，不能按。 */
export function UiButton({ icon, tone = 'secondary', children }: UiButtonProps) {
  return (
    <span className={styles.uiButton} data-tone={tone}>
      {icon !== undefined && <Icon name={icon} />}
      {children}
    </span>
  );
}

export interface Tile {
  readonly key: string;
  readonly icon: IconName;
  readonly title: string;
  readonly hint: ReactNode;
  /** 有就整格是站內連結。 */
  readonly to?: string;
}

function TileContent({ tile }: { readonly tile: Tile }) {
  return (
    <>
      <span className={styles.tileIcon}>
        <Icon name={tile.icon} size={20} />
      </span>
      <span className={styles.tileTitle}>{tile.title}</span>
      <span className={styles.tileHint}>{tile.hint}</span>
    </>
  );
}

/** 一組並排的格子：圖示、名稱、一句話（四種版面、接下來可以看的頁）。 */
export function Tiles({ label, tiles }: { readonly label: string; readonly tiles: readonly Tile[] }) {
  return (
    <ul className={styles.tiles} aria-label={label}>
      {tiles.map((tile) => (
        <li key={tile.key}>
          {tile.to === undefined ? (
            <span className={styles.tile}>
              <TileContent tile={tile} />
            </span>
          ) : (
            <Link to={tile.to} className={styles.tile} data-link="true">
              <TileContent tile={tile} />
            </Link>
          )}
        </li>
      ))}
    </ul>
  );
}

/** 常見問題的一題：平常只看得到問題，點了才展開答案（details，鍵盤與報讀軟體原生就會）。 */
export function FaqItem({ question, children }: { readonly question: string; readonly children: ReactNode }) {
  return (
    <details className={styles.faq}>
      <summary className={styles.faqQuestion}>
        <span>{question}</span>
        <span className={styles.faqChevron} aria-hidden="true">
          <Icon name="chevronDown" />
        </span>
      </summary>
      <p className={styles.faqAnswer}>{children}</p>
    </details>
  );
}

/** 示意圖上的編號圓點，和下面圖例的編號對得上。 */
export function Marker({ n }: { readonly n: number }) {
  return <span className={styles.marker}>{n}</span>;
}
