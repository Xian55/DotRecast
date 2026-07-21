using System;
using System.Runtime.CompilerServices;

namespace DotRecast.Recast
{
    /// Backing storage for heightfield spans.
    ///
    /// Spans are value types living in fixed-size pages, addressed by an int
    /// index rather than a reference. A span is 16 bytes packed contiguously
    /// instead of a ~40 byte heap object reached by a dependent load, which is
    /// what the C++ original does with its rcSpanPool.
    ///
    /// Pages are never resized or moved, so an index handed out earlier stays
    /// valid no matter how much the store grows. That is what lets parallel
    /// rasterization bands allocate concurrently: each band owns whole pages
    /// and bump-allocates inside them, taking the lock only to claim a new one.
    public sealed class RcSpanStore
    {
        /// Index value meaning "no span". Chosen as -1 so a zeroed column array
        /// would be an obvious bug rather than a silent reference to span 0.
        public const int Nil = -1;

        private const int PageShift = 11;             // 2048 spans per page
        private const int PageSize = 1 << PageShift;
        private const int PageMask = PageSize - 1;

        // A plain array rather than a List: the indexer below is the hottest
        // load in the whole build - every span field read in the filters and
        // the compact build goes through it - and List adds a covariance-free
        // but still non-elided bounds check plus an _items indirection on top
        // of the two loads the paging itself needs. Grown copy-on-write under
        // the lock so readers never see a torn or moved array.
        private RcSpan[][] pages = Array.Empty<RcSpan[]>();
        private int pageCount;
#if NET9_0_OR_GREATER
        // System.Threading.Lock is .NET 9+; this project also targets
        // netstandard2.1 and net8.0, where the monitor form is the only option.
        private readonly System.Threading.Lock pageLock = new System.Threading.Lock();
#else
        private readonly object pageLock = new object();
#endif

        /// Slots allocated across all pages, for diagnostics.
        public int Capacity { get; private set; }

        public ref RcSpan this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref pages[index >> PageShift][index & PageMask];
        }

        /// Claims a whole page for one allocator. Returns the index of its first
        /// slot; the caller owns [first, first + size).
        internal int ClaimPage(out int size)
        {
            size = PageSize;

            lock (pageLock)
            {
                int pageIndex = pageCount;
                if (pageIndex == pages.Length)
                {
                    // Copy-on-write: publish a whole new array rather than
                    // mutating the one concurrent readers are indexing.
                    RcSpan[][] grown = new RcSpan[Math.Max(16, pages.Length * 2)][];
                    Array.Copy(pages, grown, pageIndex);
                    grown[pageIndex] = new RcSpan[PageSize];
                    pages = grown;
                }
                else
                {
                    pages[pageIndex] = new RcSpan[PageSize];
                }

                pageCount = pageIndex + 1;
                Capacity += PageSize;
                return pageIndex << PageShift;
            }
        }
    }

    /// Hands out spans from pages of an <see cref="RcSpanStore"/>.
    ///
    /// One allocator per rasterization band, so bands never share a free list
    /// and need no locking on the hot path - only when claiming a fresh page.
    public sealed class RcSpanAllocator
    {
        private readonly RcSpanStore store;

        private int bump = 0;       // next unused slot in the current page
        private int bumpEnd = 0;    // one past the last slot of the current page
        private int freeList = RcSpanStore.Nil;

        public RcSpanAllocator(RcSpanStore store)
        {
            this.store = store;
        }

        public RcSpanStore Store => store;

        public int Alloc()
        {
            if (freeList != RcSpanStore.Nil)
            {
                int reused = freeList;
                freeList = store[reused].next;
                return reused;
            }

            if (bump == bumpEnd)
            {
                bump = store.ClaimPage(out int size);
                bumpEnd = bump + size;
            }

            return bump++;
        }

        public void Free(int index)
        {
            store[index].next = freeList;
            freeList = index;
        }
    }
}
