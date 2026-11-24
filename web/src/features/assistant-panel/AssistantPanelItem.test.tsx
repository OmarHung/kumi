import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { ok, signedInAs } from '../../test/api';
import {
  completeReply,
  makeAiOptions,
  makeConversationSummary,
  makeMessage,
  serveAiOptions,
  serveConversations,
  serveCreateConversation,
  serveReply,
} from '../../test/assistant';
import { agyDetail, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const NEW = makeConversationSummary({ id: 'conv-new' });
const LOGIN = smkItem({ number: 12, title: '修正登入頁' });
const HOME = smkItem({ number: 7, title: '首頁改版' });

/** 項目頁或清單旁邊的面板：兩個項目可讀，搜尋依 q 篩標題或編號。 */
function openApp(path: string) {
  signedInAs(exampleOwner, { details: [smkDetail(), agyDetail()] });
  serveAiOptions(makeAiOptions());
  serveConversations('SMK', []);
  serveConversations('AGY', []);
  serveItem(detailOf(LOGIN));
  server.use(
    http.get('/v1/items/SMK-7', () => ok(detailOf(HOME))),
    http.get('/v1/projects/SMK/items', ({ request }) => {
      const q = new URL(request.url).searchParams.get('q') ?? '';
      const items = [LOGIN, HOME].filter((item) => q === '' || item.title.includes(q) || item.key === q);
      return ok({ items, nextCursor: null });
    }),
  );
  return renderApp(path);
}

async function openPanel(user: ReturnType<typeof renderApp>['user']) {
  await user.click(await screen.findByRole('button', { name: 'AI 助理' }));
  return screen.findByRole('dialog', { name: 'AI 助理' });
}

async function openChoices(user: ReturnType<typeof renderApp>['user'], panel: HTMLElement, buttonName: RegExp) {
  await user.click(await within(panel).findByRole('button', { name: buttonName }));
  return screen.findByRole('combobox', { name: '搜尋項目' });
}

describe('選項目：自動帶', () => {
  it('在項目頁：自動選那個項目，提示字與例子換掉；送出帶 itemKey，訊息上標「關於 SMK-12」連到項目', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好', makeMessage({ id: 'u1', itemKey: 'SMK-12', parts: [{ type: 'text', text: '整理重點' }] }));
    });
    const { user } = openApp('/projects/SMK/items/SMK-12');

    // Act
    const panel = await openPanel(user);

    // Assert
    expect(await within(panel).findByRole('button', { name: '項目：SMK-12' })).toHaveTextContent('修正登入頁');
    const input = await within(panel).findByRole('textbox', { name: '訊息' });
    expect(input).toHaveAttribute('placeholder', '問這個項目的事，或請助理建議怎麼改…');
    expect(within(panel).getByRole('button', { name: '整理這個項目的重點' })).toBeInTheDocument();
    expect(within(panel).getByText(/你按「套用」才會改/)).toBeInTheDocument();

    // Act
    await user.click(input);
    await user.type(input, '整理重點{Enter}');

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    expect(reply.sent[0]?.body.itemKey).toBe('SMK-12');
    const label = await within(panel).findByRole('link', { name: '關於 SMK-12' });
    expect(label).toHaveAttribute('href', '/projects/SMK/items/SMK-12');
  });

  it('清單旁邊開著的側邊面板（?item=）也自動帶', async () => {
    // Arrange / Act
    const { user } = openApp('/projects/SMK?item=SMK-7');
    const panel = await openPanel(user);

    // Assert
    expect(await within(panel).findByRole('button', { name: '項目：SMK-7' })).toBeInTheDocument();
  });

  it('沒有開著項目：不指定，送出帶 null', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '好');
    });
    const { user } = openApp('/projects/SMK');
    const panel = await openPanel(user);
    expect(await within(panel).findByRole('button', { name: '項目：不指定' })).toBeInTheDocument();
    const input = await within(panel).findByRole('textbox', { name: '訊息' });

    // Act
    await user.type(input, '你好{Enter}');

    // Assert
    await waitFor(() => {
      expect(reply.sent[0]?.body.itemKey).toBeNull();
    });
  });

  it('路由的項目換了就跟著換；回到沒有項目的頁面就放掉', async () => {
    // Arrange
    const { user, router } = openApp('/projects/SMK/items/SMK-12');
    const panel = await openPanel(user);
    await within(panel).findByRole('button', { name: '項目：SMK-12' });

    // Act
    await act(async () => {
      await router.navigate('/projects/SMK/items/SMK-7');
    });

    // Assert
    expect(await within(panel).findByRole('button', { name: '項目：SMK-7' })).toBeInTheDocument();

    // Act
    await act(async () => {
      await router.navigate('/projects/SMK');
    });

    // Assert
    expect(await within(panel).findByRole('button', { name: '項目：不指定' })).toBeInTheDocument();
  });
});

describe('選項目：搜尋換、取消', () => {
  it('打字搜尋這個專案的項目（編號或標題），選了就換、焦點回到按鈕', async () => {
    // Arrange
    const { user } = openApp('/projects/SMK');
    const panel = await openPanel(user);
    const search = await openChoices(user, panel, /項目：不指定/);
    await screen.findByRole('option', { name: /首頁改版/ });

    // Act
    await user.type(search, '首頁');

    // Assert：搜尋停下來才送，結果只剩符合的
    await waitFor(() => {
      expect(screen.queryByRole('option', { name: /修正登入頁/ })).not.toBeInTheDocument();
    });
    await user.click(screen.getByRole('option', { name: /首頁改版/ }));
    const trigger = await within(panel).findByRole('button', { name: '項目：SMK-7' });
    expect(trigger).toHaveFocus();
    expect(screen.queryByRole('listbox', { name: '項目' })).not.toBeInTheDocument();
  });

  it('鍵盤：方向鍵移動、Enter 選', async () => {
    // Arrange
    const { user } = openApp('/projects/SMK');
    const panel = await openPanel(user);
    await openChoices(user, panel, /項目：不指定/);
    await screen.findByRole('option', { name: /修正登入頁/ });

    // Act：第一個是「不指定項目」，往下兩次是第二個項目
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}');

    // Assert
    expect(await within(panel).findByRole('button', { name: '項目：SMK-7' })).toBeInTheDocument();
  });

  it('一打開，選起來的是目前的項目；打了字，Enter 選第一個結果', async () => {
    // Arrange
    const { user } = openApp('/projects/SMK/items/SMK-12');
    const panel = await openPanel(user);
    const search = await openChoices(user, panel, /項目：SMK-12/);

    // Assert
    expect(await screen.findByRole('option', { name: /修正登入頁/ })).toHaveAttribute('aria-selected', 'true');

    // Act
    await user.type(search, '首頁');
    await waitFor(() => {
      expect(screen.queryByRole('option', { name: /修正登入頁/ })).not.toBeInTheDocument();
    });
    await user.keyboard('{Enter}');

    // Assert
    expect(await within(panel).findByRole('button', { name: '項目：SMK-7' })).toBeInTheDocument();
  });

  it('取消：選「不指定項目」；路由的項目沒換就不再帶回來，換了才再跟', async () => {
    // Arrange
    const { user, router } = openApp('/projects/SMK/items/SMK-12');
    const panel = await openPanel(user);
    await openChoices(user, panel, /項目：SMK-12/);

    // Act
    await user.click(await screen.findByRole('option', { name: '不指定項目' }));

    // Assert
    expect(await within(panel).findByRole('button', { name: '項目：不指定' })).toBeInTheDocument();
    await act(async () => {
      await router.navigate('/projects/SMK/items/SMK-12?tab=1');
    });
    expect(within(panel).getByRole('button', { name: '項目：不指定' })).toBeInTheDocument();
    expect(currentPath(router)).toContain('SMK-12');

    // Act：路由換到別的項目
    await act(async () => {
      await router.navigate('/projects/SMK/items/SMK-7');
    });

    // Assert
    expect(await within(panel).findByRole('button', { name: '項目：SMK-7' })).toBeInTheDocument();
  });
});

describe('選項目：Enter 的防呆', () => {
  it('打字後立刻 Enter（搜尋還沒送、結果還是上一份）：什麼都不選；結果出來之後 Enter 選第一個結果', async () => {
    // Arrange
    const { user } = openApp('/projects/SMK/items/SMK-12');
    const panel = await openPanel(user);
    const search = await openChoices(user, panel, /項目：SMK-12/);
    await screen.findByRole('option', { name: /修正登入頁/ });

    // Act
    await user.type(search, '首頁{Enter}');

    // Assert
    expect(within(panel).getByRole('button', { name: '項目：SMK-12' })).toBeInTheDocument();
    expect(screen.getByRole('listbox', { name: '項目' })).toBeInTheDocument();
    expect(screen.getByText('正在搜尋…')).toBeInTheDocument();

    // Act：結果是這個字的了
    await waitFor(() => {
      expect(screen.queryByRole('option', { name: /修正登入頁/ })).not.toBeInTheDocument();
    });
    await user.keyboard('{Enter}');

    // Assert
    expect(await within(panel).findByRole('button', { name: '項目：SMK-7' })).toBeInTheDocument();
  });

  it('搜尋還在載入（回應沒回來）時 Enter 不做事', async () => {
    // Arrange
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { user } = openApp('/projects/SMK/items/SMK-12');
    server.use(
      http.get('/v1/projects/SMK/items', async ({ request }) => {
        const q = new URL(request.url).searchParams.get('q') ?? '';
        if (q !== '') await gate;
        return ok({ items: q === '' ? [LOGIN, HOME] : [HOME], nextCursor: null });
      }),
    );
    const panel = await openPanel(user);
    const search = await openChoices(user, panel, /項目：SMK-12/);
    await screen.findByRole('option', { name: /首頁改版/ });

    // Act：搜尋已經送出（超過 300ms）但還沒回來
    await user.type(search, '首頁');
    await new Promise((resolve) => setTimeout(resolve, 450));
    await user.keyboard('{Enter}');

    // Assert
    expect(within(panel).getByRole('button', { name: '項目：SMK-12' })).toBeInTheDocument();
    expect(screen.getByText('正在搜尋…')).toBeInTheDocument();
    release();
    await waitFor(() => {
      expect(screen.queryByText('正在搜尋…')).not.toBeInTheDocument();
    });
  });

  it('有打字但 0 筆：Enter 不做事，目前的項目不會被清掉', async () => {
    // Arrange
    const { user } = openApp('/projects/SMK/items/SMK-12');
    const panel = await openPanel(user);
    const search = await openChoices(user, panel, /項目：SMK-12/);

    // Act
    await user.type(search, '找不到的字');
    expect(await screen.findByText('找不到符合的項目。')).toBeInTheDocument();
    await user.keyboard('{Enter}');

    // Assert
    expect(within(panel).getByRole('button', { name: '項目：SMK-12' })).toBeInTheDocument();
    expect(screen.getByRole('listbox', { name: '項目' })).toBeInTheDocument();
  });

  it('輸入法組字中按 Esc：不關掉浮層', async () => {
    // Arrange
    const { user } = openApp('/projects/SMK');
    const panel = await openPanel(user);
    const search = await openChoices(user, panel, /項目：不指定/);

    // Act
    fireEvent.keyDown(search, { key: 'Escape', isComposing: true });

    // Assert
    expect(screen.getByRole('listbox', { name: '項目' })).toBeInTheDocument();
  });
});

describe('選項目：目前的項目固定在最上面', () => {
  it('不在搜尋結果前幾筆時照樣列在「不指定項目」下面，打勾並標「（目前）」；開成整頁帶著項目', async () => {
    // Arrange
    const { user } = openApp('/projects/SMK/items/SMK-12');
    server.use(http.get('/v1/projects/SMK/items', () => ok({ items: [HOME], nextCursor: null })));
    const panel = await openPanel(user);

    // Act
    await openChoices(user, panel, /項目：SMK-12/);

    // Assert
    const options = await screen.findAllByRole('option');
    await waitFor(() => {
      expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual([
        '不指定項目',
        'SMK-12 修正登入頁（目前）',
        'SMK-7 首頁改版',
      ]);
    });
    expect(options.length).toBeGreaterThan(0);
    await user.keyboard('{Escape}');
    expect(within(panel).getByRole('link', { name: '開成整頁' })).toHaveAttribute('href', '/projects/SMK/assistant?item=SMK-12');
  });
});

