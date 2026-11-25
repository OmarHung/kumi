import { describe, expect, it } from 'vitest';
import { actualHostOf, claimedHostOf, hostMismatch, isPunycodeHost, linkHostNote } from './linkHosts';

describe('連結的實際網域', () => {
  it('http(s) 取主機名稱，國際化網域顯示 punycode', () => {
    expect(actualHostOf('https://Example.com/path?q=1')).toBe('example.com');
    expect(actualHostOf('https://bаnk.example/')).toBe('xn--bnk-6cd.example');
  });

  it('mailto 取位址本身', () => {
    expect(actualHostOf('mailto:Ann@Example.com?subject=hi')).toBe('ann@example.com');
  });

  it('其他協定與解析不了的網址是 null', () => {
    expect(actualHostOf('attachment:abc')).toBeNull();
    expect(actualHostOf('https://')).toBeNull();
  });
});

describe('文字宣稱的網域', () => {
  it('像網址或主機名稱的文字回它的主機', () => {
    expect(claimedHostOf(' https://www.bank.example/login ')).toBe('www.bank.example');
    expect(claimedHostOf('www.bank.example')).toBe('www.bank.example');
    expect(claimedHostOf('bank.example:8443/a')).toBe('bank.example');
  });

  it('同形字的主機換成 punycode，和真的網域看得出差別', () => {
    expect(claimedHostOf('bаnk.example')).toBe('xn--bnk-6cd.example');
  });

  it('像 Email 的文字回位址', () => {
    expect(claimedHostOf('Ann@Example.com')).toBe('ann@example.com');
  });

  it('一般的句子與詞不是', () => {
    expect(claimedHostOf('按這裡')).toBeNull();
    expect(claimedHostOf('v1.2')).toBeNull();
  });
});

describe('文字與實際網域不一致', () => {
  it('不同的主機回實際的主機', () => {
    expect(hostMismatch('www.bank.example', 'https://other.example')).toBe('other.example');
  });

  it('只差開頭的 www. 或路徑算一致', () => {
    expect(hostMismatch('www.example.com', 'https://example.com/a')).toBeNull();
    expect(hostMismatch('https://example.com/x', 'https://www.example.com/y')).toBeNull();
  });

  it('文字不像網址不提示', () => {
    expect(hostMismatch('按這裡', 'https://x.example')).toBeNull();
  });

  it('Email 文字連到不同的位址要提示，相同不提示', () => {
    expect(hostMismatch('ann@example.com', 'mailto:bob@other.example')).toBe('bob@other.example');
    expect(hostMismatch('ann@example.com', 'mailto:ann@example.com')).toBeNull();
  });

  it('主機名稱的文字連到信箱：網域相同才一致', () => {
    expect(hostMismatch('example.com', 'mailto:ann@example.com')).toBeNull();
    expect(hostMismatch('example.com', 'mailto:ann@other.example')).toBe('ann@other.example');
  });
});

describe('文字裡夾著不可見或全形的字元', () => {
  it.each([
    ['零寬空白', 'bank\u200b.example'],
    ['軟連字號', 'ba\u00adnk.example'],
    ['零寬連接字元', 'bank.exam\u200dple'],
  ])('%s：去掉之後照樣比對', (_name, text) => {
    expect(hostMismatch(text, 'https://other.example')).toBe('other.example');
    expect(hostMismatch(text, 'https://bank.example')).toBeNull();
  });

  it.each([
    ['全形的點', 'bank．example'],
    ['全形的句號', 'bank。example'],
    ['半形的句號', 'bank｡example'],
    ['全形的斜線與冒號', 'https：／／bank.example'],
  ])('%s：換成半形再比對', (_name, text) => {
    expect(hostMismatch(text, 'https://other.example')).toBe('other.example');
    expect(hostMismatch(text, 'https://bank.example')).toBeNull();
  });
});

describe('文字是一句話、裡面夾著網域', () => {
  it.each(['visit bank.example', 'bank.example – secure login', '請到 https://bank.example/login 登入', '(bank.example)'])('「%s」連到別的主機要提示', (text) => {
    expect(hostMismatch(text, 'https://other.example')).toBe('other.example');
  });

  it('同一句裡的網域就是實際的主機：不提示', () => {
    expect(hostMismatch('visit bank.example', 'https://bank.example/a')).toBeNull();
    expect(hostMismatch('bank.example – secure login', 'https://www.bank.example')).toBeNull();
  });

  it('一句話裡有好幾個網域，只要有一個和實際的不同就提示', () => {
    expect(hostMismatch('bank.example or other.example', 'https://other.example')).toBe('other.example');
  });

  it('沒有網域的句子、版本號、縮寫不提示', () => {
    expect(hostMismatch('按這裡看更多', 'https://other.example')).toBeNull();
    expect(hostMismatch('see v1.2 notes, e.g. below', 'https://other.example')).toBeNull();
  });
});

describe('IP、localhost、別的協定與帳密寫法', () => {
  it('IPv4 與 localhost 的文字連到別的主機要提示，相同不提示', () => {
    expect(hostMismatch('203.0.113.7', 'https://other.example')).toBe('other.example');
    expect(hostMismatch('203.0.113.7', 'http://203.0.113.7/a')).toBeNull();
    expect(hostMismatch('localhost:3000', 'https://other.example')).toBe('other.example');
    expect(hostMismatch('localhost:3000', 'http://localhost:3000/')).toBeNull();
  });

  it('別的協定開頭的文字也比對主機', () => {
    expect(hostMismatch('ftp://bank.example', 'https://other.example')).toBe('other.example');
    expect(hostMismatch('ftp://bank.example/file', 'https://bank.example')).toBeNull();
  });

  it('文字是「帳號@主機」的網址寫法：一律提示實際的主機，連主機一樣也是', () => {
    expect(hostMismatch('https://bank.example@other.example', 'https://other.example')).toBe('other.example');
    expect(hostMismatch('https://bank.example@other.example', 'https://bank.example')).toBe('bank.example');
    // 分開寫：連在一起的「帳號:密碼@」會被提交前的掃描當成真的帳密。
    const withPassword = ['https://bank.example', 'pw@other.example/x'].join(':');
    expect(hostMismatch(withPassword, 'https://other.example')).toBe('other.example');
  });
});

describe('同形字網域（punycode）', () => {
  it('實際的主機含 xn-- 一律標出來，文字一不一致都一樣', () => {
    expect(linkHostNote('bаnk.example', 'https://bаnk.example/')).toEqual({ kind: 'punycode', host: 'xn--bnk-6cd.example' });
    expect(linkHostNote('文件', 'https://bаnk.example/')).toEqual({ kind: 'punycode', host: 'xn--bnk-6cd.example' });
  });

  it('文字與實際的不一致時是不一致的提示，並註明含非英文字母', () => {
    expect(linkHostNote('bank.example', 'https://bаnk.example/')).toEqual({ kind: 'mismatch', host: 'xn--bnk-6cd.example', isPunycode: true });
  });

  it('一般的網域沒有標記', () => {
    expect(linkHostNote('bank.example', 'https://bank.example')).toBeNull();
    expect(isPunycodeHost('bank.example')).toBe(false);
    expect(isPunycodeHost('ann@xn--bnk-6cd.example')).toBe(true);
  });
});

describe('像檔名或套件名稱的文字', () => {
  it.each(['README.md', 'Node.js', 'file.pdf', 'package.json', 'next.config.ts'])('整段文字是「%s」不當成網域', (text) => {
    expect(hostMismatch(text, 'https://other.example/file')).toBeNull();
  });

  it('同樣結尾但實際存在的頂級網域（沒有大寫的檔名寫法）照樣比對', () => {
    expect(hostMismatch('bank.md', 'https://other.example')).toBe('other.example');
    expect(hostMismatch('bank.sh', 'https://other.example')).toBe('other.example');
  });

  it('句子裡的檔名不豁免：不是「整段文字就是它」', () => {
    expect(hostMismatch('open file.pdf now', 'https://other.example')).toBe('other.example');
  });

  it('實際的主機結尾有點時比對前去掉', () => {
    expect(actualHostOf('https://bank.example./x')).toBe('bank.example');
    expect(hostMismatch('bank.example', 'https://bank.example./x')).toBeNull();
  });
});

describe('中文的句子裡沒有空白就接著網域', () => {
  it.each(['請到bank.example登入', '到bank.example', '（bank.example）登入', '登入「bank.example」', '請到https://bank.example/login登入'])(
    '「%s」連到別的主機要提示',
    (text) => {
      expect(hostMismatch(text, 'https://other.example/')).toBe('other.example');
    },
  );

  it.each(['請到bank.example登入', '（bank.example）登入'])('「%s」連到同一個主機不提示', (text) => {
    expect(hostMismatch(text, 'https://bank.example/')).toBeNull();
  });

  it('整段是中文的國際化網域：照舊當成一個主機比對', () => {
    expect(hostMismatch('銀行.example', 'https://other.example/')).toBe('other.example');
    expect(hostMismatch('點這裡登入', 'https://other.example/')).toBeNull();
  });
});
