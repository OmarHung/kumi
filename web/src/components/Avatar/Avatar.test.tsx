import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Avatar, avatarToneOf } from './Avatar';

describe('Avatar', () => {
  it('單獨出現時是有名稱的圖', () => {
    // Arrange
    render(<Avatar name="範例使用者" />);

    // Act
    const avatar = screen.getByRole('img', { name: '範例使用者' });

    // Assert
    expect(avatar).toHaveTextContent('範');
  });

  it('旁邊已經有名字時只是裝飾，報讀軟體略過', () => {
    // Arrange
    render(
      <p>
        <Avatar name="Example User" size="sm" decorative />
        Example User
      </p>,
    );

    // Act
    const image = screen.queryByRole('img');

    // Assert
    expect(image).not.toBeInTheDocument();
    expect(screen.getByText('EU')).toHaveAttribute('aria-hidden', 'true');
  });
});

const NAMES = [
  '範例使用者',
  '範例成員',
  '範例設計師',
  '範例管理員',
  '測試人員',
  '王小明',
  '李大華',
  '陳美玲',
  'Example User',
  'Example Admin',
  'Alex Chen',
  'QA 主管',
];

describe('Avatar 的色調（docs/DESIGN.md 第 4 節）', () => {
  it('同一個名稱永遠是同一個色調：在哪個畫面、哪種大小、裝飾與否都一樣', () => {
    // Arrange
    render(
      <>
        <Avatar name="範例成員" />
        <Avatar name="範例成員" size="sm" decorative />
      </>,
    );

    // Act
    const standalone = screen.getByRole('img', { name: '範例成員' });
    const decorative = screen.getByText('範', { selector: '[aria-hidden="true"]' });

    // Assert
    expect(standalone).toHaveAttribute('data-tone', avatarToneOf('範例成員'));
    expect(decorative).toHaveAttribute('data-tone', standalone.getAttribute('data-tone'));
    expect(avatarToneOf('範例成員')).toBe(avatarToneOf('範例成員'));
  });

  it('色調是 1 到 6 其中一個；幾個不同的名稱分散到不同的色調，不會擠在同一種', () => {
    // Act
    const tones = NAMES.map(avatarToneOf);

    // Assert
    expect(tones.every((tone) => tone !== undefined && ['1', '2', '3', '4', '5', '6'].includes(tone))).toBe(true);
    expect(new Set(tones).size).toBeGreaterThanOrEqual(4);
  });

  it('名稱前後的空白不影響色調（同一個人，資料裡多了空白也是同一個顏色）', () => {
    // Act
    const trimmed = avatarToneOf('Example User');
    const padded = avatarToneOf('  Example User ');

    // Assert
    expect(padded).toBe(trimmed);
  });

  it('沒有名稱（只畫「?」）時維持中性：沒有 data-tone', () => {
    // Arrange
    render(<Avatar name="  " />);

    // Act
    const avatar = screen.getByRole('img');

    // Assert
    expect(avatar).toHaveTextContent('?');
    expect(avatar).not.toHaveAttribute('data-tone');
  });
});
