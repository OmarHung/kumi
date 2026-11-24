import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { TextWithCommands } from './CommandText';

const NOT_LOGGED_IN =
  'Codex 還沒登入：先在網頁按「開始登入」取得登入碼，再在主機上執行 docker compose exec -it ai-worker kumi host login codex。';

describe('TextWithCommands（主機代理說明裡的指令）', () => {
  it('句子裡的主機指令用等寬的 code 顯示，前後的字照舊，整段文字不變（複製出來是原本的指令）', () => {
    // Arrange / Act
    const { container } = render(<TextWithCommands text={NOT_LOGGED_IN} />);

    // Assert
    const codes = container.querySelectorAll('code');
    expect(codes).toHaveLength(1);
    expect(codes[0]?.textContent).toBe('docker compose exec -it ai-worker kumi host login codex');
    expect(container.textContent).toBe(NOT_LOGGED_IN);
  });

  it('指令只在空白處換行：每個字各自不斷行（不會從 ai- 的連字號斷開）', () => {
    // Arrange / Act
    const { container } = render(<TextWithCommands text={NOT_LOGGED_IN} />);

    // Assert
    const words = Array.from(container.querySelectorAll('code > span')).map((word) => word.textContent);
    expect(words).toEqual(['docker', 'compose', 'exec', '-it', 'ai-worker', 'kumi', 'host', 'login', 'codex']);
  });

  it('配對與 claude-code 的登入指令也認得；一段文字裡有兩個指令就畫兩個', () => {
    // Arrange
    const text = '先執行 docker compose exec -it ai-worker kumi host pair，再執行 docker compose exec -it ai-worker kumi host login claude-code。';

    // Act
    const { container } = render(<TextWithCommands text={text} />);

    // Assert
    expect(Array.from(container.querySelectorAll('code')).map((code) => code.textContent)).toEqual([
      'docker compose exec -it ai-worker kumi host pair',
      'docker compose exec -it ai-worker kumi host login claude-code',
    ]);
    expect(container.textContent).toBe(text);
  });

  it('沒有指令的說明照原樣，不多包 code', () => {
    // Arrange / Act
    const { container } = render(<TextWithCommands text="判斷不出 Codex 的計費方式，當作不能共用。" />);

    // Assert
    expect(container.querySelector('code')).toBeNull();
    expect(container.textContent).toBe('判斷不出 Codex 的計費方式，當作不能共用。');
  });
});
