/** 使用者要求減少動態效果。有些環境（測試用的 jsdom）沒有 matchMedia：當成沒有要求。 */
export function prefersReducedMotion(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}
