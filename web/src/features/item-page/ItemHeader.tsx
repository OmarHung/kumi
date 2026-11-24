import type { Ref } from 'react';
import { Link } from 'react-router';
import type { ItemDetail } from '../../api/types';
import type { Crumb } from '../../components/PageBar/PageBar';
import { itemTypeName } from '../../domain/blueprint';
import { formatInstant } from '../../domain/time';
import { TitleCell } from '../item-table/cells/CoreCells';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { itemPagePath } from '../work-items/itemLinks';
import styles from './ItemPage.module.css';

/**
 * 頁列上的位置：專案、上層項目、這個項目（最後一個是目前的頁面）。
 * projectLink：專案的連結——連進來時記下的專案頁（檢視與條件照舊），沒有就照這個專案上次用的檢視。
 */
export function itemCrumbs(item: ItemDetail, projectName: string, projectLink: string): readonly Crumb[] {
  const parent: readonly Crumb[] =
    item.parentKey === null ? [] : [{ label: item.parentKey, to: itemPagePath(item.projectKey, item.parentKey), mono: true }];
  return [{ label: projectName, to: projectLink }, ...parent, { label: item.key, mono: true }];
}

/** 整頁的項目頁是 1；放進面板時降一層，面板的標題列已經是 h2（一頁只有一個 h1）。 */
export type ItemHeadingLevel = 1 | 2;

interface ItemHeaderProps {
  readonly item: ItemDetail;
  readonly headingLevel?: ItemHeadingLevel;
  /** 有給時標題可以取得焦點（tabIndex=-1）：換頁後帶著提示到達時焦點放到這裡。 */
  readonly headingRef?: Ref<HTMLHeadingElement>;
}

/**
 * 內容區最上面的標題區（docs/DESIGN.md 6.2 的閱讀頁）：類型與編號一行（整頁才有）、可以直接改的標題、建立與更新的時間。
 * 位置在頁列上（itemCrumbs），改類型與刪除在頁列（面板是標題列）的「更多動作」（ItemActions）。
 */
export function ItemHeader({ item, headingLevel = 1, headingRef }: ItemHeaderProps) {
  const { detail } = useProjectTable();
  const Heading = headingLevel === 1 ? 'h1' : 'h2';
  return (
    <header className={styles.header}>
      {/* 面板的標題列已經是「類型 編號」，裡面不再畫一次；換成連到專案的連結（整頁有頁列的麵包屑，面板與浮動視窗沒有）。 */}
      {headingLevel === 2 && <ProjectLink />}
      {headingLevel === 1 && (
        <p className={styles.eyebrow}>
          {itemTypeName(detail.blueprint, item.type)} <span className={styles.key}>{item.key}</span>
        </p>
      )}
      <Heading ref={headingRef} tabIndex={headingRef === undefined ? undefined : -1} className={styles.heading}>
        <TitleCell item={item} />
      </Heading>
      <p className={styles.meta}>
        <span>
          建立於 <time dateTime={item.createdAt}>{formatInstant(item.createdAt)}</time>
        </span>{' '}
        <span>
          更新於 <time dateTime={item.updatedAt}>{formatInstant(item.updatedAt)}</time>
        </span>
      </p>
    </header>
  );
}

/**
 * 面板與浮動視窗裡，標題上面連到專案（代號與名稱）：從「我的工作」、收件匣、行事曆打開的項目，原本沒有地方回到它的專案
 * （使用者 2026-11-20 問）。照這個專案上次用的版面開；點了網址換成專案頁，面板跟著關上。
 */
function ProjectLink() {
  const { detail } = useProjectTable();
  const { key, name } = detail.project;
  const link = useProjectPageLink(key);
  return (
    <p className={styles.eyebrow}>
      <Link to={link} className={styles.projectLink}>
        <span className={styles.key}>{key}</span> {name}
      </Link>
    </p>
  );
}

