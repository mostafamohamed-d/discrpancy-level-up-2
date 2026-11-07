namespace DiscrepancyScanner
{
    /// <summary>
    /// Anything that is a straight line on a plan, on one story.
    ///
    /// Pulled out of <see cref="IWall"/> because the questions <see cref="WallGeometry"/>
    /// answers - which way does this run, how far does it sit off that, how much of it has
    /// something alongside it, which stretches of it have nothing - are not questions about
    /// walls. They are questions about lines, and the wall check was simply the first thing
    /// to need them.
    ///
    /// A line load is the second. One drawn load line can run past several ETABS frames and
    /// one frame can carry several drawn lines, which is the same shape as a wall run meshed
    /// into panels, so it wants the same measuring tape rather than a second copy of it.
    ///
    /// Thickness and section are not here on purpose: a detail line has neither, and a
    /// contract that demanded them would have to be answered with invented numbers.
    /// </summary>
    public interface IPlanLine
    {
        /// <summary>One end of the line in plan, in mm, in the shared (ETABS) system.</summary>
        MyPoint PlanStart { get; }

        /// <summary>The other end of the line in plan, in mm.</summary>
        MyPoint PlanEnd { get; }

        /// <summary>
        /// The story this line belongs to, or empty when it belongs to none in particular.
        /// An empty story is in reach of every one of them, which is what lets a model that
        /// holds a single floor be compared against one that names its levels.
        /// </summary>
        string Story { get; }
    }
}
