// Board grid model. The board is a grid of cells; tiles snap into cells and a run
// of contiguous filled cells in a row is one set (a gap ends a set). Auto-placement
// follows a fixed convention — groups left-aligned, runs on a number axis with 13 in
// the last column — but positions the player chooses are preserved across updates.
import type { Tile } from './types';

// 21 columns = a full 13-tile run plus two 4-tile groups (13+4+4). With the run
// number axis, column = RUN_ANCHOR + number, so number 13 lands in the last column.
export const BOARD_COLS = 21;
export const BOARD_ROWS = 8;
const RUN_ANCHOR = BOARD_COLS - 1 - 13; // 7 -> number 1 at col 8, number 13 at col 20

export type Cell = Tile | null;
export type Grid = Cell[][];

export function emptyGrid(rows = BOARD_ROWS): Grid {
  return Array.from({ length: rows }, () => Array<Cell>(BOARD_COLS).fill(null));
}

export function cloneGrid(grid: Grid): Grid {
  return grid.map((row) => row.slice());
}

/** Read the grid back into sets: each maximal run of filled cells in a row is a set. */
export function gridToSets(grid: Grid): Tile[][] {
  const sets: Tile[][] = [];
  for (const row of grid) {
    let segment: Tile[] = [];
    for (const cell of row) {
      if (cell) {
        segment.push(cell);
      } else if (segment.length) {
        sets.push(segment);
        segment = [];
      }
    }
    if (segment.length) sets.push(segment);
  }
  return sets;
}

export function findTileCell(grid: Grid, tileId: number): { r: number; c: number } | null {
  for (let r = 0; r < grid.length; r++) {
    for (let c = 0; c < grid[r].length; c++) {
      if (grid[r][c]?.id === tileId) return { r, c };
    }
  }
  return null;
}

export function gridPositions(grid: Grid): Map<number, { r: number; c: number }> {
  const map = new Map<number, { r: number; c: number }>();
  grid.forEach((row, r) => row.forEach((cell, c) => cell && map.set(cell.id, { r, c })));
  return map;
}

/** Where each tile in a set goes by the default convention (group left / run number-axis). */
function setColumns(set: Tile[]): { tile: Tile; col: number }[] {
  const nonJokers = set.filter((t) => !t.isJoker);
  const isGroup = nonJokers.length === 0 || nonJokers.every((t) => t.number === nonJokers[0].number);

  if (isGroup) {
    // Same-number group: left-aligned in columns 0..L-1.
    return set.map((tile, i) => ({ tile, col: i }));
  }

  // Run: place each tile at the column matching its number (13 -> last column),
  // with jokers filling the interior/extension numbers.
  const numbers = nonJokers.map((t) => t.number).sort((a, b) => a - b);
  const length = set.length;
  let start = numbers[0];
  let end = start + length - 1;
  if (end > 13) {
    end = 13;
    start = 14 - length;
  }
  const byNumber = new Map<number, Tile>();
  for (const t of nonJokers) byNumber.set(t.number, t);
  const jokers = set.filter((t) => t.isJoker);

  const placements: { tile: Tile; col: number }[] = [];
  for (let n = start; n <= end; n++) {
    const tile = byNumber.get(n) ?? jokers.pop()!;
    placements.push({ tile, col: RUN_ANCHOR + n });
  }
  return placements;
}

/** Place one set into the first fully-empty row using the default convention. */
export function placeSetByDefault(grid: Grid, set: Tile[]): void {
  if (set.length === 0) return;
  let r = grid.findIndex((row) => row.every((cell) => cell === null));
  if (r === -1) {
    grid.push(Array<Cell>(BOARD_COLS).fill(null));
    r = grid.length - 1;
  }
  for (const { tile, col } of setColumns(set)) grid[r][col] = tile;
}

/** Fresh layout of all sets by the default convention (used when there is no prior grid). */
export function layoutSetsToGrid(sets: Tile[][]): Grid {
  const grid = emptyGrid();
  for (const set of sets) placeSetByDefault(grid, set);
  return grid;
}

/**
 * Build the grid for a new server board while preserving where tiles already sat:
 * a set whose tiles all kept their previous, contiguous, same-row positions stays
 * put; genuinely new/changed sets are auto-placed by the default convention.
 */
export function reconcileGrid(prevGrid: Grid | null, serverSets: Tile[][]): Grid {
  const grid = emptyGrid();
  const prev = prevGrid ? gridPositions(prevGrid) : new Map<number, { r: number; c: number }>();
  const toAutoPlace: Tile[][] = [];

  for (const set of serverSets) {
    if (set.length === 0) continue;
    const positions = set.map((t) => prev.get(t.id));
    const allKnown = positions.every((p) => p !== undefined);
    let kept = false;

    if (allKnown) {
      const rows = new Set(positions.map((p) => p!.r));
      const cols = positions.map((p) => p!.c).sort((a, b) => a - b);
      const contiguous = cols.every((c, i) => i === 0 || c === cols[i - 1] + 1);
      const free = positions.every((p) => grid[p!.r][p!.c] === null);
      if (rows.size === 1 && contiguous && free) {
        for (const t of set) {
          const p = prev.get(t.id)!;
          grid[p.r][p.c] = t;
        }
        kept = true;
      }
    }
    if (!kept) toAutoPlace.push(set);
  }

  for (const set of toAutoPlace) placeSetByDefault(grid, set);
  return grid;
}
