import { initialsOf } from '../../domain/initials';
import styles from './Avatar.module.css';

export interface AvatarProps {
  readonly name: string;
  readonly size?: 'sm' | 'md';
  /** 旁邊已經寫了名字：只是裝飾，報讀軟體略過。 */
  readonly decorative?: boolean;
}

/** 頭像的色調數：tokens.css 的 --color-avatar-1～6。 */
const AVATAR_TONE_COUNT = 6;
// FNV-1a（32 位元）的起始值與質數。
const FNV_OFFSET = 0x811c9dc5;
const FNV_PRIME = 0x01000193;

/**
 * 名稱對到的色調（'1'～'6'）：同一個名稱永遠是同一個，所以同一個人在表格、看板、活動裡都是同一個顏色。
 * 用 FNV-1a 雜湊，不是把字元碼加起來取餘數：中文姓名用字碼加總時六種色調分得不平均（實測 3200 個姓名，最多與最少的色調差兩成），
 * 而且字換了位置（「王小明」「王明小」）會是同一個顏色。名稱是空白時沒有色調（頭像只畫「?」，維持中性）。
 */
export function avatarToneOf(name: string): string | undefined {
  const trimmed = name.trim();
  if (trimmed === '') return undefined;
  let hash = FNV_OFFSET;
  for (const character of trimmed) {
    hash = Math.imul(hash ^ (character.codePointAt(0) ?? 0), FNV_PRIME) >>> 0;
  }
  return String((hash % AVATAR_TONE_COUNT) + 1);
}

/**
 * 負責人、成員、活動作者的頭像：名稱的第一個字（英文取前兩個單字的字首）。
 * 照名稱固定一種色調（docs/DESIGN.md 第 4 節）：一眼認得出是誰。顏色不是唯一的線索——字與旁邊的名字才是，
 * 色調只是幫忙掃過去時認人。
 */
export function Avatar({ name, size = 'md', decorative = false }: AvatarProps) {
  const tone = avatarToneOf(name);
  if (decorative) {
    return (
      <span className={styles.avatar} data-size={size} data-tone={tone} aria-hidden="true">
        {initialsOf(name)}
      </span>
    );
  }
  return (
    <span className={styles.avatar} data-size={size} data-tone={tone} role="img" aria-label={name} title={name}>
      {initialsOf(name)}
    </span>
  );
}
