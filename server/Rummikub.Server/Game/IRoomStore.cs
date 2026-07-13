using System.Collections.Concurrent;

namespace Rummikub.Server.Game;

/// <summary>
/// Storage for active rooms. In-memory today; the interface exists so a Redis-backed
/// store can replace it when the game outgrows a single server instance (see the
/// deployment plan). Keep all room lookups going through this.
/// </summary>
public interface IRoomStore
{
    GameRoom Create();
    GameRoom? Get(string code);
    void Remove(string code);
}

public sealed class InMemoryRoomStore : IRoomStore
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no easily-confused chars
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _rng = new();

    public GameRoom Create()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var code = GenerateCode();
            var room = new GameRoom { Code = code };
            if (_rooms.TryAdd(code, room))
                return room;
        }
        throw new InvalidOperationException("Could not allocate a unique room code.");
    }

    public GameRoom? Get(string code) =>
        code is not null && _rooms.TryGetValue(code, out var room) ? room : null;

    public void Remove(string code) => _rooms.TryRemove(code, out _);

    private string GenerateCode()
    {
        Span<char> chars = stackalloc char[4];
        lock (_rng)
        {
            for (int i = 0; i < chars.Length; i++)
                chars[i] = Alphabet[_rng.Next(Alphabet.Length)];
        }
        return new string(chars);
    }
}
