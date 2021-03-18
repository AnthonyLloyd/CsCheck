using System;
using System.Linq;
using System.Collections.Generic;
using CsCheck;
using Xunit;

namespace Tests
{
    public class SlimCollectionsTests
    {
        [Fact]
        public void ListSlim_ModelBased()
        {
            Gen.Int.Array.Select(a =>
            {
                var l = new ListSlim<int>(a.Length);
                foreach (var i in a) l.Add(i);
                return (l, a.ToList());
            })
            .SampleModelBased(
                Gen.Int.Operation<ListSlim<int>, List<int>>((ls, l, i) => {
                    ls.Add(i);
                    l.Add(i);
                }),
                equal: (ls, l) => Check.ModelEqual(ls.ToArray(), l)
            );
        }

        [Fact]
        public void ListSlim_Concurrency()
        {
            Gen.Byte.Array.Select(a =>
            {
                var l = new ListSlim<byte>(a.Length);
                foreach (var i in a) l.Add(i);
                return l;
            })
            .SampleConcurrent(
                Gen.Byte.Operation<ListSlim<byte>>((l, i) => { lock (l) l.Add(i); }),
                Gen.Int.NonNegative.Operation<ListSlim<byte>>((l, i) => { if (i < l.Count) { var _ = l[i]; } }),
                Gen.Int.NonNegative.Select(Gen.Byte).Operation<ListSlim<byte>>((l, t) => { if (t.V0 < l.Count) l[t.V0] = t.V1; }),
                Gen.Operation<ListSlim<byte>>(l => l.ToArray()),
                equal: (l1, l2) => Check.Equal(l1.ToArray(), l2.ToArray())
            );
        }
    }


    public class ListSlim<T>
    {
        int count;
        T[] entries;
        public ListSlim() => entries = Array.Empty<T>();
        public ListSlim(int capacity) => entries = new T[capacity];
        public int Count => count;
        public T this[int i]
        {
            get => entries[i];
            set => entries[i] = value;
        }
        public void Add(T item)
        {
            int c = count;
            if (c == entries.Length)
            {
                if (c == 0) entries = new T[4];
                else
                {
                    var newEntries = new T[c * 2];
                    Array.Copy(entries, 0, newEntries, 0, c);
                    entries = newEntries;
                }
            }
            entries[c] = item;
            count = c + 1;
        }
        public T[] ToArray()
        {
            int c = count;
            var a = new T[c];
            Array.Copy(entries, 0, a, 0, c);
            return a;
        }
    }
}
