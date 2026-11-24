import { act } from '@testing-library/react';

/**
 * 「沒有送出」這類否定的斷言前先等一下：MSW 收到請求是非同步的，按完鍵馬上同步看請求的數量幾乎一定是 0，
 * 真的多送了一個也抓不到。讓排隊中的請求與畫面更新跑完（一個短的計時器）再看。
 */
export async function flushPending(ms = 50): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => {
      setTimeout(resolve, ms);
    });
  });
}
