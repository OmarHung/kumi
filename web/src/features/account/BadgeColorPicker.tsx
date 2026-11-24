import type { ReactNode } from 'react';
import { Popover } from '../../components/Popover/Popover';
import { TAB_BADGE_COLOR_KEYS, TAB_BADGE_COLORS, type TabBadgeColor, type TabBadgeColorChoice } from '../inbox/faviconBadge';
import { CustomColorPicker } from './CustomColorPicker';
import styles from './BadgeColorPicker.module.css';

interface BadgeColorPickerProps {
  readonly value: TabBadgeColorChoice;
  /** 自訂的顏色，還沒調過是 null。 */
  readonly customColor: string | null;
  readonly onChange: (value: TabBadgeColor) => void;
  /** 按了「自訂顏色」：選成自訂（還沒調過時從目前的顏色開始）。 */
  readonly onSelectCustom: () => void;
  readonly onCustomColorChange: (hex: string) => void;
  /** 取色器裡色碼旁邊的示意（分頁的圖示）。 */
  readonly customPreview?: ReactNode;
}

/** 色輪的六片（每 60° 一個純色）：看得出「這一格是任意的顏色」。 */
const WHEEL_COLORS = ['#ff0000', '#ffff00', '#00ff00', '#00ffff', '#0000ff', '#ff00ff'];
const CENTER = 8;

function pointAt(radius: number, degrees: number): string {
  const angle = ((degrees - 90) * Math.PI) / 180;
  return `${(CENTER + radius * Math.cos(angle)).toFixed(3)} ${(CENTER + radius * Math.sin(angle)).toFixed(3)}`;
}

/** 外半徑 7、內半徑 4.5 的環切成六片。 */
const WHEEL = WHEEL_COLORS.map((color, index) => {
  const [from, to] = [index * 60, index * 60 + 60];
  return {
    color,
    d: `M${pointAt(7, from)}A7 7 0 0 1 ${pointAt(7, to)}L${pointAt(4.5, to)}A4.5 4.5 0 0 0 ${pointAt(4.5, from)}Z`,
  };
});

/** 「自訂顏色」那一格：外圈是色輪，中間是調過的顏色（還沒調過是空的）。 */
function CustomSwatchIcon({ color }: { readonly color: string | null }) {
  return (
    <svg className={styles.dot} viewBox="0 0 16 16" aria-hidden="true" focusable="false">
      {WHEEL.map((wedge) => (
        <path key={wedge.color} d={wedge.d} fill={wedge.color} />
      ))}
      {color !== null && <circle cx={CENTER} cy={CENTER} r="3.5" fill={color} />}
    </svg>
  );
}

interface CustomSwatchProps {
  readonly isSelected: boolean;
  readonly customColor: string | null;
  readonly onSelectCustom: () => void;
  readonly onCustomColorChange: (hex: string) => void;
  readonly preview: ReactNode;
}

/**
 * 「自訂顏色」那一格：同時是選項（aria-pressed）與打開取色器的按鈕（aria-expanded）。還沒選它時按下去先選成自訂
 * （從目前的顏色開始）再打開；選中之後再按是重新打開取色器。
 */
function CustomSwatch({ isSelected, customColor, onSelectCustom, onCustomColorChange, preview }: CustomSwatchProps) {
  // 名稱帶上目前的自訂色碼：報讀軟體聽得出調的是什麼顏色（只看得到色輪中間的一小點）。
  const name = customColor === null ? '自訂顏色' : `自訂顏色（${customColor}）`;
  return (
    <Popover
      label="自訂顏色"
      align="end"
      renderTrigger={(trigger) => (
        <button
          ref={trigger.ref}
          type="button"
          className={styles.swatch}
          data-opens-picker=""
          aria-label={name}
          title={name}
          aria-pressed={isSelected}
          aria-haspopup="dialog"
          aria-expanded={trigger['aria-expanded']}
          aria-controls={trigger['aria-controls']}
          onClick={() => {
            if (!isSelected) onSelectCustom();
            trigger.onClick();
          }}
        >
          <CustomSwatchIcon color={customColor} />
        </button>
      )}
    >
      {customColor !== null && <CustomColorPicker value={customColor} onChange={onCustomColorChange} preview={preview} />}
    </Popover>
  );
}

interface PresetSwatchProps {
  readonly color: TabBadgeColor;
  readonly isSelected: boolean;
  readonly onChange: (value: TabBadgeColor) => void;
}

/** 預設的一色：按目前那一格什麼都不做（和 SegmentedControl 一樣）。 */
function PresetSwatch({ color, isSelected, onChange }: PresetSwatchProps) {
  const { label, hex } = TAB_BADGE_COLORS[color];
  return (
    <button
      type="button"
      className={styles.swatch}
      aria-label={label}
      title={label}
      aria-pressed={isSelected}
      onClick={() => {
        if (!isSelected) onChange(color);
      }}
    >
      <svg className={styles.dot} viewBox="0 0 16 16" aria-hidden="true" focusable="false">
        <circle cx="8" cy="8" r="7" fill={hex} />
      </svg>
    </button>
  );
}

/**
 * 圓點的顏色：一排色票，行為和 SegmentedControl 一樣（群組裡每個選項一顆 aria-pressed 的按鈕，按目前那一顆什麼都不做），
 * 長相也一樣（凹下去的軌道、選中的那一格浮起來），只是每一格畫的是顏色、名稱在 aria-label 與 title。
 * 「自訂顏色」打開自己畫的取色器（不用瀏覽器原生的，docs/design/07-3-controls.md）；它自己一條軌道、跟在六個預設色後面：
 * 320 寬的觸控螢幕七格放不下一行，分成兩條時換行的是整條自訂，不會把一條軌道切成兩段。
 * 色票用 SVG 的 fill 畫：顏色是圖示的資料（和分頁上的圓點同一個色碼），不是頁面的樣式，不經過設計變數。
 */
export function BadgeColorPicker({ value, customColor, onChange, onSelectCustom, onCustomColorChange, customPreview }: BadgeColorPickerProps) {
  return (
    <div role="group" aria-label="顏色" className={styles.group}>
      <div className={styles.track}>
        {TAB_BADGE_COLOR_KEYS.map((color) => (
          <PresetSwatch key={color} color={color} isSelected={color === value} onChange={onChange} />
        ))}
      </div>
      <div className={styles.track}>
        <CustomSwatch
          isSelected={value === 'custom'}
          customColor={customColor}
          onSelectCustom={onSelectCustom}
          onCustomColorChange={onCustomColorChange}
          preview={customPreview}
        />
      </div>
    </div>
  );
}
