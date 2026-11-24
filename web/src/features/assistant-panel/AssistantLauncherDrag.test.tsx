import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { makeAiOptions, serveAiOptions, serveConversations } from '../../test/assistant';
import { LAUNCHER_POSITION_STORAGE_KEY } from './launcherPosition';

function openApp() {
  signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()] });
  serveAiOptions(makeAiOptions());
  serveConversations('SMK', []);
  serveConversations('AGY', []);
  return renderApp('/projects/SMK');
}

async function findLauncher(): Promise<HTMLElement> {
  return screen.findByRole('button', { name: 'AI 助理' });
}

/** 一次拖曳：按下、移到終點、（可選）放開並送出放開後跟著來的 click。jsdom 沒有版面，起點是預設的右 24、下 16。 */
function drag(button: HTMLElement, to: { x: number; y: number }, from = { x: 1000, y: 700 }) {
  fireEvent.pointerDown(button, { pointerId: 1, button: 0, clientX: from.x, clientY: from.y });
  fireEvent.pointerMove(button, { pointerId: 1, clientX: to.x, clientY: to.y });
}

function release(button: HTMLElement, at: { x: number; y: number }) {
  fireEvent.pointerUp(button, { pointerId: 1, clientX: at.x, clientY: at.y });
  fireEvent.click(button);
}

function stored(): unknown {
  const raw = localStorage.getItem(LAUNCHER_POSITION_STORAGE_KEY);
  return raw === null ? null : JSON.parse(raw);
}

describe('拖動右下角的按鈕', () => {
  it('點一下（沒移動）照舊打開面板，不記位置', async () => {
    // Arrange
    openApp();
    const button = await findLauncher();

    // Act
    fireEvent.pointerDown(button, { pointerId: 1, button: 0, clientX: 1000, clientY: 700 });
    release(button, { x: 1000, y: 700 });

    // Assert
    expect(await screen.findByRole('dialog', { name: 'AI 助理' })).toBeInTheDocument();
    expect(stored()).toBeNull();
  });

  it('移動沒超過 4px 還是點一下', async () => {
    // Arrange
    openApp();
    const button = await findLauncher();

    // Act
    drag(button, { x: 1003, y: 702 });
    release(button, { x: 1003, y: 702 });

    // Assert
    expect(await screen.findByRole('dialog', { name: 'AI 助理' })).toBeInTheDocument();
    expect(stored()).toBeNull();
  });

  it('拖過門檻：放開記住離右邊、下邊的距離，而且不打開面板', async () => {
    // Arrange
    openApp();
    const button = await findLauncher();

    // Act
    drag(button, { x: 900, y: 600 });
    expect(button).toHaveAttribute('data-dragging', 'true');
    expect(document.documentElement.style.getPropertyValue('--launcher-right')).toBe('124px');
    release(button, { x: 900, y: 600 });

    // Assert
    expect(stored()).toEqual({ right: 124, bottom: 116 });
    expect(button).toHaveAttribute('data-dragging', 'false');
    expect(screen.queryByRole('dialog', { name: 'AI 助理' })).not.toBeInTheDocument();
  });

  it('拖曳之後的下一次點擊照常打開', async () => {
    // Arrange
    openApp();
    const button = await findLauncher();
    drag(button, { x: 900, y: 600 });
    release(button, { x: 900, y: 600 });

    // Act
    fireEvent.click(button);

    // Assert
    expect(await screen.findByRole('dialog', { name: 'AI 助理' })).toBeInTheDocument();
  });

  it('記住的位置下次載入讀回來，放在根元素的變數上', async () => {
    // Arrange
    localStorage.setItem(LAUNCHER_POSITION_STORAGE_KEY, JSON.stringify({ right: 300, bottom: 200 }));

    // Act
    openApp();
    await findLauncher();

    // Assert
    const root = document.documentElement.style;
    expect(root.getPropertyValue('--launcher-right')).toBe('300px');
    expect(root.getPropertyValue('--launcher-bottom')).toBe('200px');
  });

  it.each(['不是 JSON', '{"right":"a","bottom":1}', '{"right":-5,"bottom":1}', 'null', '[]'])('壞掉的記錄（%s）當作沒有', async (raw) => {
    // Arrange
    localStorage.setItem(LAUNCHER_POSITION_STORAGE_KEY, raw);

    // Act
    openApp();
    await findLauncher();

    // Assert
    expect(document.documentElement.style.getPropertyValue('--launcher-right')).toBe('');
  });

  it('拖到畫面外：夾回畫面裡，四邊至少留 8px', async () => {
    // Arrange
    openApp();
    const button = await findLauncher();

    // Act
    drag(button, { x: -5000, y: -5000 });
    release(button, { x: -5000, y: -5000 });

    // Assert
    expect(stored()).toEqual({ right: window.innerWidth - 48 - 8, bottom: window.innerHeight - 48 - 8 });

    // Act
    drag(button, { x: 9000, y: 9000 });
    release(button, { x: 9000, y: 9000 });

    // Assert
    expect(stored()).toEqual({ right: 8, bottom: 8 });
  });

  it('拖曳中按 Esc：回到開始的位置、不記，放開也不打開', async () => {
    // Arrange
    openApp();
    const button = await findLauncher();
    drag(button, { x: 900, y: 600 });

    // Act
    fireEvent.keyDown(document, { key: 'Escape' });
    fireEvent.pointerMove(button, { pointerId: 1, clientX: 500, clientY: 500 });
    release(button, { x: 500, y: 500 });

    // Assert
    expect(stored()).toBeNull();
    expect(document.documentElement.style.getPropertyValue('--launcher-right')).toBe('');
    expect(screen.queryByRole('dialog', { name: 'AI 助理' })).not.toBeInTheDocument();
  });

  it('pointercancel：回到開始的位置、不記', async () => {
    // Arrange
    openApp();
    const button = await findLauncher();
    drag(button, { x: 900, y: 600 });

    // Act
    fireEvent.pointerCancel(button, { pointerId: 1 });

    // Assert
    expect(stored()).toBeNull();
    expect(document.documentElement.style.getPropertyValue('--launcher-bottom')).toBe('');
    expect(button).toHaveAttribute('data-dragging', 'false');
  });

  it('只有按鈕在預設右下角（48px 以內）時新通知的提示才讓位', async () => {
    // Arrange
    openApp();
    const button = await findLauncher();
    expect(document.documentElement.dataset.assistant).toBe('button');

    // Act / Assert：小移動還算在右下角
    drag(button, { x: 980, y: 680 });
    release(button, { x: 980, y: 680 });
    expect(document.documentElement.dataset.assistant).toBe('button');

    // Act / Assert：移走了就不讓
    drag(button, { x: 500, y: 300 });
    release(button, { x: 500, y: 300 });
    expect(document.documentElement.dataset.assistant).toBeUndefined();
  });
});

describe('放回原位', () => {
  async function openAccountMenu(user: ReturnType<typeof renderApp>['user']) {
    await user.click(screen.getByRole('button', { name: /範例使用者/ }));
  }

  it('沒移動過時帳號選單沒有這一項', async () => {
    // Arrange
    const { user } = openApp();
    await findLauncher();

    // Act
    await openAccountMenu(user);

    // Assert
    expect(await screen.findByRole('menuitem', { name: '我的帳號' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: '把 AI 助理按鈕放回右下角' })).not.toBeInTheDocument();
  });

  it('移動過：選單多一項，按了清掉記錄、焦點到按鈕', async () => {
    // Arrange
    localStorage.setItem(LAUNCHER_POSITION_STORAGE_KEY, JSON.stringify({ right: 300, bottom: 200 }));
    const { user } = openApp();
    const button = await findLauncher();

    // Act
    await openAccountMenu(user);
    await user.click(await screen.findByRole('menuitem', { name: '把 AI 助理按鈕放回右下角' }));

    // Assert
    await waitFor(() => {
      expect(button).toHaveFocus();
    });
    expect(stored()).toBeNull();
    expect(document.documentElement.style.getPropertyValue('--launcher-right')).toBe('');
    expect(document.documentElement.dataset.assistant).toBe('button');
  });

  it('又藏起來又移動過：兩項都列', async () => {
    // Arrange
    localStorage.setItem(LAUNCHER_POSITION_STORAGE_KEY, JSON.stringify({ right: 300, bottom: 200 }));
    const { user } = openApp();
    await findLauncher();
    await user.click(screen.getByRole('button', { name: '隱藏 AI 助理按鈕' }));

    // Act
    await openAccountMenu(user);

    // Assert
    expect(await screen.findByRole('menuitem', { name: '顯示 AI 助理按鈕' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: '把 AI 助理按鈕放回右下角' })).toBeInTheDocument();
  });

  it('點兩下按鈕不當作放回', async () => {
    // Arrange
    localStorage.setItem(LAUNCHER_POSITION_STORAGE_KEY, JSON.stringify({ right: 300, bottom: 200 }));
    openApp();
    const button = await findLauncher();

    // Act
    fireEvent.doubleClick(button);

    // Assert
    expect(stored()).toEqual({ right: 300, bottom: 200 });
  });
});
