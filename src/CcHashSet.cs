/* Copyright <2021> <Thorben Linneweber>
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
* 
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HashSetDemo
{

    /// <summary>
    /// Concurrent HashSet implementation.
    /// </summary>
    public class CcHashSet<T> : IEnumerable<T> where T : IEquatable<T>
    {

        private class HashSet
        {
            public struct Node
            {
                public int Hash;
                public int Next;
                public T Data;

                public void Set(int hash, int next, ref T data)
                {
                    this.Data = data;
                    this.Hash = hash;
                    this.Next = next;
                }
            }

            private static readonly int[] bucketsSizeArray = {
                1367, 2741, 5471, 10_937, 19_841, 40_241, 84_463, 174_767,
                349_529, 699_053, 1_398_107, 2_796_221, 5_592_407,
                11_184_829, 22_369_661, 44_739_259, 89_478_503,
                178_956_983, 357_913_951, 715_827_947,
                1_431_655_777, 2_147_483_629 };

            private int[] slots;
            public Node[] nodes;

            public const int NullNode = 0;

            private IEqualityComparer<T> comparer;
            private int bucketSize = 0;

            private int freeNodes;
            public int nodePointer;

            public HashSet()
            {
                slots = new int[bucketsSizeArray[bucketSize]];
                nodes = new Node[bucketsSizeArray[bucketSize]];

                comparer = EqualityComparer<T>.Default;

                nodePointer = 1; freeNodes = 0;
            }

            public void Clear()
            {
                Array.Clear(slots, 0, slots.Length);
                nodePointer = 1; freeNodes = 0;
                nodes[0].Next = NullNode;
            }

            public bool Add(T item, int hash)
            {
                EnsureSize();

                int hashms = hash % slots.Length;

                for(int nidx = slots[hashms]; nidx != NullNode; nidx = nodes[nidx].Next)
                {
                    if (nodes[nidx].Hash == hash && comparer.Equals(nodes[nidx].Data, item))
                    {
                        return false; // object already in set
                    }
                }

                int nn = AllocateNode();
                nodes[nn].Set(hash, slots[hashms], ref item);
                slots[hashms] = nn;

                return true;
            }

            public bool Contains(T item, int hash)
            {
                int hashms = hash % slots.Length;

                for(int nidx = slots[hashms]; nidx != NullNode; nidx = nodes[nidx].Next)
                {
                    if (nodes[nidx].Hash == hash && comparer.Equals(nodes[nidx].Data, item))
                    {
                        return false; // object already in set
                    }
                }
                return true;
            }

            public bool Remove(T item, int hash)
            {
                int hashms = hash % slots.Length;

                int current = slots[hashms];

                if (current == NullNode) return false;

                if (comparer.Equals(nodes[current].Data, item))
                {
                    slots[hashms] = nodes[current].Next;
                    FreeNode(current);
                    return true;
                }

                int next;

                while ((next = nodes[current].Next) != NullNode)
                {
                    if (comparer.Equals(nodes[next].Data, item))
                    {
                        nodes[current].Next = nodes[next].Next;
                        FreeNode(next);
                        return true;
                    }

                    current = next;
                }

                return false;
            }

            private void EnsureSize()
            {
                if (this.Count * 10 > slots.Length * 7)
                {
                    slots = new int[bucketsSizeArray[++bucketSize]];
                    Array.Resize<Node>(ref nodes, bucketsSizeArray[bucketSize]);

                    int hash, modhash;

                    for (int i = 1; i < nodePointer; i++)
                    {
                        hash = nodes[i].Hash;
                        if (hash != 0)
                        {
                            modhash = hash % slots.Length;
                            if (slots[modhash] == NullNode)
                            {
                                slots[modhash] = i;
                                nodes[i].Next = NullNode;
                            }
                            else
                            {
                                nodes[i].Next = slots[modhash];
                                slots[modhash] = i;
                            }
                        }
                    }
                }
            }

            public int Count
            {
                get => nodePointer - freeNodes - 1;
            }

            private int AllocateNode()
            {
                int next = nodes[0].Next;

                if (next == NullNode)
                {
                    return nodePointer++;
                }
                else
                {
                    nodePointer--;
                    nodes[0].Next = nodes[next].Next;
                    return next;
                }
            }

            private void FreeNode(int node)
            {
                freeNodes++;

                int oldnext = nodes[0].Next;
                nodes[0].Next = node;
                nodes[node].Next = oldnext;
                nodes[node].Hash = 0;
            }

        }

        private HashSet[] sets = new HashSet[NumSets];
        private const int NumSets = 17;

        private IEqualityComparer<T> hasher;

        public CcHashSet()
        {
            hasher = EqualityComparer<T>.Default;

            for (int i = 0; i < NumSets; i++)
                sets[i] = new HashSet();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ClampHash(int hash)
        {
            // make sure hash is not negative (used as index after %-operation)
            // and also not equal to NullNode (used as special indicator)
            hash = hash & 0x7FFFFFFF;
            return (hash == 0) ? int.MaxValue : hash;
        }

        /// <summary>
        /// Adds an item to the set.
        /// </summary>
        /// <param name="item">Item to add.</param>
        /// <returns>Return false, if the set already contains the item. Otherwise true.</returns>
        public bool Add(T item)
        {
            int hc = ClampHash(hasher.GetHashCode(item));
            int idx = hc % NumSets;

            lock (sets[idx]) { return sets[idx].Add(item, hc); }
        }

        /// <summary>
        /// Removes an item from the set.
        /// </summary>
        /// <param name="item">Item to remove.</param>
        /// <returns>Return false, if the set does not contain the item. Otherwise true.</returns>
        public bool Remove(T item)
        {
            int hc = ClampHash(hasher.GetHashCode(item));
            int idx = hc % NumSets;

            lock (sets[idx]) { return sets[idx].Remove(item, hc); }
        }

        /// <summary>
        /// Checks wether an item is in the hashset. This method is not thread-safe.
        /// </summary>
        /// <param name="item">Item to check.</param>
        /// <returns>True, if the item is in the set. Otherwise false.</returns>
        public bool Contains(T item)
        {
            int hc = ClampHash(hasher.GetHashCode(item));
            int idx = hc % NumSets;

            return sets[idx].Contains(item, idx);
        }

        /// <summary>
        /// Returns the current number of elements in the set.
        /// </summary>
        /// <value>Number of elements.</value>
        public int Count
        {
            get
            {
                int count = 0;
                foreach(var hs in sets) count += hs.Count;
                return count;
            }
        }

        /// <summary>
        /// Clears the hashset. Internal data structures keep their sizes.
        /// This method is not thread-safe.
        /// </summary>
        public void Clear()
        {
            foreach(var hs in sets) hs.Clear();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new CcHashSetEnumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new CcHashSetEnumerator(this);
        }

        /// <summary>
        /// Enumerates a CcHashSet.
        /// </summary>
        public class CcHashSetEnumerator : IEnumerator<T>
        {
            CcHashSet<T> hashSet;
            int index1;
            int index2;

            public CcHashSetEnumerator(CcHashSet<T> hashSet)
            {
                this.hashSet = hashSet;
            }

            object IEnumerator.Current => hashSet.sets[index1].nodes[index2].Data;
            T IEnumerator<T>.Current => hashSet.sets[index1].nodes[index2].Data;

            public void Dispose() { }

            internal bool MoveNext()
            {
                while (index1 < hashSet.sets.Length)
                {
                    while (index2++ < hashSet.sets[index1].nodePointer)
                    {
                        if (hashSet.sets[index1].nodes[index2].Hash != 0) return true;
                    }

                    index1++;
                    index2 = 0;
                }

                return false;
            }

            public void Reset()
            {
                index1 = 0;
                index2 = 0;
            }

            bool IEnumerator.MoveNext()
            {
                return this.MoveNext();
            }

        }

    }

}