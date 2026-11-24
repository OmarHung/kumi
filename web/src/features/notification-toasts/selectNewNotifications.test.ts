import { describe, expect, it } from 'vitest';
import { notification } from '../../test/notifications';
import { seedFrom, selectNewNotifications } from './selectNewNotifications';

const at = (minute: number) => `2026-11-22T08:${String(minute).padStart(2, '0')}:00Z`;

describe('挑出新的通知', () => {
  it('開始時讀到的未讀都是已經知道的，不跳', () => {
    // Arrange
    const existing = [notification({ createdAt: at(5) }), notification({ createdAt: at(4) })];
    const seed = seedFrom(existing);

    // Act
    const { fresh } = selectNewNotifications(seed, existing);

    // Assert
    expect(fresh).toEqual([]);
  });

  it('編號沒看過、而且比已知的最新一則新的才跳；舊的先進（疊起來新的在下面）', () => {
    // Arrange
    const known = notification({ createdAt: at(5) });
    const older = notification({ createdAt: at(6) });
    const newer = notification({ createdAt: at(7) });
    const seed = seedFrom([known]);

    // Act
    const { fresh } = selectNewNotifications(seed, [newer, older, known]);

    // Assert
    expect(fresh.map((entry) => entry.notificationId)).toEqual([older.notificationId, newer.notificationId]);
  });

  it('跳過的記下來，同一則不會跳第二次', () => {
    // Arrange
    const known = notification({ createdAt: at(5) });
    const incoming = notification({ createdAt: at(6) });
    const first = selectNewNotifications(seedFrom([known]), [incoming, known]);

    // Act
    const second = selectNewNotifications(first.seed, [incoming, known]);

    // Assert
    expect(first.fresh).toHaveLength(1);
    expect(second.fresh).toEqual([]);
  });

  it('標成已讀讓更舊的未讀浮出來時不跳：編號沒看過、但不比已知的最新一則新', () => {
    // Arrange
    const top = notification({ createdAt: at(9) });
    const surfaced = notification({ createdAt: at(1) });
    const seed = seedFrom([top]);

    // Act
    const { fresh } = selectNewNotifications(seed, [surfaced]);

    // Assert
    expect(fresh).toEqual([]);
  });

  it('開始時沒有任何未讀：只看編號，第一則新的就跳', () => {
    // Arrange
    const seed = seedFrom([]);
    const incoming = notification({ createdAt: at(1) });

    // Act
    const { fresh, seed: next } = selectNewNotifications(seed, [incoming]);

    // Assert
    expect(seed.latestCreatedAt).toBeNull();
    expect(fresh).toEqual([incoming]);
    expect(next.latestCreatedAt).toBe(Date.parse(at(1)));
  });

  it('同一毫秒的兩則都要跳', () => {
    // Arrange
    const known = notification({ createdAt: '2026-11-22T08:00:00.123Z' });
    const twin = notification({ createdAt: '2026-11-22T08:00:00.123Z' });

    // Act
    const { fresh } = selectNewNotifications(seedFrom([known]), [twin, known]);

    // Assert
    expect(fresh).toEqual([twin]);
  });

  it('兩個並行的交易先後顛倒：晚到的那一則時間早幾秒也要跳', () => {
    // Arrange
    const known = notification({ createdAt: at(5) });
    const lateArrival = notification({ createdAt: '2026-11-22T08:04:57Z' });

    // Act
    const { fresh } = selectNewNotifications(seedFrom([known]), [lateArrival, known]);

    // Assert
    expect(fresh).toEqual([lateArrival]);
  });

  it('早了超過 5 秒的（標成已讀後浮上來的舊通知）不跳', () => {
    // Arrange
    const known = notification({ createdAt: at(5) });
    const old = notification({ createdAt: '2026-11-22T08:04:54Z' });

    // Act
    const { fresh } = selectNewNotifications(seedFrom([known]), [old, known]);

    // Assert
    expect(fresh).toEqual([]);
  });

  it('比的是建立時間的先後，不是字串（有沒有小數秒都一樣）', () => {
    // Arrange
    const known = notification({ createdAt: '2026-11-22T08:00:10.5Z' });
    const later = notification({ createdAt: '2026-11-22T08:00:11Z' });
    const older = notification({ createdAt: '2026-11-22T08:00:00Z' });

    // Act
    const { fresh } = selectNewNotifications(seedFrom([known]), [later, older]);

    // Assert
    expect(fresh).toEqual([later]);
  });
});
