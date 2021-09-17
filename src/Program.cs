using System;
using System.Diagnostics;
using System.Threading.Tasks;

using SystemHashSet = System.Collections.Generic.HashSet<HashSetDemo.Custom>;
using CustomHashSet = HashSetDemo.CcHashSet<HashSetDemo.Custom>;

namespace HashSetDemo
{
    public struct Custom : IEquatable<Custom>
    {
        public int idx1;
        public int idx2;

        public Custom(int a, int b)
        {
            idx1 = a;
            idx2 = b;
        }

        public override int GetHashCode()
        {
            return (idx1 << 16) ^ (31 * idx2);
        }

        public bool Equals(Custom other)
        {
            return idx1 == other.idx1 &&
                   idx2 == other.idx2;
        }
    }

    class Program
    {
        const int TestSize = 10_000_000;

        static SystemHashSet shs;
        static CustomHashSet chs;

        static void Perform(int seed)
        {
            Random rnd = new Random(seed);

            for (int i = 0; i < TestSize; i++)
            {
                shs.Add(new Custom(rnd.Next(1, TestSize / 1000), rnd.Next(1, TestSize / 1000)));
            }
        }

        static void PerformRem(int seed)
        {
            Random rnd = new Random(seed);

            for (int i = 0; i < TestSize; i++)
            {
                shs.Remove(new Custom(rnd.Next(1, TestSize / 1000), rnd.Next(1, TestSize / 1000)));
            }
        }

        static void CustomPerform(int seed)
        {
            Random rnd = new Random(seed);

            for (int i = 0; i < TestSize; i++)
            {
                chs.Add(new Custom(rnd.Next(1, TestSize / 1000), rnd.Next(1, TestSize / 1000)));
            }
        }

        static void CustomPerformRem(int seed)
        {
            Random rnd = new Random(seed);

            for (int i = 0; i < TestSize; i++)
            {
                chs.Remove(new Custom(rnd.Next(1, TestSize / 1000), rnd.Next(1, TestSize / 1000)));
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Benchmarking...");

            Stopwatch sw;

            sw = Stopwatch.StartNew();
            shs = new SystemHashSet();
            Perform(1);
            Perform(2);
            Perform(3);
            Perform(4);
            PerformRem(5);
            PerformRem(6);
            PerformRem(7);
            PerformRem(8);
            sw.Stop();
            Console.WriteLine($"SERIAL: System HashSet implementation took {sw.ElapsedMilliseconds}ms. Checksum {shs.Count}.");

            sw = Stopwatch.StartNew();
            chs = new CustomHashSet();
            CustomPerform(1);
            CustomPerform(2);
            CustomPerform(3);
            CustomPerform(4);
            CustomPerformRem(5);
            CustomPerformRem(6);
            CustomPerformRem(7);
            CustomPerformRem(8);
            sw.Stop();
            Console.WriteLine($"SERIAL: CcHashSet implementation took {sw.ElapsedMilliseconds}ms. Checksum {chs.Count}.");

            sw = Stopwatch.StartNew();

            chs = new CustomHashSet();
            Parallel.Invoke(() => CustomPerform(1), () => CustomPerform(2), () => CustomPerform(3), () => CustomPerform(4));
            Parallel.Invoke(() => CustomPerformRem(5), () => CustomPerformRem(6), () => CustomPerformRem(7), () => CustomPerformRem(8));

            sw.Stop();
            Console.WriteLine($"PARALLEL: CcHashSet implementation took {sw.ElapsedMilliseconds}ms. Checksum {chs.Count}.");
        }
    }
}
