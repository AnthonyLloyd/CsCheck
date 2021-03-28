using System;
using System.Linq;
using System.Collections.Generic;
using CsCheck;
using Xunit;
using System.Runtime.CompilerServices;
using System.Collections;

namespace Tests
{
    public class SlimCollectionsTests
    {
        readonly Action<string> writeLine;
        public SlimCollectionsTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;
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
                })
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
                Gen.Operation<ListSlim<byte>>(l => l.ToArray())
            );
        }

        [Fact]
        public void ListSlim_Faster()
        {
            Gen.Byte.Array
            .Faster(
                t =>
                {
                    var d = new ListSlim<byte>();
                    for (int i = 0; i < t.Length; i++)
                        d.Add(t[i]);
                    return d.Count;
                },
                t =>
                {
                    var d = new List<byte>();
                    for (int i = 0; i < t.Length; i++)
                        d.Add(t[i]);
                    return d.Count;
                },
                repeat: 50
            ).Output(writeLine);
        }
    }


    public class ListSlim<T> : IReadOnlyList<T>
    {
        static readonly T[] empty = Array.Empty<T>();
        T[] entries;
        int count;
        public ListSlim() => entries = empty;
        public ListSlim(int capacity) => entries = new T[capacity];
        public int Count => count;
        public T this[int i]
        {
            get => entries[i];
            set => entries[i] = value;
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithResize(T item)
        {
            int c = count;
            if (c == 0) entries = new T[4];
            else
            {
                var newEntries = new T[c * 2];
                Array.Copy(entries, 0, newEntries, 0, c);
                entries = newEntries;
            }
            entries[c] = item;
            count = c + 1;
        }

        public void Add(T item)
        {
            T[] e = entries;
            int c = count;
            if ((uint)c < (uint)e.Length)
            {
                e[c] = item;
                count = c + 1;
            }
            else
            {
                AddWithResize(item);
            }
        }

        public T[] ToArray()
        {
            int c = count;
            var a = new T[c];
            Array.Copy(entries, 0, a, 0, c);
            return a;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
                yield return entries[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
