import type { ItemDetail } from '../../api/types';
import { itemTypeName } from '../../domain/blueprint';
import { childrenNote, parentSentence, stateSentence, type ArchivedValue, type TypeChangePreview } from '../../domain/itemTypeChange';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { ChildLinks } from './ChildLinks';
import styles from './ItemDialogs.module.css';

function ArchivedLines({ archived, typeName }: { readonly archived: readonly ArchivedValue[]; readonly typeName: string }) {
  if (archived.length === 0) {
    return <p className={styles.text}>沒有欄位的值要封存。</p>;
  }
  return (
    <>
      <p className={styles.text}>{`這些欄位的值會封存起來，${typeName}沒有這些欄位：`}</p>
      <ul className={styles.list} aria-label="會封存的值">
        {archived.map((field) => (
          <li key={field.key}>{field.value === null ? `「${field.name}」` : `「${field.name}」：${field.value}`}</li>
        ))}
      </ul>
    </>
  );
}

function parentReasonId(id: string): string {
  return `${id}-parent`;
}

function childrenReasonId(id: string): string {
  return `${id}-children`;
}

/** 前端看得出來會被擋下時，說明原因的那幾段的 id（送出鈕的 aria-describedby）；沒有被擋下是空的。 */
export function blockedReasonIds(id: string, preview: TypeChangePreview): readonly string[] {
  return [
    ...(preview.parentProblem === null ? [] : [parentReasonId(id)]),
    ...(preview.blockedChildren.length === 0 ? [] : [childrenReasonId(id)]),
  ];
}

interface ChangeTypePreviewProps {
  readonly item: ItemDetail;
  /** 還沒選新類型時是 null，區域留著但是空的。 */
  readonly preview: TypeChangePreview | null;
  readonly id: string;
}

/**
 * 選了新類型之後會發生什麼：狀態怎麼搬、哪些欄位的值會封存、上下層會不會擋下。
 * 一直掛著的即時區域（aria-live），換選項時螢幕報讀器會唸出新的說明；選單用 aria-describedby 連過來。
 */
export function ChangeTypePreview({ item, preview, id }: ChangeTypePreviewProps) {
  const { detail } = useProjectTable();
  const { blueprint } = detail;
  const typeName = preview === null ? '' : `「${itemTypeName(blueprint, preview.toType)}」`;
  const parent = preview === null ? '' : parentSentence(blueprint, item, preview);
  const partial = preview === null ? '' : childrenNote(preview);
  return (
    <section id={id} aria-label="改了之後" aria-live="polite" className={styles.preview}>
      {preview !== null && (
        <>
          <p className={styles.text}>{stateSentence(blueprint, preview)}</p>
          <ArchivedLines archived={preview.archived} typeName={typeName} />
          {parent !== '' && (
            <p id={parentReasonId(id)} className={styles.warning}>
              {parent}
            </p>
          )}
          {preview.blockedChildren.length > 0 && (
            <>
              <p id={childrenReasonId(id)} className={styles.warning}>
                {`這些下層項目不能掛在${typeName}底下，要先移開或刪除它們才能改：`}
              </p>
              <ChildLinks
                projectKey={item.projectKey}
                keys={preview.blockedChildren.map((child) => child.key)}
                known={preview.blockedChildren}
                suffix={(child) => `（${itemTypeName(blueprint, child.type)}）`}
              />
            </>
          )}
          {partial !== '' && <p className={styles.text}>{partial}</p>}
        </>
      )}
    </section>
  );
}
