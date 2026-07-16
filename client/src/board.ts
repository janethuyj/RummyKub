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

// Groups live to the left of the run axis, and two fit side by side on one row: the
// first left-aligned, the second starting at column 5 — clear of a 4-tile first group
// (columns 0..3), with column 4 blank between them, and up against the run axis at
// column 8. A table full of same-number melds fills these second slots instead of
// growing the board, which is fixed at BOARD_ROWS rows.
const GROUP_STARTS = [0, 5];

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

function isGroupSet(set: Tile[]): boolean {
  const nonJokers = set.filter((t) => !t.isJoker);
  return nonJokers.length === 0 || nonJokers.every((t) => t.number === nonJokers[0].number);
}

/**
 * A set's tiles in the left-to-right order they must sit in, plus the columns its first
 * tile would like to start at, best first. A group has two candidate slots; a run has
 * one, fixed by its numbers.
 */
function setLayout(set: Tile[]): { ordered: Tile[]; starts: number[] } {
  if (isGroupSet(set)) return { ordered: set, starts: GROUP_STARTS };

  // Run: each tile sits at the column matching its number (13 -> last column), with
  // jokers filling the interior/extension numbers.
  const nonJokers = set.filter((t) => !t.isJoker);
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

  const ordered: Tile[] = [];
  for (let n = start; n <= end; n++) ordered.push(byNumber.get(n) ?? jokers.pop()!);
  return { ordered, starts: [RUN_ANCHOR + start] };
}

/**
 * True when `length` tiles can start at `start` on row `r`. Requires a blank column on
 * each side, so that a set never touches its neighbour — contiguous cells read as one
 * set, so two touching sets would merge into a single invalid one.
 */
function spanFree(grid: Grid, r: number, start: number, length: number): boolean {
  if (start < 0 || start + length > BOARD_COLS) return false;
  const lo = Math.max(0, start - 1);
  const hi = Math.min(BOARD_COLS - 1, start + length);
  for (let c = lo; c <= hi; c++) if (grid[r][c] !== null) return false;
  return true;
}

function write(grid: Grid, r: number, start: number, ordered: Tile[]): void {
  ordered.forEach((tile, i) => {
    grid[r][start + i] = tile;
  });
}

// Preferred rows for a single-colour run, so same-colour runs group together.
const RUN_ROWS: Record<string, number[]> = {
  red: [0, 1],
  blue: [2, 3],
  orange: [4, 5],
  black: [6, 7],
};

/** The row search order for a set: a single-colour run prefers its colour's rows. */
function rowOrder(set: Tile[], rowCount: number): number[] {
  const all = Array.from({ length: rowCount }, (_, i) => i);
  if (isGroupSet(set)) return all;

  const nonJokers = set.filter((t) => !t.isJoker);
  const preferred = (RUN_ROWS[nonJokers[0].color ?? ''] ?? []).filter((r) => r < rowCount);
  return [...preferred, ...all.filter((r) => !preferred.includes(r))];
}

/** How many blank columns sit immediately to the left of `start` on row `r`. */
function gapToLeft(grid: Grid, r: number, start: number): number {
  let blanks = 0;
  for (let c = start - 1; c >= 0 && grid[r][c] === null; c--) blanks++;
  return blanks;
}

/**
 * Rows to try for a second group, widest breathing room first. Beside a 3-tile group the
 * newcomer gets two blank columns (3 and 4); beside a 4-tile one it gets only column 4
 * and the two read as though they were crowded together. Prefer the roomier row and fall
 * back to the tight one only when nothing better is free.
 */
function roomiestFirst(grid: Grid, rows: number[], start: number): number[] {
  // Sort is stable, so rows with equal room keep their original preference order.
  return [...rows].sort((a, b) => gapToLeft(grid, b, start) - gapToLeft(grid, a, start));
}

/**
 * Place one set, packing it into the first row where it fits — so a left-aligned group
 * and a number-axis run can share a row. Single-colour runs prefer their colour's rows.
 * The convention is tried first (each group slot in turn, left slot across every row
 * before the column-5 slot); if every slot is taken the set goes wherever there is room,
 * because showing it out of convention beats not showing it at all.
 *
 * The board never grows: it stays at BOARD_ROWS rows. Returns false if the set would not
 * fit anywhere, which leaves the caller responsible for not dropping its tiles.
 */
export function placeSetByDefault(grid: Grid, set: Tile[]): boolean {
  if (set.length === 0) return true;
  const { ordered, starts } = setLayout(set);

  for (const start of starts) {
    let rows = rowOrder(set, grid.length).filter((r) => spanFree(grid, r, start, ordered.length));
    // Only the second group slot has a neighbour to its left worth keeping clear of.
    // Runs keep their colour's row order untouched.
    if (start > 0 && isGroupSet(set)) rows = roomiestFirst(grid, rows, start);
    if (rows.length > 0) {
      write(grid, rows[0], start, ordered);
      return true;
    }
  }

  for (let r = 0; r < grid.length; r++) {
    for (let start = 0; start + ordered.length <= BOARD_COLS; start++) {
      if (spanFree(grid, r, start, ordered.length)) {
        write(grid, r, start, ordered);
        return true;
      }
    }
  }
  return false;
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
      const withinRows = positions.every((p) => p!.r < BOARD_ROWS);
      const free = withinRows && positions.every((p) => grid[p!.r][p!.c] === null);
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

  let placedAll = true;
  for (const set of toAutoPlace) if (!placeSetByDefault(grid, set)) placedAll = false;

  // Holding tiles at their old positions can fragment the board enough to leave a set
  // with nowhere to go. A clean re-layout defragments, at the cost of moving tiles the
  // player had positioned — better than dropping a set off the board entirely.
  return placedAll ? grid : layoutSetsToGrid(serverSets);
}
