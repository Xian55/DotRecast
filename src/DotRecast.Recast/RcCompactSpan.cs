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

namespace DotRecast.Recast
{
    /** Represents a span of unobstructed space within a compact heightfield. */
    ///
    /// Eight bytes, matching the C++ original's bitfield layout. Four ints cost
    /// sixteen, and there are hundreds of thousands of these per tile - they are
    /// the largest single part of the compact heightfield's working set, and
    /// every region, contour and detail pass sweeps them.
    ///
    /// The widths are ones the algorithm already assumes: RC_BORDER_REG is
    /// 0x8000, so a region id has to fit in fifteen bits regardless; #con holds
    /// four six-bit neighbour slots; and #h is the clearance above the span,
    /// where 255 voxels is not distinguishable from more by any walkability
    /// test - which is why the C++ original clamps it to a byte too.
    public readonly struct RcCompactSpan
    {
        /** The lower extent of the span. (Measured from the heightfield's base.) */
        public readonly ushort y;

        /** The id of the region the span belongs to. (Or zero if not in a region.) */
        public readonly ushort reg;

        /** Neighbour connections in the low 24 bits, height in the high 8. */
        private readonly uint conh;

        /** Packed neighbor connection data. */
        public int con => (int)(conh & 0x00ffffffu);

        /** The height of the span. (Measured from #y.) */
        public int h => (int)(conh >> 24);

        public RcCompactSpan(RcCompactSpanBuilder span)
        {
            y = (ushort)span.y;
            reg = (ushort)span.reg;
            conh = ((uint)span.con & 0x00ffffffu) | ((uint)span.h << 24);
        }
    }
}