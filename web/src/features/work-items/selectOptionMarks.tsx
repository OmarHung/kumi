import type { StateCategory, WorkflowState } from '../../api/blueprint';
import type { PersonRef } from '../../api/types';
import { Avatar } from '../../components/Avatar/Avatar';
import { Icon } from '../../components/Icon/Icon';
import type { SelectOption } from '../../components/Select/Select';
import { NOT_A_MEMBER } from '../../domain/fields';
import { STATE_CATEGORY_LABELS } from '../../domain/labels';
import { cx } from '../../lib/cx';
import { CATEGORY_ICONS } from './ItemCells';
import styles from './selectOptionMarks.module.css';

/*
 * 下拉選項前面的小圖（docs/DESIGN.md 7.3）：表格、看板、項目頁與設定頁共用這一份。
 * 狀態是狀態類別的形狀（和 StateBadge 同一組圖示，16px），人員是頭像（Avatar 的 sm，20px）。
 * Select 把小圖包成 aria-hidden，選到時按鈕上也畫。同一個清單裡每一個選項都給同樣大小的小圖，字才對齊：
 * 「所有人」「沒有負責人」「所有狀態」這類不是某一個人、某一個狀態的，用這裡的佔位小圖。
 * 看板卡片上的「沒有負責人」也用 NobodyMark：同一件事同一種長相。
 * data-mark 是佔位的種類，在開發者工具與測試裡看得出是哪一種（和 Icon 的 data-icon 一樣）。
 */

type StateLike = Pick<WorkflowState, 'key' | 'name' | 'category'>;

function CategoryShape({ category }: { readonly category: StateCategory }) {
  return <Icon name={CATEGORY_ICONS[category]} />;
}

/** 狀態的選項：前面是它的類別的形狀。label 預設是狀態的名稱（狀態格的「待辦（目前）」、設定頁草稿的名稱另外給）。 */
export function stateOption(state: StateLike, label: string = state.name): SelectOption {
  return { value: state.key, label, leading: <CategoryShape category={state.category} /> };
}

/** 一串狀態的選項，照原本的順序。labelOf 給設定頁的草稿用（名稱空著時用 key）。 */
export function stateOptions(states: readonly StateLike[], labelOf?: (state: StateLike) => string): SelectOption[] {
  return states.map((state) => stateOption(state, labelOf?.(state)));
}

const CATEGORY_ORDER: readonly StateCategory[] = ['backlog', 'not_started', 'in_progress', 'done', 'canceled'];

/** 狀態的類別：五個，照固定的順序，前面是各自的形狀。 */
export const CATEGORY_OPTIONS: readonly SelectOption[] = CATEGORY_ORDER.map((category) => ({
  value: category,
  label: STATE_CATEGORY_LABELS[category],
  leading: <CategoryShape category={category} />,
}));

/** 「所有狀態」（篩選不限）不是一個狀態：「沒有值」的短線（規範 7.1），和狀態的形狀一樣大。 */
export function AnyStateMark() {
  return <span className={styles.noState} data-mark="anyState" />;
}

/** 某一個人：頭像。名字查不到（已經不是成員，FieldCells 標成 NOT_A_MEMBER）時是沒有字的頭像，不畫「不」這個字。 */
export function PersonMark({ name }: { readonly name: string }) {
  if (name === NOT_A_MEMBER) {
    return <span className={cx(styles.person, styles.unknownPerson)} data-mark="unknownPerson" />;
  }
  return <Avatar name={name} size="sm" decorative />;
}

/** 沒有人（「沒有負責人」「沒有人」）：虛線的空圓。 */
export function NobodyMark() {
  return <span className={cx(styles.person, styles.nobody)} data-mark="nobody" />;
}

/** 所有人（負責人的篩選不限）：頭像大小的圓裡一個人員的圖示。 */
export function EveryoneMark() {
  return (
    <span className={cx(styles.person, styles.everyone)} data-mark="everyone">
      <Icon name="people" />
    </span>
  );
}

/** 人員的選項：前面是頭像。有職稱時寫在名稱下面（次要文字），篩選時也找得到。 */
export function personOption(person: PersonRef, startsGroup = false, jobTitle?: string): SelectOption {
  return {
    value: person.personId,
    label: person.displayName,
    leading: <PersonMark name={person.displayName} />,
    startsGroup,
    description: jobTitle,
    keywords: jobTitle === undefined ? undefined : [jobTitle],
  };
}
