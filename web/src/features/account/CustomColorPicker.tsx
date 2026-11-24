import { useId, useState, type ReactNode } from 'react';
import { TextField } from '../../components/Field/Field';
import { hexToHsv, hsvToHex, type Hsv } from '../../lib/hexColor';
import { HueSlider } from './HueSlider';
import { SaturationArea } from './SaturationArea';
import { useHexDraft } from './useHexDraft';
import styles from './CustomColorPicker.module.css';

interface CustomColorPickerProps {
  /** 目前的顏色，小寫的 #rrggbb。 */
  readonly value: string;
  /** 每一次改動都送出（拖曳中也是），值一律是整理過的 #rrggbb。 */
  readonly onChange: (hex: string) => void;
  /** 色碼欄位右邊的示意（分頁的圖示）：取色器開著時會蓋住頁面上的示意圖。 */
  readonly preview?: ReactNode;
}

/** 打出來的色碼沒有色相（灰、黑、白）時沿用原本的色相：不然打 #000000 再調亮，色相會跳回紅色。 */
function keepHue(next: Hsv, previous: Hsv): Hsv {
  return next.s === 0 || next.v === 0 ? { ...next, h: previous.h } : next;
}

/** SVG 漸層的 id 放在 url(#…) 裡，只留英數字。 */
function useSvgId(): string {
  return `picker-${useId().replace(/[^a-zA-Z0-9]/g, '')}`;
}

/**
 * 自己畫的取色器（不用 <input type="color">：打開是作業系統的取色器，長相與深色模式管不到，docs/design/07-3-controls.md）。
 * 由上到下是飽和度與亮度的方塊、色相的軌道、色碼的欄位；三個都改同一個顏色，鍵盤都操作得到。
 *
 * 自己記著 HSV：色碼換成 HSV 時灰、黑、白沒有色相，拖到全黑再拖亮，色相不能跑掉。外面傳進來的色碼
 * 不是自己剛送出的（別的分頁改了）才用它重算。
 */
export function CustomColorPicker({ value, onChange, preview }: CustomColorPickerProps) {
  const idPrefix = useSvgId();
  const [own, setOwn] = useState(() => ({ hsv: hexToHsv(value), hex: value }));
  const hsv = own.hex === value ? own.hsv : hexToHsv(value);

  // 色碼沒變（拖曳時在同一個色碼裡移動、灰色時拖色相）只記下 HSV，不送出：外面每次都會寫進瀏覽器、重畫分頁的圖示。
  function commit(next: Hsv, hex = hsvToHex(next)) {
    setOwn({ hsv: next, hex });
    if (hex !== value) onChange(hex);
  }
  const hexDraft = useHexDraft(value, (hex) => {
    if (hex !== value) commit(keepHue(hexToHsv(hex), hsv), hex);
  });
  function changeFromSlider(next: Hsv) {
    hexDraft.reset();
    commit(next);
  }

  return (
    <div className={styles.picker}>
      <SaturationArea hsv={hsv} idPrefix={idPrefix} onChange={changeFromSlider} />
      <HueSlider hue={hsv.h} idPrefix={idPrefix} onChange={(h) => { changeFromSlider({ ...hsv, h }); }} />
      <div className={styles.hexRow}>
        <TextField
          label="色碼"
          size="sm"
          maxLength={7}
          spellCheck={false}
          autoComplete="off"
          autoCapitalize="off"
          className={styles.hexField}
          {...hexDraft.fieldProps}
        />
        {preview}
      </div>
    </div>
  );
}
