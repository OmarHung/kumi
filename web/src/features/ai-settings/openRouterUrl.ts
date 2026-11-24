const OPENROUTER_HOST = 'openrouter.ai';

/**
 * 登入要導去的網址：只放行 https 且主機就是 openrouter.ai。伺服器給的網址若被動過手腳（導去釣魚站），
 * 只檢查 scheme 擋不住，所以連主機一起鎖死。
 */
export function isOpenRouterAuthorizeUrl(url: string): boolean {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'https:' && parsed.hostname === OPENROUTER_HOST && parsed.username === '' && parsed.password === '';
  } catch {
    return false;
  }
}
