namespace Tests.Specs;

using System;

/// <summary>A refresh-on-access cache written the way the real thing is: mutable per-key entries, an explicit
/// in-flight flag, and no planted defect. The load is split into starting it and completing it so the interleaving
/// point is part of the API - which is exactly what makes it both model checkable and conformance testable.
///
/// As the system under test this owns the vocabulary and the configuration; the specification depends on it and not
/// the other way round.</summary>
public sealed class RefreshCache
{
    /// <summary>Ticks before a loaded value is considered stale and worth refreshing.</summary>
    public const int Ttl = 2;
    /// <summary>Ages and versions saturate here. A concession to the specification, which is what keeps the two
    /// comparable under <c>Conform</c>.</summary>
    public const int Cap = 3;

    public enum Key { A, B }

    /// <summary>What a read handed back. <c>Miss</c> means the caller got nothing and has to wait for a load, which
    /// is the thing a refresh-on-access cache exists to avoid.</summary>
    public enum Served { None, Miss, Fresh, Stale }

    sealed class Entry
    {
        public int Version;
        public int Age;
        public int Loads;
    }

    readonly Entry _a = new();
    readonly Entry _b = new();

    Entry Get(Key k) => k == Key.A ? _a : _b;

    public int Version(Key k) => Get(k).Version;
    public int Age(Key k) => Get(k).Age;
    public int Loads(Key k) => Get(k).Loads;

    public Served Read(Key k)
    {
        var e = Get(k);
        var served = e.Version == 0 ? Served.Miss : e.Age >= Ttl ? Served.Stale : Served.Fresh;
        if (e.Loads == 0 && (e.Version == 0 || e.Age >= Ttl)) e.Loads++;
        return served;
    }

    public void Complete(Key k)
    {
        var e = Get(k);
        e.Loads--;
        e.Version = Math.Min(e.Version + 1, Cap);
        e.Age = 0;
    }

    public void Fail(Key k) => Get(k).Loads--;

    public void Tick()
    {
        _a.Age = Math.Min(_a.Age + 1, Cap);
        _b.Age = Math.Min(_b.Age + 1, Cap);
    }
}
