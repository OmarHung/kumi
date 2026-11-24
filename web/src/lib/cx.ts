/** 合併 className，略過 false、null、undefined 與空字串。 */
export function cx(...classes: readonly (string | false | null | undefined)[]): string {
  return classes.filter(Boolean).join(' ');
}
