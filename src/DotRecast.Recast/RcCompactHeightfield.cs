/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
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
using System.Buffers;
using System.Collections.Generic;

using DotRecast.Core.Numerics;

namespace DotRecast.Recast
{
    /// A compact, static heightfield representing unobstructed space.
    /// @ingroup recast
    ///
    /// The four bulk arrays below are the largest allocation in a tile build -
    /// roughly 10 MB at 6e5 spans - and they are dead once the polygon and
    /// detail meshes have been built. Disposing the heightfield returns them
    /// to the shared array pool; not disposing is still correct, it just
    /// leaves them to the garbage collector as before.
    ///
    /// @warning The arrays may be longer than the size given below, because a
    /// pooled array is only guaranteed to be at least as long as requested.
    /// Bound every loop by #spanCount or #width * #height, never by Length.
    public class RcCompactHeightfield : IDisposable
    {
        public int width;					// The width of the heightfield. (Along the x-axis in cell units.)
        public int height;					// The height of the heightfield. (Along the z-axis in cell units.)
        public int spanCount;				// The number of spans in the heightfield.
        public int walkableHeight;			// The walkable height used during the build of the field.  (See: rcConfig::walkableHeight)
        public int walkableClimb;			// The walkable climb used during the build of the field. (See: rcConfig::walkableClimb)
        public int borderSize;				// The AABB border size used during the build of the field. (See: rcConfig::borderSize)
        public int maxDistance;	            // The maximum distance value of any span within the field. 
        public int maxRegions;	            // The maximum region id of any span within the field. 
        public RcVec3f bmin;				// The minimum bounds in world space. [(x, y, z)]
        public RcVec3f bmax;				// The maximum bounds in world space. [(x, y, z)]
        public float cs;					// The size of each cell. (On the xz-plane.)
        public float ch;					// The height of each cell. (The minimum increment along the y-axis.)
        public RcCompactCell[] cells;		// Array of cells. [Size: #width*#height]
        public RcCompactSpan[] spans;		// Array of spans. [Size: #spanCount]
        public int[] dist;		            // Array containing border distance data. [Size: #spanCount]
        /// Array containing area id data. [Size: #spanCount]
        ///
        /// One byte per span, matching the C++ original. Area ids are small
        /// (RC_WALKABLE_AREA is 63) and this array is swept in a 3x3 stencil by
        /// the median filter and read by every neighbour test in the region
        /// build, so its width shows up directly in cache pressure.
        public byte[] areas;

        private List<Array> pooled;

        /// Rents an array that #Dispose will hand back to the shared pool.
        ///
        /// Arrays that are replaced part-way through the build - areas by the
        /// median filter, dist by the blur - stay in the list and are returned
        /// with the rest, since nothing reads the old one afterwards.
        internal T[] Rent<T>(int minimumLength)
        {
            T[] array = ArrayPool<T>.Shared.Rent(minimumLength);
            (pooled ??= new List<Array>(6)).Add(array);
            return array;
        }

        public void Dispose()
        {
            if (pooled is null)
            {
                return;
            }

            for (int i = 0; i < pooled.Count; ++i)
            {
                switch (pooled[i])
                {
                    case int[] a:
                        ArrayPool<int>.Shared.Return(a);
                        break;
                    case byte[] a:
                        ArrayPool<byte>.Shared.Return(a);
                        break;
                    case RcCompactSpan[] a:
                        ArrayPool<RcCompactSpan>.Shared.Return(a);
                        break;
                    case RcCompactCell[] a:
                        ArrayPool<RcCompactCell>.Shared.Return(a);
                        break;
                }
            }

            pooled.Clear();

            // Recycled storage: null them so a use-after-dispose fails loudly
            // instead of quietly reading another tile's spans.
            cells = null;
            spans = null;
            dist = null;
            areas = null;
        }
    }
}