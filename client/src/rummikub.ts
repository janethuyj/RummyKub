// Client-side mirror of the engine's RuleValidator + SetFinder (server remains
// authoritative on commit). Used by auto-organize to detect valid runs/groups in
// the rack and cluster them, and to keep the rack organized after new draws.
import type { Tile } from './types';

const COLOR_ORDER: Record<string, number> = { red: 0, orange: 1, blue: 2, black: 3 };

/** True if the tiles form a valid group or run (jokers substitute). */
export function isValidSet(tiles: Tile[]): boolean {
  if (tiles.length < 3) return false;
  return isGroup(tiles) || isRun(tiles);
}

function isGroup(tiles: Tile[]): boolean {
  if (tiles.length < 3 || tiles.length > 4) return false;
  const nonJokers = tiles.filter((t) => !t.isJoker);
  const jokers = tiles.length - nonJokers.length;
  if (nonJokers.length === 0) return true;
  const number = nonJokers[0].number;
  if (nonJokers.some((t) => t.number !== number)) return false;
  const colors = new Set(nonJokers.map((t) => t.color));
  if (colors.size !== nonJokers.length) return false;
  return jokers <= 4 - nonJokers.length;
}

// Positional run check (mirrors the engine): each tile's number is fixed by its
// position and a joker takes the implied number, which must stay within 1..13 —
// so a joker after a 13 (a 14) or before a 1 (a 0) is rejected.
function isRun(tiles: Tile[]): boolean {
  if (tiles.length < 3) return false;

  let color: Tile['color'] = null;
  let base: number | null = null;
  for (let i = 0; i < tiles.length; i++) {
    if (tiles[i].isJoker) continue;
    color = tiles[i].color;
    base = tiles[i].number - i;
    break;
  }
  if (base === null) return tiles.length <= 13; // all jokers

  const start = base;
  const end = start + tiles.length - 1;
  if (start < 1 || end > 13) return false;

  for (let i = 0; i < tiles.length; i++) {
    const t = tiles[i];
    if (t.isJoker) continue;
    if (t.color !== color || t.number !== start + i) return false;
  }
  return true;
}

/** Greedily pull disjoint valid sets out of the tiles, spending jokers last. */
export function findSets(tiles: Tile[]): { sets: Tile[][]; leftovers: Tile[] } {
  let remaining = tiles.filter((t) => !t.isJoker);
  const jokers = tiles.filter((t) => t.isJoker);
  const sets: Tile[][] = [];

  for (;;) {
    const best = findLargestSet(remaining);
    if (!best || best.length < 3) break;
    const ids = new Set(best.map((t) => t.id));
    remaining = remaining.filter((t) => !ids.has(t.id));
    sets.push(best);
  }

  for (const joker of jokers) {
    if (extendWithJoker(sets, joker)) continue;
    if (completeWithJoker(remaining, joker, sets)) continue;
    remaining.push(joker);
  }

  return { sets, leftovers: remaining };
}

export interface OrganizedRack {
  ordered: Tile[];
  /** Rack indices at which to draw a gap (after each set, hence before the loose tiles). */
  gaps: number[];
}

/** Auto-organize: valid sets first (grouped), then loose tiles sorted, jokers last. */
export function organizeRack(rack: Tile[]): OrganizedRack {
  const { sets, leftovers } = findSets(rack);
  const loose = [...leftovers].sort(byColorThenNumber);
  const ordered = [...sets.flat(), ...loose];

  const gaps: number[] = [];
  let idx = 0;
  for (const s of sets) {
    idx += s.length;
    gaps.push(idx); // gap after this set
  }
  return { ordered, gaps };
}

function byColorThenNumber(a: Tile, b: Tile): number {
  if (a.isJoker !== b.isJoker) return a.isJoker ? 1 : -1;
  if (a.isJoker) return 0;
  const ca = COLOR_ORDER[a.color ?? ''] ?? 9;
  const cb = COLOR_ORDER[b.color ?? ''] ?? 9;
  return ca !== cb ? ca - cb : a.number - b.number;
}

function findLargestSet(tiles: Tile[]): Tile[] | null {
  let best: Tile[] | null = null;

  // Runs: per colour, longest run of consecutive numbers (one tile per number).
  for (const group of groupBy(tiles, (t) => t.color ?? '').values()) {
    const byNumber = new Map<number, Tile>();
    for (const t of group) if (!byNumber.has(t.number)) byNumber.set(t.number, t);
    const numbers = [...byNumber.keys()].sort((a, b) => a - b);
    let i = 0;
    while (i < numbers.length) {
      let j = i;
      while (j + 1 < numbers.length && numbers[j + 1] === numbers[j] + 1) j++;
      if (j - i + 1 >= 3) {
        const run = numbers.slice(i, j + 1).map((n) => byNumber.get(n)!);
        if (!best || run.length > best.length) best = run;
      }
      i = j + 1;
    }
  }

  // Groups: per number, distinct colours (3 or 4).
  for (const group of groupBy(tiles, (t) => t.number).values()) {
    const distinct = new Map<string, Tile>();
    for (const t of group) if (!distinct.has(t.color ?? '')) distinct.set(t.color ?? '', t);
    const tilesOut = [...distinct.values()];
    if (tilesOut.length >= 3 && (!best || tilesOut.length > best.length)) best = tilesOut;
  }

  return best;
}

function extendWithJoker(sets: Tile[][], joker: Tile): boolean {
  // Insert the joker where it keeps the set valid (runs are order-sensitive).
  for (const set of sets) {
    for (let pos = 0; pos <= set.length; pos++) {
      const candidate = [...set.slice(0, pos), joker, ...set.slice(pos)];
      if (isValidSet(candidate)) {
        set.length = 0;
        set.push(...candidate);
        return true;
      }
    }
  }
  return false;
}

function completeWithJoker(remaining: Tile[], joker: Tile, sets: Tile[][]): boolean {
  for (let a = 0; a < remaining.length; a++) {
    for (let b = a + 1; b < remaining.length; b++) {
      const pairs = [
        [remaining[a], remaining[b]],
        [remaining[b], remaining[a]],
      ];
      for (const pair of pairs) {
        for (let pos = 0; pos <= 2; pos++) {
          const candidate = [...pair.slice(0, pos), joker, ...pair.slice(pos)];
          if (isValidSet(candidate)) {
            remaining.splice(b, 1);
            remaining.splice(a, 1);
            sets.push(candidate);
            return true;
          }
        }
      }
    }
  }
  return false;
}

function groupBy<K>(tiles: Tile[], key: (t: Tile) => K): Map<K, Tile[]> {
  const map = new Map<K, Tile[]>();
  for (const t of tiles) {
    const k = key(t);
    const arr = map.get(k);
    if (arr) arr.push(t);
    else map.set(k, [t]);
  }
  return map;
}
