/**
 * 取色器滑桿的圓圈（方塊與色相的軌道共用）：裡面是選到的顏色，白圈外再一圈半透明的黑，在任何底色上都看得到。
 * 位置用百分比（SVG 沒有 viewBox 時就是元素的寬高），圓圈本身是固定的 px，不會被拉成橢圓。
 */
export function PickerThumb({ cx, cy, fill }: { readonly cx: string; readonly cy: string; readonly fill: string }) {
  return (
    <>
      <circle cx={cx} cy={cy} r="8" fill="none" stroke="#000" strokeOpacity="0.35" strokeWidth="1" />
      <circle cx={cx} cy={cy} r="7" fill={fill} stroke="#fff" strokeWidth="2" />
    </>
  );
}
