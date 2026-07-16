namespace Rummikub.Engine;

/// <summary>A position in a solved layout: a concrete (colour, number), or a joker standing in for one.</summary>
internal readonly record struct Slot(TileColor Color, int Number, bool IsJoker);

/// <summary>
/// Tiles available to the solver, counted by (colour, number) rather than identity —
/// the two copies of a tile are interchangeable, so the search works on counts and the
/// caller maps the answer back onto real tiles afterwards.
/// </summary>
/// <param name="Mandatory">Tiles that must appear in the solution (board tiles cannot be taken away).</param>
/// <param name="Optional">Tiles the solver may use if it helps (the player's rack).</param>
/// <param name="RequireMeldPoints">When set, the layout must be worth at least 30 points (initial meld).</param>
internal sealed record SolverInput(
    int[,] Mandatory,
    int[,] Optional,
    int MandatoryJokers,
    int OptionalJokers,
    bool RequireMeldPoints);

internal sealed record SolverResult(List<List<Slot>> Sets, int OptionalTilesUsed);

/// <summary>
/// Exact single-turn solver, in the spirit of Den Hertog &amp; Hulshof's formulation of
/// the Rummikub problem. It considers every legal way to lay out the pooled board and
/// rack tiles — including tearing the board apart and rebuilding it — and returns the
/// best layout under the objective below: usually the one playing the most tiles, but
/// jokers are priced above a single tile, so it will hold one back rather than fritter
/// it away padding a set that was already fine.
///
/// The search is a dynamic program that sweeps the numbers 1..13 once. Runs are built
/// left to right, so at each number all that matters about the past is how many runs of
/// each colour are still open and how long they are: a run of length 1 or 2 must be
/// extended by the next number or it is not a set, while a run of length 3+ may be
/// extended or closed off. That summary, plus the jokers spent so far, is the state.
/// Groups live entirely within one number and are chosen as part of each step.
///
/// The state is bounded (there are only two copies of each tile and two jokers), so this
/// runs in milliseconds regardless of how big the board gets — no combinatorial blow-up.
/// </summary>
internal static class TurnSolver
{
    private const int ColorCount = 4;
    private const int MaxNumber = 13;

    // Objective, most significant term first:
    //   • Emptying the rack ends the round and wins it — nothing else comes close.
    //   • Otherwise play as many tiles as possible.
    //   • A joker costs two tiles to spend, so it is only worth playing when it frees at
    //     least two tiles that would otherwise be stuck. A joker can stand in for anything,
    //     which is worth far more than the one tile it puts on the board.
    //   • Among otherwise equal plays, shed the highest-value tiles: they are the penalty
    //     if somebody else goes out first.
    private const long WinScore = 1_000_000;
    private const long TileScore = 10_000;
    private const long JokerCost = 2 * TileScore;
    private const long ValueScore = 10;

    /// <summary>Best score reached at a state, and how many rack tiles that path had played.</summary>
    private readonly record struct Reached(long Score, int OptionalTiles);

    /// <summary>Per-colour, per-number allocation chosen at one number of the sweep.</summary>
    private sealed class Decision
    {
        public int[] RealUsed = new int[ColorCount];   // concrete tiles taken at this number
        public int[] GroupReal = new int[ColorCount];  // how many of those go into groups (rest into runs)
        public int[] RunJokers = new int[ColorCount];  // jokers standing in for this colour inside runs
        public int[] ExtendLong = new int[ColorCount]; // how many already-complete runs get extended
        public int GroupJokers;                        // jokers filling missing colours in groups
    }

    public static SolverResult? Solve(SolverInput input)
    {
        int jokerBudget = input.MandatoryJokers + input.OptionalJokers;

        var scores = new Dictionary<long, Reached>[MaxNumber + 1];
        var choices = new Dictionary<long, (long Prev, Decision Dec)>[MaxNumber + 1];
        scores[0] = new Dictionary<long, Reached> { [0] = default };

        for (int number = 1; number <= MaxNumber; number++)
        {
            scores[number] = new Dictionary<long, Reached>();
            choices[number] = new Dictionary<long, (long, Decision)>();

            foreach (var (key, reached) in scores[number - 1])
                Expand(input, jokerBudget, number, key, reached, scores[number], choices[number]);

            if (scores[number].Count == 0)
                return null; // a mandatory tile could not be placed in any legal layout
        }

        var best = SelectBest(input, scores[MaxNumber]);
        if (best is null)
            return null;

        return Reconstruct(input, best.Value, choices);
    }

    // ---- Search ----

    /// <summary>Expands one state across every legal allocation of the tiles at <paramref name="number"/>.</summary>
    private static void Expand(
        SolverInput input,
        int jokerBudget,
        int number,
        long key,
        Reached reached,
        Dictionary<long, Reached> next,
        Dictionary<long, (long Prev, Decision Dec)> choices)
    {
        var state = State.Decode(key);
        var dec = new Decision();
        var acc = State.EmptyRuns(); // the run layout being built for this number, filled in colour by colour
        int jokersLeft = jokerBudget - state.JokersUsed;

        // Walk the colours one at a time; each colour's runs are independent once we know
        // how many of its tiles go to groups, and the group check only needs the tally.
        void WalkColor(int color, int jokersSpent, int doubles, int singles, long gained, int meldPoints, int optional)
        {
            if (color == ColorCount)
            {
                CloseNumber(jokersSpent, doubles, singles, gained, meldPoints, optional);
                return;
            }

            int mandatory = input.Mandatory[color, number];
            int available = mandatory + input.Optional[color, number];

            for (int realUsed = mandatory; realUsed <= available; realUsed++)
            {
                for (int groupReal = 0; groupReal <= realUsed; groupReal++)
                {
                    for (int runJokers = 0; jokersSpent + runJokers <= jokersLeft; runJokers++)
                    {
                        int runTiles = realUsed - groupReal + runJokers;

                        // Runs of length 1 and 2 are not yet sets: they must be extended here.
                        int mustExtend = state.Short1[color] + state.Short2[color];
                        if (runTiles < mustExtend)
                            continue; // maybe another joker covers the shortfall

                        int spare = runTiles - mustExtend;
                        for (int extendLong = 0; extendLong <= Math.Min(state.Long[color], spare); extendLong++)
                        {
                            acc.Short1[color] = spare - extendLong;              // brand-new runs
                            acc.Short2[color] = state.Short1[color];             // length 1 -> 2
                            acc.Long[color] = state.Short2[color] + extendLong;  // length 2 -> 3, and 3+ extended

                            dec.RealUsed[color] = realUsed;
                            dec.GroupReal[color] = groupReal;
                            dec.RunJokers[color] = runJokers;
                            dec.ExtendLong[color] = extendLong;

                            int optionalUsed = realUsed - mandatory;
                            WalkColor(
                                color + 1,
                                jokersSpent + runJokers,
                                doubles + (groupReal == 2 ? 1 : 0),
                                singles + (groupReal == 1 ? 1 : 0),
                                gained + optionalUsed * (TileScore + number * ValueScore),
                                meldPoints + number * (realUsed + runJokers),
                                optional + optionalUsed);
                        }
                    }
                }
            }
        }

        void CloseNumber(int jokersSpent, int doubles, int singles, long gained, int meldPoints, int optional)
        {
            for (int groupJokers = 0; jokersSpent + groupJokers <= jokersLeft; groupJokers++)
            {
                if (!CanFormGroups(doubles, singles, groupJokers))
                    continue;

                acc.JokersUsed = state.JokersUsed + jokersSpent + groupJokers;
                acc.MeldPoints = input.RequireMeldPoints
                    ? Math.Min(RuleValidator.InitialMeldMinimum, state.MeldPoints + meldPoints + number * groupJokers)
                    : 0;

                dec.GroupJokers = groupJokers;

                var candidate = new Reached(reached.Score + gained, reached.OptionalTiles + optional);
                long doneKey = acc.Encode();
                if (next.TryGetValue(doneKey, out var existing) && existing.Score >= candidate.Score)
                    continue;

                next[doneKey] = candidate;
                choices[doneKey] = (key, Clone(dec));
            }
        }

        WalkColor(0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Can <paramref name="doubles"/> colours contributing two tiles, <paramref name="singles"/>
    /// contributing one, and <paramref name="jokers"/> jokers be partitioned into groups of 3-4
    /// tiles with distinct colours? Only the tally matters, since the colours are interchangeable.
    /// </summary>
    private static bool CanFormGroups(int doubles, int singles, int jokers)
    {
        if (doubles == 0 && singles == 0 && jokers == 0)
            return true; // no groups at this number

        // One group: no colour may appear twice in it.
        if (doubles == 0 && singles + jokers is >= RuleValidator.MinSetSize and <= ColorCount)
            return true;

        // Two groups: the doubled colours sit in both, the singles and jokers split between them.
        for (int s1 = 0; s1 <= singles; s1++)
        {
            for (int j1 = 0; j1 <= jokers; j1++)
            {
                int size1 = doubles + s1 + j1;
                int size2 = doubles + (singles - s1) + (jokers - j1);
                if (size1 is >= RuleValidator.MinSetSize and <= ColorCount &&
                    size2 is >= RuleValidator.MinSetSize and <= ColorCount)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Picks the highest-scoring finished layout: no run left dangling, every board joker placed.</summary>
    private static (long Key, long Score)? SelectBest(SolverInput input, Dictionary<long, Reached> final)
    {
        (long Key, long Score)? best = null;
        int rackSize = RackSize(input);

        foreach (var (key, reached) in final)
        {
            var state = State.Decode(key);
            if (state.Short1.Any(v => v != 0) || state.Short2.Any(v => v != 0))
                continue; // a run of length 1 or 2 ran off the end of the board
            if (state.JokersUsed < input.MandatoryJokers)
                continue; // a joker already on the board was left unplaced
            if (input.RequireMeldPoints && state.MeldPoints < RuleValidator.InitialMeldMinimum)
                continue;

            int rackJokers = state.JokersUsed - input.MandatoryJokers;
            long total = reached.Score + rackJokers * (TileScore - JokerCost);

            // Holding a joker back is worthless if the round is over: spend it to go out.
            if (reached.OptionalTiles + rackJokers == rackSize)
                total += WinScore;

            if (best is null || total > best.Value.Score)
                best = (key, total);
        }

        return best;
    }

    private static int RackSize(SolverInput input)
    {
        int total = input.OptionalJokers;
        for (int c = 0; c < ColorCount; c++)
            for (int n = 1; n <= MaxNumber; n++)
                total += input.Optional[c, n];
        return total;
    }

    // ---- Rebuilding the layout ----

    /// <summary>Replays the chosen decisions forward, turning them back into concrete sets.</summary>
    private static SolverResult Reconstruct(
        SolverInput input,
        (long Key, long Score) best,
        Dictionary<long, (long Prev, Decision Dec)>[] choices)
    {
        var path = new Decision[MaxNumber + 1];
        long key = best.Key;
        for (int number = MaxNumber; number >= 1; number--)
        {
            var (prev, dec) = choices[number][key];
            path[number] = dec;
            key = prev;
        }

        var sets = new List<List<Slot>>();
        var open = new RunsInProgress[ColorCount];
        for (int c = 0; c < ColorCount; c++)
            open[c] = new RunsInProgress();

        int optionalUsed = 0;

        for (int number = 1; number <= MaxNumber; number++)
        {
            var dec = path[number];

            for (int c = 0; c < ColorCount; c++)
            {
                var color = (TileColor)c;
                optionalUsed += dec.RealUsed[c] - input.Mandatory[c, number];

                // Slots to place into runs at this (colour, number): concrete tiles, then jokers.
                var slots = new Queue<Slot>();
                for (int i = 0; i < dec.RealUsed[c] - dec.GroupReal[c]; i++)
                    slots.Enqueue(new Slot(color, number, IsJoker: false));
                for (int i = 0; i < dec.RunJokers[c]; i++)
                    slots.Enqueue(new Slot(color, number, IsJoker: true));

                sets.AddRange(open[c].Advance(slots, dec.ExtendLong[c]));
            }

            sets.AddRange(BuildGroups(dec.GroupReal, dec.GroupJokers, number));
        }

        for (int c = 0; c < ColorCount; c++)
            sets.AddRange(open[c].Close());

        int jokersPlaced = sets.SelectMany(s => s).Count(s => s.IsJoker);
        optionalUsed += jokersPlaced - input.MandatoryJokers;

        return new SolverResult(sets, optionalUsed);
    }

    /// <summary>The runs of one colour that are still being built as the sweep moves right.</summary>
    private sealed class RunsInProgress
    {
        private List<List<Slot>> _len1 = new();
        private List<List<Slot>> _len2 = new();
        private List<List<Slot>> _complete = new();

        /// <summary>Consumes this number's slots and returns any runs that are now finished.</summary>
        public List<List<Slot>> Advance(Queue<Slot> slots, int extendComplete)
        {
            // Every unfinished run must take a slot, or it never becomes a set.
            foreach (var run in _len1.Concat(_len2))
                run.Add(slots.Dequeue());

            var stillGrowing = _complete.Take(extendComplete).ToList();
            var finished = _complete.Skip(extendComplete).ToList();
            foreach (var run in stillGrowing)
                run.Add(slots.Dequeue());

            var promoted = _len2;          // length 2 + this slot = a real run
            var wasLen1 = _len1;           // length 1 + this slot = length 2

            _complete = stillGrowing.Concat(promoted).ToList();
            _len2 = wasLen1;
            _len1 = new List<List<Slot>>();
            while (slots.Count > 0)
                _len1.Add(new List<Slot> { slots.Dequeue() });

            return finished;
        }

        /// <summary>Runs still open once the sweep passes 13. The solver guarantees these are all complete.</summary>
        public List<List<Slot>> Close() => _complete;
    }

    /// <summary>Splits one number's group tiles into actual groups, mirroring <see cref="CanFormGroups"/>.</summary>
    private static List<List<Slot>> BuildGroups(int[] groupReal, int groupJokers, int number)
    {
        var groups = new List<List<Slot>>();
        var doubled = new List<int>();
        var single = new List<int>();
        for (int c = 0; c < ColorCount; c++)
        {
            if (groupReal[c] == 2) doubled.Add(c);
            else if (groupReal[c] == 1) single.Add(c);
        }

        if (doubled.Count == 0 && single.Count == 0 && groupJokers == 0)
            return groups;

        if (doubled.Count == 0 && single.Count + groupJokers is >= RuleValidator.MinSetSize and <= ColorCount)
        {
            var only = single.Select(c => new Slot((TileColor)c, number, false)).ToList();
            only.AddRange(Jokers(groupJokers, number));
            groups.Add(only);
            return groups;
        }

        for (int s1 = 0; s1 <= single.Count; s1++)
        {
            for (int j1 = 0; j1 <= groupJokers; j1++)
            {
                int size1 = doubled.Count + s1 + j1;
                int size2 = doubled.Count + (single.Count - s1) + (groupJokers - j1);
                if (size1 is < RuleValidator.MinSetSize or > ColorCount ||
                    size2 is < RuleValidator.MinSetSize or > ColorCount)
                    continue;

                var first = doubled.Select(c => new Slot((TileColor)c, number, false)).ToList();
                var second = doubled.Select(c => new Slot((TileColor)c, number, false)).ToList();
                first.AddRange(single.Take(s1).Select(c => new Slot((TileColor)c, number, false)));
                second.AddRange(single.Skip(s1).Select(c => new Slot((TileColor)c, number, false)));
                first.AddRange(Jokers(j1, number));
                second.AddRange(Jokers(groupJokers - j1, number));
                groups.Add(first);
                groups.Add(second);
                return groups;
            }
        }

        throw new InvalidOperationException($"Groups at {number} passed the feasibility check but could not be built.");
    }

    private static IEnumerable<Slot> Jokers(int count, int number)
        => Enumerable.Repeat(new Slot(default, number, IsJoker: true), count);

    private static Decision Clone(Decision d) => new()
    {
        RealUsed = (int[])d.RealUsed.Clone(),
        GroupReal = (int[])d.GroupReal.Clone(),
        RunJokers = (int[])d.RunJokers.Clone(),
        ExtendLong = (int[])d.ExtendLong.Clone(),
        GroupJokers = d.GroupJokers,
    };

    // ---- State packing ----

    /// <summary>
    /// What the sweep needs to remember at a number boundary: per colour, how many runs are
    /// open at length 1, length 2, and length 3-or-more; plus jokers spent and (for an initial
    /// meld) points so far, capped at 30 since anything above that is equivalent.
    /// </summary>
    private sealed class State
    {
        public int[] Short1 = new int[ColorCount];
        public int[] Short2 = new int[ColorCount];
        public int[] Long = new int[ColorCount];
        public int JokersUsed;
        public int MeldPoints;

        public static State EmptyRuns() => new();

        // At most 4 runs of a colour can pass through one number (two copies plus two jokers),
        // so each count fits in 3 bits: 9 bits per colour, 36 for the board, then the extras.
        public long Encode()
        {
            long key = (long)MeldPoints | ((long)JokersUsed << 5);
            for (int c = 0; c < ColorCount; c++)
            {
                long packed = (long)Short1[c] | ((long)Short2[c] << 3) | ((long)Long[c] << 6);
                key |= packed << (7 + c * 9);
            }
            return key;
        }

        public static State Decode(long key)
        {
            var state = new State
            {
                MeldPoints = (int)(key & 0x1F),
                JokersUsed = (int)((key >> 5) & 0x3),
            };
            for (int c = 0; c < ColorCount; c++)
            {
                long packed = (key >> (7 + c * 9)) & 0x1FF;
                state.Short1[c] = (int)(packed & 0x7);
                state.Short2[c] = (int)((packed >> 3) & 0x7);
                state.Long[c] = (int)((packed >> 6) & 0x7);
            }
            return state;
        }
    }
}
