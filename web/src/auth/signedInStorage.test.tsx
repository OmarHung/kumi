import { screen } from '@testing-library/react';
import { http } from 'msw';
import { beforeEach, describe, expect, it } from 'vitest';
import { exampleMember, exampleOwner, webProject } from '../test/fixtures';
import { failure, ok, signedInAs, signedOut } from '../test/api';
import { flushPending } from '../test/flush';
import { renderApp } from '../test/renderApp';
import { server } from '../test/server';

const PREFERENCE = 'shell.theme';
const PER_PROJECT = 'project.view.WEB';
const LAST_PERSON = 'session.lastPersonId';

function seedPreferences(): void {
  localStorage.setItem(PREFERENCE, 'dark');
  localStorage.setItem(PER_PROJECT, 'board');
}

async function signInThroughForm(user: ReturnType<typeof renderApp>['user'], who: typeof exampleOwner) {
  server.use(
    http.post('/v1/session', () => {
      signedInAs(who, { projects: [webProject] });
      return ok({ user: who, scopes: null });
    }),
  );
  await user.type(await screen.findByLabelText('Email'), who.email);
  await user.type(screen.getByLabelText('密碼'), 'example-password');
  await user.click(screen.getByRole('button', { name: '登入' }));
  expect(await screen.findByRole('heading', { level: 1, name: '我的工作' })).toBeInTheDocument();
}

describe('登入狀態與瀏覽器儲存', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it('一開始讀工作階段就是 401（瀏覽器重開、Cookie 過期）：不在保留清單上的偏好清掉，顯示偏好留著', async () => {
    // Arrange
    seedPreferences();
    signedOut();

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByLabelText('Email')).toBeInTheDocument();
    expect(localStorage.getItem(PER_PROJECT)).toBeNull();
    expect(localStorage.getItem(PREFERENCE)).toBe('dark');
  });

  it('一開始讀工作階段時斷線（5xx）：不算沒登入，不清', async () => {
    // Arrange
    seedPreferences();
    server.use(http.get('/v1/session', () => failure(503, 'server.not_ready', '服務正在啟動。')));

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    await flushPending();
    expect(localStorage.getItem(PER_PROJECT)).toBe('board');
  });

  it('一開始讀工作階段時網路錯誤：不清', async () => {
    // Arrange
    seedPreferences();
    server.use(http.get('/v1/session', () => Response.error()));

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    await flushPending();
    expect(localStorage.getItem(PER_PROJECT)).toBe('board');
  });

  it('登入的人和上一次不同：上一個人留下的偏好清掉', async () => {
    // Arrange
    seedPreferences();
    localStorage.setItem(LAST_PERSON, exampleOwner.personId);
    signedInAs(exampleMember, { projects: [webProject] });

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '我的工作' })).toBeInTheDocument();
    expect(localStorage.getItem(PER_PROJECT)).toBeNull();
    expect(localStorage.getItem(PREFERENCE)).toBe('dark');
    expect(localStorage.getItem(LAST_PERSON)).toBe(exampleMember.personId);
  });

  it('同一個人重新登入：不清非保留的偏好', async () => {
    // Arrange
    seedPreferences();
    localStorage.setItem(LAST_PERSON, exampleOwner.personId);
    signedInAs(exampleOwner, { projects: [webProject] });

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '我的工作' })).toBeInTheDocument();
    expect(localStorage.getItem(PER_PROJECT)).toBe('board');
  });

  it('從登入表單換另一個人登入：清掉上一個人的偏好', async () => {
    // Arrange
    localStorage.setItem(LAST_PERSON, exampleOwner.personId);
    signedOut();
    const { user } = renderApp('/login');
    // 登入頁一開始的 401 本身就會清；偏好在那之後才放進去，只有「換人」這條路能清掉它。
    await screen.findByLabelText('Email');
    seedPreferences();

    // Act
    await signInThroughForm(user, exampleMember);

    // Assert
    expect(localStorage.getItem(PER_PROJECT)).toBeNull();
    expect(localStorage.getItem(PREFERENCE)).toBe('dark');
    expect(localStorage.getItem(LAST_PERSON)).toBe(exampleMember.personId);
  });

  it('從登入表單用同一個人再登入：不清非保留的偏好', async () => {
    // Arrange
    localStorage.setItem(LAST_PERSON, exampleOwner.personId);
    signedOut();
    const { user } = renderApp('/login');
    await screen.findByLabelText('Email');
    seedPreferences();

    // Act
    await signInThroughForm(user, exampleOwner);

    // Assert
    expect(localStorage.getItem(PER_PROJECT)).toBe('board');
  });
});
