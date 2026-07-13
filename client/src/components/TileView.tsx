import type { Tile } from '../types';

interface Props {
  tile: Tile;
  highlighted?: boolean;
  small?: boolean;
}

/**
 * A single tile rendered entirely with CSS — no image assets. Colour drives the
 * numeral colour; jokers show a smiley. Per the asset plan, only the joker and
 * logo are bespoke artwork.
 */
export function TileView({ tile, highlighted, small }: Props) {
  const colorClass = tile.isJoker ? 'joker' : tile.color ?? 'red';
  return (
    <div className={`tile ${colorClass} ${highlighted ? 'hint' : ''} ${small ? 'small' : ''}`}>
      <span className="tile-face">{tile.isJoker ? '☺' : tile.number}</span>
    </div>
  );
}
