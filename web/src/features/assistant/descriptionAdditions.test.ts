import { render, waitFor } from '@testing-library/react';
import { createElement } from 'react';
import { describe, expect, it } from 'vitest';
import { Markdown } from '../../components/Markdown/Markdown';
import { exampleMember } from '../../test/fixtures';
import { descriptionAdditions, linkTargets } from './descriptionAdditions';

const MEMBERS = [{ personId: exampleMember.personId, displayName: exampleMember.displayName, role: 'editor' as const }];
const DASHED_MEMBERS = [{ personId: 'person-1', displayName: '有橫線的人', role: 'editor' as const }];

describe('說明新增的連結與提及', () => {
  it('連結顯示實際的網域，不是連結上寫的字', () => {
    // Act
    const text = descriptionAdditions('', '[example.org](https://login.example.net/path)', MEMBERS);

    // Assert
    expect(text).toBe('這份說明會新增 1 個連結：login.example.net。');
  });

  it('同一個網域的多個連結只列一次網域，數量照連結算', () => {
    // Act
    const text = descriptionAdditions('', 'https://example.com/a 和 https://example.com/b', MEMBERS);

    // Assert
    expect(text).toBe('這份說明會新增 2 個連結：example.com。');
  });

  it('不是成員的提及寫「不是成員的人」，不採用說明裡自己寫的名字', () => {
    // Act
    const text = descriptionAdditions('', '@[某某](mention:not-a-member)', MEMBERS);

    // Assert
    expect(text).toBe('這份說明會提及 1 個人：不是成員的人。');
  });

  it('現在就有的連結與提及、附件與沒有連結的文字都不算', () => {
    // Arrange
    const before = `見 https://example.com/a @[範例成員](mention:${exampleMember.personId})`;

    // Act
    const text = descriptionAdditions(before, `${before} ![圖](attachment:1) 一般文字`, MEMBERS);

    // Assert
    expect(text).toBeNull();
  });
});

describe('說明新增的連結：和畫面上畫出來的連結同一套解析', () => {
  it.each([
    ['www. 開頭的自動連結', '請看 www.example.com/docs', '這份說明會新增 1 個連結：www.example.com。'],
    ['沒有 // 的網址（瀏覽器會補成完整網址）', '[字](https:example.com)', '這份說明會新增 1 個連結：example.com。'],
    ['網址後面緊接全形逗號', '請到 https://example.com，然後登入', '這份說明會新增 1 個連結：example.com。'],
    ['網址後面緊接全形句號', '登入頁在 https://example.com。', '這份說明會新增 1 個連結：example.com。'],
    ['網址後面緊接頓號（頓號之後照畫面是一般文字）', '見 https://example.com、https://other.example', '這份說明會新增 1 個連結：example.com。'],
    ['連結裡面的外部圖片只畫文字，不另外算', '[字![圖](https://img.example.com/a.png)](https://wrap.example.com)', '這份說明會新增 1 個連結：wrap.example.com。'],
    ['帶埠的網址顯示含埠的主機', '[後台](https://example.com:8443/admin)', '這份說明會新增 1 個連結：example.com:8443。'],
    ['參考式連結', '[字][1]\n\n[1]: https://ref.example.com/x', '這份說明會新增 1 個連結：ref.example.com。'],
    ['角括號包起來的網址', '<https://angle.example.com/a>', '這份說明會新增 1 個連結：angle.example.com。'],
    ['外部圖片（畫成連結）', '![圖](https://img.example.com/a.png)', '這份說明會新增 1 個連結：img.example.com。'],
  ])('%s', (_name, after, expected) => {
    // Act
    const text = descriptionAdditions('', after, MEMBERS);

    // Assert
    expect(text).toBe(expected);
  });

  it.each([
    ['裸的 email', '請寄 someone@example.com'],
    ['mailto 連結', '[寄信](mailto:someone@example.com?subject=hi)'],
  ])('%s列成信箱連結，顯示信箱的網域', (_name, after) => {
    // Act
    const text = descriptionAdditions('', after, MEMBERS);

    // Assert
    expect(text).toBe('這份說明會新增 1 個信箱連結：example.com。');
  });

  it.each([
    ['程式碼區塊', '```\nhttps://example.com/a\n```'],
    ['行內程式碼', '用 `https://example.com/a` 開'],
    ['附件', '[檔案](attachment:2f1c1c9e-0000-4000-8000-000000000001)'],
    ['顯示端不認的協定', '[字](javascript:alert(1))'],
    ['沒有定義的參考式連結', '[字][nothing]'],
    ['相對路徑的連結', '[字](#x)'],
  ])('%s不算連結', (_name, after) => {
    // Act
    const text = descriptionAdditions('', after, MEMBERS);

    // Assert
    expect(text).toBeNull();
  });

  it('沒有 @ 的 [字](mention:編號) 顯示端也畫成提及，編號用同一個函式還原（%2D 是 -）', () => {
    // Act
    const text = descriptionAdditions('', '請 [隨便](mention:person%2D1) 看', DASHED_MEMBERS);

    // Assert
    expect(text).toBe('這份說明會提及 1 個人：有橫線的人。');
  });

  it('編號大小寫不同的不是同一個人（顯示端也是照原樣比對）', () => {
    // Act
    const text = descriptionAdditions('', '[x](mention:PERSON-1)', DASHED_MEMBERS);

    // Assert
    expect(text).toBe('這份說明會提及 1 個人：不是成員的人。');
  });

  it('同時有連結、信箱與提及：用分號接起來', () => {
    // Act
    const text = descriptionAdditions('', 'https://example.com a@other.example @[甲](mention:person-1)', DASHED_MEMBERS);

    // Assert
    expect(text).toBe('這份說明會新增 1 個連結：example.com；新增 1 個信箱連結：other.example；提及 1 個人：有橫線的人。');
  });
});

describe('比較的基準讀不到、成員還沒載入', () => {
  it('讀不到現在的說明：列出建議裡全部的連結與提及，不說「新增」', () => {
    // Act
    const text = descriptionAdditions(undefined, 'https://example.com/a 與 [甲](mention:person-1)', DASHED_MEMBERS);

    // Assert
    expect(text).toBe('這份說明裡有 1 個連結：example.com；提及 1 個人：有橫線的人。');
  });

  it('讀不到現在的說明時，信箱也照列', () => {
    // Act
    const text = descriptionAdditions(undefined, 'a@example.com', MEMBERS);

    // Assert
    expect(text).toBe('這份說明裡有 1 個信箱連結：example.com。');
  });

  it('成員還沒載入：只說提及幾個人，不把人說成「不是成員的人」', () => {
    // Act
    const text = descriptionAdditions('', '[甲](mention:person-1)', undefined);

    // Assert
    expect(text).toBe('這份說明會提及 1 個人。');
  });
});

const TRICKY_INPUTS = [
  '請到 https://example.com，然後登入。也看 www.example.org/a、https://example.net:8443/x。',
  '[字](https:example.com) [信](mailto:a@example.com) b@example.org',
  '[字][1] 與 [另一個][2]\n\n[1]: https://ref.example.com/x\n[2]: javascript:alert(1)',
  '<https://angle.example.com/a> ![圖](https://img.example.com/a.png) [![圖](https://img.example.com/b.png)](https://wrap.example.com)',
  '```\nhttps://code.example.com\n```\n`https://inline.example.com` 和 https://real.example.com',
  '[字](https://example.com/路徑/中文?q=甲乙) <b>https://html.example.com</b>',
  '| 欄 |\n| --- |\n| https://table.example.com/a |\n\n- https://list.example.com\n> https://quote.example.com',
  '[x](mention:abc) @[y](mention:def) [z](attachment:2f1c1c9e-0000-4000-8000-000000000001)',
  '~~https://struck.example.com~~ **https://bold.example.com/a_b_c** https://paren.example.com/(a)',
];

describe('摘要列的連結和畫面上畫出來的一致', () => {
  it.each(TRICKY_INPUTS.map((input) => [input]))('%s', async (input) => {
    // Arrange
    const { container } = render(createElement(Markdown, { text: input, mentionNames: new Map(), hasAttachments: false }));
    await waitFor(() => {
      expect(container.querySelector('[data-state="rendered"]')).not.toBeNull();
    });

    // Act
    const shown = new Set([...container.querySelectorAll('a')].map((anchor) => anchor.href));
    const { links, mailboxes } = linkTargets(input);

    // Assert
    expect(new Set([...links.keys(), ...mailboxes.keys()])).toEqual(shown);
  });
});
