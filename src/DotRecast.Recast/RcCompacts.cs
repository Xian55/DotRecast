/*
recast4j copyright (c) 2021 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Linq;
using DotRecast.Core;

namespace DotRecast.Recast
{
    using static RcRecast;

    public static class RcCompacts
    {
        private const int MAX_HEIGHT = RC_SPAN_MAX_HEIGHT;

        /// @}
        /// @name Compact Heightfield Functions
        /// @see rcCompactHeightfield
        /// @{
        /// Builds a compact heightfield representing open space, from a heightfield representing solid space.
        ///
        /// This is just the beginning of the process of fully building a compact heightfield.
        /// Various filters may be applied, then the distance field and regions built.
        /// E.g: #rcBuildDistanceField and #rcBuildRegions
        ///
        /// See the #rcConfig documentation for more information on the configuration parameters.
        ///
        /// @see rcAllocCompactHeightfield, rcHeightfield, rcCompactHeightfield, rcConfig
        /// @ingroup recast
        /// 
        /// @param[in,out]	context				The build context to use during the operation.
        /// @param[in]		walkableHeight		Minimum floor to 'ceiling' height that will still allow the floor area 
        /// 									to be considered walkable. [Limit: >= 3] [Units: vx]
        /// @param[in]		walkableClimb		Maximum ledge height that is considered to still be traversable. 
        /// 									[Limit: >=0] [Units: vx]
        /// @param[in]		heightfield			The heightfield to be compacted.
        /// @param[out]		compactHeightfield	The resulting compact heightfield. (Must be pre-allocated.)
        /// @returns True if the operation completed successfully.
        public static RcCompactHeightfield BuildCompactHeightfield(RcContext context, int walkableHeight, int walkableClimb, RcHeightfield heightfield)
        {
            using var timer = context.ScopedTimer(RcTimerLabel.RC_TIMER_BUILD_COMPACTHEIGHTFIELD);

            int xSize = heightfield.width;
            int zSize = heightfield.height;
            int spanCount = GetHeightFieldSpanCount(context, heightfield);

            // Fill in header.
            RcCompactHeightfield compactHeightfield = new RcCompactHeightfield();
            compactHeightfield.width = xSize;
            compactHeightfield.height = zSize;
            compactHeightfield.borderSize = heightfield.borderSize;
            compactHeightfield.spanCount = spanCount;
            compactHeightfield.walkableHeight = walkableHeight;
            compactHeightfield.walkableClimb = walkableClimb;
            compactHeightfield.maxRegions = 0;
            compactHeightfield.bmin = heightfield.bmin;
            compactHeightfield.bmax = heightfield.bmax;
            compactHeightfield.bmax.Y += walkableHeight * heightfield.ch;
            compactHeightfield.cs = heightfield.cs;
            compactHeightfield.ch = heightfield.ch;
            compactHeightfield.cells = new RcCompactCell[xSize * zSize];
            //chf.spans = new RcCompactSpan[spanCount];
            compactHeightfield.areas = new int[spanCount];

            // Value type, so the array itself is the storage - no per-span object.
            RcCompactSpanBuilder[] tempSpans = new RcCompactSpanBuilder[spanCount];

            // Fill in cells and spans.
            int currentCellIndex = 0;
            int numColumns = xSize * zSize;
            for (int columnIndex = 0; columnIndex < numColumns; ++columnIndex)
            {
                RcSpan span = heightfield.spans[columnIndex];

                // If there are no spans at this cell, just leave the data to index=0, count=0.
                if (span == null)
                    continue;

                int tmpIdx = currentCellIndex;
                int tmpCount = 0;
                for (; span != null; span = span.next)
                {
                    if (span.area != RC_NULL_AREA)
                    {
                        int bot = span.smax;
                        int top = span.next != null ? (int)span.next.smin : MAX_HEIGHT;
                        tempSpans[currentCellIndex].y = Math.Clamp(bot, 0, MAX_HEIGHT);
                        tempSpans[currentCellIndex].h = Math.Clamp(top - bot, 0, MAX_HEIGHT);
                        compactHeightfield.areas[currentCellIndex] = span.area;
                        currentCellIndex++;
                        tmpCount++;
                    }
                }

                compactHeightfield.cells[columnIndex] = new RcCompactCell(tmpIdx, tmpCount);
            }

            // Find neighbour connections.
            const int MAX_LAYERS = RC_NOT_CONNECTED - 1;
            int maxLayerIndex = 0;
            int zStride = xSize; // for readability
            RcCompactCell[] cells = compactHeightfield.cells;

            // The four neighbour cells depend only on the column, not on the
            // span being connected, so resolve them once per column instead of
            // once per span - that removes four bounds tests, four dir-table
            // reads and four cell loads (each with a multiply) per span.
            Span<int> neighborIndex = stackalloc int[4];
            Span<int> neighborCount = stackalloc int[4];

            for (int z = 0; z < zSize; ++z)
            {
                for (int x = 0; x < xSize; ++x)
                {
                    ref RcCompactCell cell = ref cells[x + z * zStride];
                    int cellEnd = cell.index + cell.count;
                    if (cell.count == 0)
                    {
                        continue;
                    }

                    for (int dir = 0; dir < 4; ++dir)
                    {
                        int neighborX = x + GetDirOffsetX(dir);
                        int neighborZ = z + GetDirOffsetY(dir);

                        // Out of bounds neighbours stay unconnected; a zero count
                        // skips the scan below and leaves RC_NOT_CONNECTED set.
                        if (neighborX < 0 || neighborZ < 0 || neighborX >= xSize || neighborZ >= zSize)
                        {
                            neighborIndex[dir] = 0;
                            neighborCount[dir] = 0;
                            continue;
                        }

                        ref RcCompactCell neighborCell = ref cells[neighborX + neighborZ * zStride];
                        neighborIndex[dir] = neighborCell.index;
                        neighborCount[dir] = neighborCell.count;
                    }

                    for (int i = cell.index; i < cellEnd; ++i)
                    {
                        ref RcCompactSpanBuilder s = ref tempSpans[i];
                        int sy = s.y;
                        int sTop = sy + s.h;

                        for (int dir = 0; dir < 4; ++dir)
                        {
                            SetCon(ref s, dir, RC_NOT_CONNECTED);

                            // Iterate over all neighbour spans and check if any of the is
                            // accessible from current cell.
                            int baseIndex = neighborIndex[dir];
                            for (int k = baseIndex, nk = baseIndex + neighborCount[dir]; k < nk; ++k)
                            {
                                ref RcCompactSpanBuilder neighborSpan = ref tempSpans[k];
                                int ny = neighborSpan.y;

                                // Spans within a cell are stored bottom-up, so once
                                // the neighbour climbs past the climb limit no later
                                // span in this column can pass either.
                                if (ny - sy > walkableClimb)
                                {
                                    break;
                                }

                                int bot = Math.Max(sy, ny);
                                int top = Math.Min(sTop, ny + neighborSpan.h);

                                // Check that the gap between the spans is walkable,
                                // and that the climb height between the gaps is not too high.
                                if ((top - bot) >= walkableHeight && Math.Abs(ny - sy) <= walkableClimb)
                                {
                                    // Mark direction as walkable.
                                    int layerIndex = k - baseIndex;
                                    if (layerIndex < 0 || layerIndex > MAX_LAYERS)
                                    {
                                        maxLayerIndex = Math.Max(maxLayerIndex, layerIndex);
                                        continue;
                                    }

                                    SetCon(ref s, dir, layerIndex);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (maxLayerIndex > MAX_LAYERS)
            {
                throw new Exception($"rcBuildCompactHeightfield: Heightfield has too many layers {maxLayerIndex} (max: {MAX_LAYERS})");
            }

            RcCompactSpan[] builtSpans = new RcCompactSpan[spanCount];
            for (int i = 0; i < spanCount; ++i)
            {
                builtSpans[i] = tempSpans[i].Build();
            }

            compactHeightfield.spans = builtSpans;

            return compactHeightfield;
        }

        /// Returns the number of spans contained in the specified heightfield.
        ///  @ingroup recast
        ///  @param[in,out]	context		The build context to use during the operation.
        ///  @param[in]		heightfield	An initialized heightfield.
        ///  @returns The number of spans in the heightfield.
        private static int GetHeightFieldSpanCount(RcContext context, RcHeightfield heightfield)
        {
            int numCols = heightfield.width * heightfield.height;
            int spanCount = 0;
            for (int columnIndex = 0; columnIndex < numCols; ++columnIndex)
            {
                for (RcSpan span = heightfield.spans[columnIndex]; span != null; span = span.next)
                {
                    if (span.area != RC_NULL_AREA)
                    {
                        spanCount++;
                    }
                }
            }

            return spanCount;
        }
    }
}