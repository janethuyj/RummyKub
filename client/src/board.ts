// Board grid model. The board is a fixed grid of cells; tiles snap into cells and
// a run of contiguous filled cells in a row is one set (a gap ends a set). This
// mirrors a physical Rummikub board and makes forming sets obvious.
import type { Tile } from './types';

// 21 columns = a full 13-tile run plus two 4-tile groups side by side (13+4+4).
export const BOARD_COLS = 21;
export const BOARD_ROWS = 8;

export type Cell = Tile | null;
export type Grid = Cell[][];

export function emptyGrid(rows = BOARD_ROWS): Grid {
  return Array.from({ length: rows }, () => Array<Cell>(BOARD_COLS).fill(null));
}

export function cloneGrid(grid: Grid): Grid {
  return grid.map((row) => row.slice());
}

/** Pack a list of sets into the grid, each set contiguous with a 1-cell gap after it. */
export function layoutSetsToGrid(sets: Tile[][]): Grid {
  const grid = emptyGrid();
  let r = 0;
  let c = 0;
  for (const set of sets) {
    if (set.length === 0) continue;
    if (c + set.length > BOARD_COLS) {
      r += 1;
      c = 0;
    }
    while (r >= grid.length) grid.push(Array<Cell>(BOARD_COLS).fill(null));
    for (let i = 0; i < set.length; i++) grid[r][c + i] = set[i];
    c += set.length + 1; // gap between sets
  }
  return grid;
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
