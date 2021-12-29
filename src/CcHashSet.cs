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
using System.Threading;
using System.Runtime.CompilerServices; 

namespace HashSetDemo
{

    /// <summary>
    /// Concurrent HashSet implementation.
    /// </summary>
    public class CcHashSet<T> : IEnumerable<T> where T : IEquatable<T>
    {
        private struct Node<K>
        {
            public int Hash;
            public int Next;
            public K Data;
        }

        private const int NullNode = 0;

        private static readonly int[] bucketsSizeArray = {
            1367, 2741, 5471, 10_937, 19_841, 40_241, 84_463, 174_767,
            349_529, 699_053, 1_398_107, 2_796_221, 5_592_407,
            11_184_829, 22_369_661, 44_739_259, 89_478_503,
            178_956_983, 357_913_951, 715_827_947,
            1_431_655_777, 2_147_483_629 };

        private int[] slots;
        private Node<T>[] nodes;

        private const int MaxLocks = 997;
        private Object[] locks = new Object[MaxLocks];
        private Object lock0 = new Object();

        private SpinWait spinWait = new SpinWait();
        private volatile bool signalResize = false;
        private volatile int freeNodes;
        private volatile int nodePointer;

        private IEqualityComparer<T> comparer;
        private int bucketSize = 0;

        public CcHashSet()
        {
            slots = new int[bucketsSizeArray[bucketSize]];
            nodes = new Node<T>[MaxLocks + bucketsSizeArray[bucketSize]];

            comparer = EqualityComparer<T>.Default;

            for (int i = 0; i < MaxLocks; i++)
                locks[i] = new object();

            Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ClampHash(int hash)
        {
            // make sure hash is not negative (used as index after %-operation)
            // and also not equal to NullNode (used as special indicator)
            hash = hash & 0x7FFFFFFF;
            return (hash == NullNode) ? int.MaxValue : hash; 
        }

        private int AllocateNode(int mhash)
        {
            int next = nodes[mhash].Next;

            if (next == NullNode)
            {
                return Interlocked.Increment(ref nodePointer) - 1;
            }
            else
            {
                Interlocked.Decrement(ref freeNodes);
                nodes[mhash].Next = nodes[next].Next;
                return next;
            }
        }

        private void FreeNode(int node, int mhash)
        {
            Interlocked.Increment(ref freeNodes);

            int oldnext = nodes[mhash].Next;
            nodes[mhash].Next = node;
            nodes[node].Next = oldnext;
            nodes[node].Hash = 0;
        }

        /// <summary>
        /// Adds an item to the set.
        /// </summary>
        /// <param name="item">Item to add.</param>
        /// <returns>Return false, if the set already contains the item. Otherwise true.</returns>
        public bool Add(T item)
        {
            EnsureSize();

            int hash = ClampHash(comparer.GetHashCode(item));

        retry:
            while (signalResize) spinWait.SpinOnce();

            int slotl = slots.Length;
            int hashms = hash % slotl;
            int hashmsml = hashms % MaxLocks;

            lock (locks[hashmsml])
            {
                if (signalResize || slotl != slots.Length) goto retry;

                int nidx = slots[hashms];

                if (nidx == NullNode)
                {
                    slots[hashms] = nidx = AllocateNode(hashmsml);
                    nodes[nidx].Data = item;
                    nodes[nidx].Next = NullNode;
                    nodes[nidx].Hash = hash;
                    return true;
                }

                ref Node<T> node = ref nodes[nidx];

                while (true)
                {
                    if (node.Hash == hash && comparer.Equals(node.Data, item))
                    {
                        return false; // object already in set
                    }

                    if (node.Next == NullNode) break;
                    node = ref nodes[node.Next];
                }

                node.Next = AllocateNode(hashmsml);
                node = ref nodes[node.Next];
                node.Next = NullNode;
                node.Hash = hash;
                node.Data = item;

                return true;
            }

        }

        /// <summary>
        /// Removes an item from the set.
        /// </summary>
        /// <param name="item">Item to remove.</param>
        /// <returns>Returns false, if the item is not in the set. Otherwise true.</returns>
        public bool Remove(T item)
        {
            int hash = ClampHash(comparer.GetHashCode(item));

        retry:
            while (signalResize) spinWait.SpinOnce();

            int slotl = slots.Length;
            int hashms = hash % slotl;
            int hashmsml = hashms % MaxLocks;

            lock (locks[hashmsml])
            {
                if (signalResize || slotl != slots.Length) goto retry;

                int nidx = slots[hashms];
                if (nidx == NullNode) return false;

                ref Node<T> current = ref nodes[nidx];

                if (comparer.Equals(current.Data, item))
                {
                    if (current.Next != NullNode)
                    {
                        int tr = current.Next;
                        current.Data = nodes[current.Next].Data;
                        current.Next = nodes[current.Next].Next;
                        FreeNode(tr, hashmsml);
                    }
                    else
                    {
                        slots[hashms] = NullNode;
                        FreeNode(nidx, hashmsml);
                    }

                    return true;
                }

                while (current.Next != NullNode)
                {
                    ref Node<T> next = ref nodes[current.Next];

                    if (comparer.Equals(next.Data, item))
                    {
                        int tr = current.Next;
                        current.Next = next.Next;
                        FreeNode(tr, hashmsml);
                        return true;
                    }

                    current = ref nodes[current.Next];
                }

                return false;
            }
        }

        /// <summary>
        /// Checks wether an item is in the hashset. This method is not thread-safe.
        /// </summary>
        /// <param name="item">Item to check.</param>
        /// <returns>True, if the item is in the set. Otherwise false.</returns>
        public bool Contains(T item)
        {
            int hash = ClampHash(comparer.GetHashCode(item));
            int nidx = slots[hash % slots.Length];

            if (nidx == NullNode) return false;

            ref Node<T> node = ref nodes[nidx];

            while (true)
            {
                if (node.Hash == hash && comparer.Equals(node.Data, item))
                    return true;

                if (node.Next == NullNode) break;
                node = ref nodes[node.Next];
            }

            return false;
        }

        /// <summary>
        /// Clears the hashset. Internal data structures keep their sizes.
        /// This method is not thread-safe.
        /// </summary>
        public void Clear()
        {
            nodePointer = MaxLocks;
            freeNodes = 0;

            for (int i = 0; i < MaxLocks; i++)
                nodes[i].Next = NullNode;

            Array.Clear(slots, 0, slots.Length);
        }

        /// <summary>
        /// Returns the current number of elements in the set.
        /// </summary>
        /// <value>Number of elements.</value>
        public int Count
        {
            get => nodePointer - freeNodes - MaxLocks;
        }

        private void EnsureSize()
        {
            if (this.Count * 10 > slots.Length * 7)
            {
                // Signal that a resize is required
                signalResize = true;

                lock (lock0)
                {
                    if (!signalResize) return;

                    // Acquire all locks
                    for (int i = 0; i < MaxLocks;) lock (locks[i]) { i++; }

                    // At this point we can be sure that no Add or Remove action is taking place.
                    // We are running exclusively.

                    // Check condition once more
                    if (this.Count * 10 > slots.Length * 7)
                    {
                        slots = new int[bucketsSizeArray[++bucketSize]];
                        Array.Resize<Node<T>>(ref nodes, bucketsSizeArray[bucketSize] + MaxLocks);

                        int hash, modhash;

                        for (int i = MaxLocks; i < nodePointer; i++)
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

                    signalResize = false;
                }
            }
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
            int index;

            public CcHashSetEnumerator(CcHashSet<T> hashSet) => this.hashSet = hashSet;

            object IEnumerator.Current => hashSet.nodes[index].Data;
            T IEnumerator<T>.Current => hashSet.nodes[index].Data;

            public void Dispose() { }

            internal bool MoveNext()
            {
                for (++index; index < hashSet.nodePointer; index++)
                {
                    if (hashSet.nodes[index].Hash != 0) return true;
                }

                return false;
            }

            public void Reset()
            {
                index = CcHashSet<T>.MaxLocks - 1;
            }

            bool IEnumerator.MoveNext()
            {
                return this.MoveNext();
            }

        }
    }

}