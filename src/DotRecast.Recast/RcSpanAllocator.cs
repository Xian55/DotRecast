namespace DotRecast.Recast
{
    /// Pooled span storage for rasterization.
    ///
    /// Spans are handed out from pages of <see cref="RcRecast.RC_SPANS_PER_POOL"/>
    /// and returned to a free list when a merge absorbs them, which keeps
    /// rasterization from allocating one object per (triangle, cell) incidence.
    ///
    /// One allocator is used per rasterization band so bands can run in parallel
    /// without sharing mutable state; the spans themselves are owned by the
    /// heightfield's column lists once linked in.
    public sealed class RcSpanAllocator
    {
        private RcSpanPool pools;
        private RcSpan freelist;

        public RcSpan Alloc()
        {
            // If necessary, allocate new page and update the freelist.
            if (freelist == null || freelist.next == null)
            {
                RcSpanPool spanPool = new RcSpanPool();

                // Add the pool into the list of pools.
                spanPool.next = pools;
                pools = spanPool;

                // Add new spans to the free list.
                RcSpan freeList = freelist;
                int head = 0;
                int it = RcRecast.RC_SPANS_PER_POOL;
                do
                {
                    --it;
                    spanPool.items[it].next = freeList;
                    freeList = spanPool.items[it];
                } while (it != head);

                freelist = spanPool.items[it];
            }

            // Pop item from the front of the free list.
            RcSpan newSpan = freelist;
            freelist = freelist.next;
            return newSpan;
        }

        public void Free(RcSpan span)
        {
            if (span == null)
            {
                return;
            }

            // Add the span to the front of the free list.
            span.next = freelist;
            freelist = span;
        }
    }
}
