import type { Person, PersonRef } from '../../../api/types';

/** 名單上的人：自己永遠在最前面（伺服器也會補，畫面先照那個順序顯示），其餘照加入的順序。 */
export function withSelfFirst(people: readonly PersonRef[], self: PersonRef): readonly PersonRef[] {
  return [self, ...people.filter((person) => person.personId !== self.personId)];
}

export interface Candidates {
  /** 專案空間的專案成員（排在前面）；組織空間沒有這一組。 */
  readonly members: readonly Person[];
  readonly others: readonly Person[];
}

/**
 * 可以加進名單的人：停用的不列（他不能登入，加進來也看不到）、已經在名單上的不重複。
 * excludesGuests：組織空間的訪客看不到組織空間（專案空間的訪客可以是專案成員，照列）。
 */
export function candidatesOf(
  people: readonly Person[],
  memberIds: ReadonlySet<string>,
  selected: readonly PersonRef[],
  excludesGuests: boolean,
): Candidates {
  const taken = new Set(selected.map((person) => person.personId));
  const available = people.filter((person) => !person.disabled && !taken.has(person.personId) && !(excludesGuests && person.role === 'guest'));
  return {
    members: available.filter((person) => memberIds.has(person.personId)),
    others: available.filter((person) => !memberIds.has(person.personId)),
  };
}

/** 名單有沒有變（順序算，因為自己固定在最前面，順序只會因為加減人而變）。 */
export function isSameList(a: readonly PersonRef[], b: readonly PersonRef[]): boolean {
  return a.length === b.length && a.every((person, index) => person.personId === b[index]?.personId);
}

const ITEM_PATH = /^people\[(\d+)\]$/;

export interface RestrictionProblems {
  /** 依人員編號放的訊息（伺服器的細項路徑 people[i] 對到送出的第 i 個）。 */
  readonly byPerson: ReadonlyMap<string, string>;
  /** 對不到單一個人的訊息。 */
  readonly general: string | null;
}

interface DetailLike {
  readonly path: string | null;
  readonly message: string;
}

/** 422 knowledge.restriction_invalid 的細項：people[i] 對到名單上的人，其他（people 或沒有路徑）放表單上方。 */
export function problemsOf(details: readonly DetailLike[], fallback: string, sent: readonly PersonRef[]): RestrictionProblems {
  if (details.length === 0) return { byPerson: new Map(), general: fallback };
  const byPerson = new Map<string, string>();
  const general: string[] = [];
  for (const detail of details) {
    const index = Number(ITEM_PATH.exec(detail.path ?? '')?.[1] ?? Number.NaN);
    const person = Number.isNaN(index) ? undefined : sent[index];
    if (person === undefined) general.push(detail.message);
    else if (!byPerson.has(person.personId)) byPerson.set(person.personId, detail.message);
  }
  return { byPerson, general: general.length > 0 ? general.join(' ') : null };
}
