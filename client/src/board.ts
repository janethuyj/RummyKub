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

// Groups live to the left of the run axis (columns 0..RUN_ANCHOR). They pack left,
// each group sitting exactly one blank column after the previous one on its row, so
// two fit side by side — a 3-tile first group gives `nnn.nnn`, a 4-tile one `nnnn.nnn`.
// A table full of same-number melds fills these slots instead of growing the board,
// which is fixed at BOARD_ROWS rows.
const GROUP_ZONE_END = RUN_ANCHOR + 1; // exclusive: columns 0..RUN_ANCHOR hold groups

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
 * A run's tiles in the left-to-right order they must sit in, plus the single column
 * its first tile starts at (fixed by its numbers). Runs only — groups pack left
 * dynamically, see groupStartOnRow.
 */
function setLayout(set: Tile[]): { ordered: Tile[]; starts: number[] } {
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

/**
 * The column where a group of `length` tiles packs on row `r`: column 0 when the row's
 * group zone is empty, otherwise one blank column after the rightmost group already
 * there. Returns -1 when it would cross the run axis or touch a neighbour (runs never
 * sit in the group zone, so the rightmost filled cell there is always a group).
 */
function groupStartOnRow(grid: Grid, r: number, length: number): number {
  let rightmost = -1;
  for (let c = 0; c < GROUP_ZONE_END; c++) if (grid[r][c] !== null) rightmost = c;
  const start = rightmost < 0 ? 0 : rightmost + 2;
  if (start + length > GROUP_ZONE_END) return -1;
  return spanFree(grid, r, start, length) ? start : -1;
}

/**
 * Place one set. Groups pack into the group zone (columns 0..RUN_ANCHOR): the left
 * slot fills across every row before any second group, and a second group sits one
 * blank column after the first, preferring a row whose first group is narrower — so
 * `nnn.nnn` is chosen over `nnnn.nnn` and the pair stays clear of the run axis. Runs
 * sit on the number axis, preferring their colour's rows. If nothing fits by
 * convention the set goes wherever there is room — better shown out of place than
 * dropped.
 *
 * The board never grows: it stays at BOARD_ROWS rows. Returns false if the set would
 * not fit anywhere, which leaves the caller responsible for not dropping its tiles.
 */
export function placeSetByDefault(grid: Grid, set: Tile[]): boolean {
  if (set.length === 0) return true;

  if (isGroupSet(set)) {
    const candidates = rowOrder(set, grid.length)
      .map((r) => ({ r, start: groupStartOnRow(grid, r, set.length) }))
      .filter((c) => c.start >= 0);
    // Left slot (start 0) across rows first; then second groups, narrowest first group
    // first (smaller start => nnn.nnn before nnnn.nnn). The sort is stable, so rows tie-
    // break by their natural order.
    const firstSlot = candidates.filter((c) => c.start === 0);
    const secondSlot = candidates.filter((c) => c.start > 0).sort((a, b) => a.start - b.start);
    const chosen = firstSlot[0] ?? secondSlot[0];
    if (chosen) {
      write(grid, chosen.r, chosen.start, set);
      return true;
    }
  } else {
    const { ordered, starts } = setLayout(set);
    for (const start of starts) {
      const rows = rowOrder(set, grid.length).filter((r) => spanFree(grid, r, start, ordered.length));
      if (rows.length > 0) {
        write(grid, rows[0], start, ordered);
        return true;
      }
    }
  }

  // Nothing fit by convention: drop the set into the first free span anywhere.
  const ordered = isGroupSet(set) ? set : setLayout(set).ordered;
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
