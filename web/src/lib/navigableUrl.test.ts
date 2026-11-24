import { describe, expect, it } from 'vitest';
import { isNavigableUrl } from './navigableUrl';

describe('isNavigableUrl', () => {
  it.each(['http://127.0.0.1:4567/callback?code=a', 'https://app.example/cb'])('放行 %s', (url) => {
    expect(isNavigableUrl(url)).toBe(true);
  });

  it.each(['javascript:alert(1)', 'data:text/html,x', 'file:///etc/passwd', 'custom-app://cb', '/relative', '', 'not a url'])(
    '擋下 %s',
    (url) => {
      expect(isNavigableUrl(url)).toBe(false);
    },
  );
});
