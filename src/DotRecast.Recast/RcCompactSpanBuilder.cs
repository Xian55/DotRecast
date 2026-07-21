namespace DotRecast.Recast
{
    /// Mutable scratch form of <see cref="RcCompactSpan"/> used while the compact
    /// heightfield is being assembled.
    ///
    /// This is a struct: one instance exists per compact span (hundreds of
    /// thousands per tile) and it only ever holds values, so a reference type
    /// cost one heap allocation per span in BuildCompactHeightfield and again in
    /// every region write-back pass. Callers mutate it through `ref` into the
    /// backing array.
    public struct RcCompactSpanBuilder
    {
        public int y;
        public int reg;
        public int con;
        public int h;

        public static RcCompactSpanBuilder NewBuilder(ref RcCompactSpan span)
        {
            RcCompactSpanBuilder builder = default;
            builder.y = span.y;
            builder.reg = span.reg;
            builder.con = span.con;
            builder.h = span.h;
            return builder;
        }

        public static RcCompactSpanBuilder NewBuilder()
        {
            return default;
        }

        public RcCompactSpanBuilder WithReg(int reg)
        {
            this.reg = reg;
            return this;
        }

        public readonly RcCompactSpan Build()
        {
            return new RcCompactSpan(this);
        }
    }
}
