/** 整頁導過去的網址只放行 http 與 https：javascript:、data: 等其他種類一律當作沒有（OAuth 的導回網址來自外部資料）。 */
export function isNavigableUrl(url: string): boolean {
  try {
    const { protocol } = new URL(url);
    return protocol === 'http:' || protocol === 'https:';
  } catch {
    return false;
  }
}
