using System;

namespace DiscrepancyScanner
{
    public class MyTransformation
    {
        private const double feetToMM = 304.8d;
        private const double metersToMM = 1000d;

        MyPoint translation;
        double rotation;
        bool isMirroredAboutX;
        bool isMirroredAboutY;

        public MyTransformation()
        {
            translation = new MyPoint();
            rotation = 0;
            isMirroredAboutX = false;
            isMirroredAboutY = false;
        }

        /// <summary>
        /// Translation in mm. The setter takes a value in feet and converts it, which is what
        /// the older forms pass; use <see cref="SetAlignment"/> when the value is already in mm.
        /// </summary>
        public MyPoint Translation { get => translation; set => translation = value.fromFeetTomm(); }

        /// <summary>Rotation about the Z axis, in degrees, applied before the translation.</summary>
        public double Rotation { get => rotation; set => rotation = value; }

        public bool IsMirroredAboutX { get => isMirroredAboutX; set => isMirroredAboutX = value; }
        public bool IsMirroredAboutY { get => isMirroredAboutY; set => isMirroredAboutY = value; }

        /// <summary>
        /// True when the two models have opposite handedness, so the Revit model has to be
        /// reflected about the X axis before it is rotated into place. Two matched points
        /// cannot tell a mirror from a rotation, which is why detecting this needs a third
        /// point off the line through the first two.
        /// </summary>
        public bool IsMirrored { get => isMirroredAboutX; set => isMirroredAboutX = value; }

        /// <summary>What the point pairs were able to determine, shown to the user.</summary>
        public string Diagnostics { get; private set; } = "";

        /// <summary>Sets the transform directly from a translation already expressed in mm.</summary>
        public void SetAlignment(MyPoint translationInMM, double rotationInDegrees, bool mirrored = false,
                                 string diagnostics = "")
        {
            translation = translationInMM ?? new MyPoint();
            rotation = rotationInDegrees;
            isMirroredAboutX = mirrored;
            Diagnostics = diagnostics ?? "";
        }

        /// <summary>
        /// The transform stated outright rather than worked out from matched points: rotate
        /// the Revit model about its own origin by <paramref name="rotationInDegrees"/>, then
        /// move it by the given displacement.
        ///
        /// The order matters and is the same one <see cref="ApplyToPointInMM"/> uses, so a
        /// displacement typed here is where the model ends up after it has been turned, not
        /// before. Anyone reading a number off a drawing has the turned model in front of
        /// them, which is the way round that needs no explaining.
        /// </summary>
        public static MyTransformation FromAngleAndDisplacement(double rotationInDegrees,
                                                                MyPoint displacementInMM,
                                                                bool mirrored = false)
        {
            MyTransformation transformation = new MyTransformation();
            transformation.SetAlignment(displacementInMM ?? new MyPoint(),
                                        NormalizeDegrees(rotationInDegrees),
                                        mirrored,
                                        "typed in rather than worked out from picked points");
            return transformation;
        }

        /// <summary>
        /// Maps a point from the Revit coordinate system into the ETABS one: reflect if the
        /// models are mirrored, then rotate about the origin, then translate. Both the point
        /// and the result are in mm.
        /// </summary>
        public MyPoint ApplyToPointInMM(MyPoint pointInMM)
        {
            if (pointInMM == null)
            {
                return new MyPoint();
            }

            MyPoint result = new MyPoint(pointInMM.X, pointInMM.Y, pointInMM.Z);
            if (isMirroredAboutX)
            {
                result.Y = -result.Y;
            }
            if (Math.Abs(rotation) > double.Epsilon)
            {
                result.Rotate(rotation);
            }
            result.Translate(translation);
            return result;
        }

        /// <summary>
        /// The other way round: takes a point already in the ETABS frame back to the Revit
        /// one. Undoing the move in the reverse order it was made - untranslate, unrotate,
        /// unreflect - is what makes it exact rather than nearly right.
        ///
        /// This is what lets a point be picked off the plan the viewer draws. Everything on
        /// that plan is in the ETABS frame, the Revit model included, because the alignment
        /// in force was baked into it as it was read; a point clicked on a Revit wall there
        /// is not the Revit coordinate of that wall, and this is how it becomes one.
        /// </summary>
        public MyPoint InvertPointInMM(MyPoint pointInMM)
        {
            if (pointInMM == null)
            {
                return new MyPoint();
            }

            MyPoint result = new MyPoint(pointInMM.X - translation.X,
                                         pointInMM.Y - translation.Y,
                                         pointInMM.Z - translation.Z);
            if (Math.Abs(rotation) > double.Epsilon)
            {
                result.Rotate(-rotation);
            }
            if (isMirroredAboutX)
            {
                result.Y = -result.Y;
            }
            return result;
        }

        /// <summary>
        /// Rotates a Revit element's own orientation into the ETABS frame, in degrees.
        /// Reflecting about the X axis negates an angle before the rotation is added.
        /// </summary>
        public double ApplyToAngleInDegrees(double angleInDegrees)
        {
            double angle = isMirroredAboutX ? -angleInDegrees : angleInDegrees;
            return angle + rotation;
        }

        /// <summary>
        /// The transform that puts one matched pair of points on top of each other at an
        /// angle already known: the model is reflected and turned by that angle, and then
        /// moved by whatever is left over so the base point lands on its counterpart.
        ///
        /// This is what a pair picked off the plan wants. The plan draws the Revit model
        /// already turned into place, so two points clicked on it say where the model should
        /// slide to and say nothing at all about the angle - and passing no angle would not
        /// leave the angle alone, it would flatten it to nought and spin the model back.
        /// </summary>
        public static MyTransformation FromBaseAndRotation(MyPoint revitBaseInFeet,
                                                           MyPoint etabsBaseInMeters,
                                                           double rotationInDegrees,
                                                           bool mirrored)
        {
            MyPoint revitBase = ScaleFeetToMM(revitBaseInFeet);
            MyPoint etabsBase = ScaleMetersToMM(etabsBaseInMeters);

            MyPoint placedBase = Reflect(revitBase, mirrored);
            if (Math.Abs(rotationInDegrees) > double.Epsilon)
            {
                placedBase.Rotate(rotationInDegrees);
            }

            MyPoint translationInMM = new MyPoint(etabsBase.X - placedBase.X,
                                                  etabsBase.Y - placedBase.Y,
                                                  etabsBase.Z - placedBase.Z);

            MyTransformation transformation = new MyTransformation();
            transformation.SetAlignment(translationInMM, NormalizeDegrees(rotationInDegrees), mirrored,
                                        Math.Abs(rotationInDegrees) > double.Epsilon
                                            ? "one matched pair, at the angle already in force"
                                            : "");
            return transformation;
        }

        /// <summary>
        /// The height move that puts a Revit level on an ETABS story, in mm: the level raised
        /// by <paramref name="offsetMM"/> is where the story sits, so whatever is at that height
        /// in Revit lands at the story's elevation. Negative offsets lower the level.
        ///
        /// This is what the height is worked out from rather than from picked points. A plan
        /// has no level to give, and a point picked in Revit sits on whatever work plane the
        /// view happens to have - which is how an alignment used to pick up a height nobody
        /// asked for. A level is the thing both models actually agree on.
        /// </summary>
        public static double HeightFromStoryAndLevel(double storyElevationMM, double levelElevationMM,
                                                     double offsetMM)
        {
            return storyElevationMM - (levelElevationMM + offsetMM);
        }

        /// <summary>
        /// This same turn, flip and move in plan, at a different height. Neither the rotation
        /// nor the flip touches Z, so the height is independent of everything else and can be
        /// set on its own. The note, when there is one, is added to the diagnostics.
        /// </summary>
        public MyTransformation WithHeight(double dzInMM, string note)
        {
            MyTransformation copy = Copy();
            string diagnostics = string.IsNullOrEmpty(note) ? Diagnostics
                               : string.IsNullOrEmpty(Diagnostics) ? note
                               : Diagnostics + "; " + note;
            copy.SetAlignment(new MyPoint(translation.X, translation.Y, dzInMM),
                              rotation, isMirroredAboutX, diagnostics);
            return copy;
        }

        /// <summary>A value copy, for holding on to what an alignment was.</summary>
        public MyTransformation Copy()
        {
            MyTransformation copy = new MyTransformation();
            copy.SetAlignment(new MyPoint(translation.X, translation.Y, translation.Z),
                              rotation, isMirroredAboutX, Diagnostics);
            copy.isMirroredAboutY = isMirroredAboutY;
            return copy;
        }

        /// <summary>
        /// The move that takes a model already placed by <paramref name="from"/> to where
        /// <paramref name="to"/> would have put it. Applying it to placed geometry gives the
        /// same answer as reading that geometry out of Revit again under the new alignment.
        ///
        /// This exists because the alignment is baked into each element as it is built, so a
        /// changed alignment used to mean reading the whole Revit model again - minutes of it,
        /// for a move. Every alignment is a reflection, then a rotation, then a translation,
        /// and one of those followed by another is still one of those, which is what makes
        /// this exact rather than an approximation and what lets an alignment be nudged as
        /// many times as it takes without the model drifting.
        /// </summary>
        public static MyTransformation Difference(MyTransformation to, MyTransformation from)
        {
            if (to == null)
            {
                return new MyTransformation();
            }

            if (from == null)
            {
                return to.Copy();
            }

            // Flipping twice is not flipping at all, so the difference flips only when the two
            // alignments disagree about it - and a flip reverses the angle it is asked to undo.
            bool mirrored = to.isMirroredAboutX != from.isMirroredAboutX;
            double rotation = NormalizeDegrees(
                to.rotation - (mirrored ? -from.rotation : from.rotation));

            // Where the old translation lands once the difference has flipped and turned it;
            // whatever is left over is the difference's own translation.
            MyPoint carried = Reflect(from.translation, mirrored);
            if (Math.Abs(rotation) > double.Epsilon)
            {
                carried.Rotate(rotation);
            }

            MyTransformation difference = new MyTransformation();
            difference.SetAlignment(new MyPoint(to.translation.X - carried.X,
                                                to.translation.Y - carried.Y,
                                                to.translation.Z - carried.Z),
                                    rotation, mirrored,
                                    "the move from one alignment to the next");
            return difference;
        }

        /// <summary>
        /// Builds the transform that lines the Revit model up with the ETABS model from two
        /// matched points. The base pair fixes the position, the rotation pair fixes the
        /// heading: the direction from base to rotation point must be the same physical
        /// direction in both models.
        ///
        /// Revit points arrive in feet and ETABS points in meters, which is how each program
        /// reports them to the user.
        /// </summary>
        public static MyTransformation FromPointPairs(MyPoint revitBaseInFeet, MyPoint etabsBaseInMeters,
                                                      MyPoint revitRotationInFeet, MyPoint etabsRotationInMeters)
        {
            return FromPointPairs(revitBaseInFeet, etabsBaseInMeters,
                                  revitRotationInFeet, etabsRotationInMeters, false);
        }

        /// <summary>
        /// As above, with the handedness stated outright.
        ///
        /// Two matched points pin down a rigid move, but they cannot tell a rotation from a
        /// reflection: both map those two points perfectly and disagree everywhere off the
        /// line joining them, which is what a mirrored result looks like. That single missing
        /// bit is what <paramref name="mirrored"/> supplies. Reflecting about the X axis and
        /// then rotating covers every possible mirror, because any reflection is a reflection
        /// about X followed by some rotation.
        /// </summary>
        public static MyTransformation FromPointPairs(MyPoint revitBaseInFeet, MyPoint etabsBaseInMeters,
                                                      MyPoint revitRotationInFeet, MyPoint etabsRotationInMeters,
                                                      bool mirrored)
        {
            MyPoint revitBase = ScaleFeetToMM(revitBaseInFeet);
            MyPoint etabsBase = ScaleMetersToMM(etabsBaseInMeters);

            bool hasRotationPair = revitRotationInFeet != null && etabsRotationInMeters != null;
            MyPoint revitSecond = hasRotationPair ? ScaleFeetToMM(revitRotationInFeet) : null;
            MyPoint etabsSecond = hasRotationPair ? ScaleMetersToMM(etabsRotationInMeters) : null;

            // Reflect first, so the rotation is measured on the already-flipped model.
            MyPoint alignedBase = Reflect(revitBase, mirrored);
            double rotationInDegrees = 0;

            if (hasRotationPair)
            {
                MyPoint alignedSecond = Reflect(revitSecond, mirrored);

                // Guard against two picks landing on the same spot, which has no heading.
                if (alignedBase.DistanceTo(alignedSecond) > 1d && etabsBase.DistanceTo(etabsSecond) > 1d)
                {
                    double revitHeading = Math.Atan2(alignedSecond.Y - alignedBase.Y, alignedSecond.X - alignedBase.X);
                    double etabsHeading = Math.Atan2(etabsSecond.Y - etabsBase.Y, etabsSecond.X - etabsBase.X);
                    rotationInDegrees = NormalizeDegrees((etabsHeading - revitHeading) * 180d / Math.PI);
                }
            }

            // The base point has to land exactly on its ETABS counterpart, so the translation
            // is whatever is left over after the reflection and the rotation.
            MyPoint placedBase = new MyPoint(alignedBase.X, alignedBase.Y, alignedBase.Z);
            if (Math.Abs(rotationInDegrees) > double.Epsilon)
            {
                placedBase.Rotate(rotationInDegrees);
            }

            MyPoint translationInMM = new MyPoint(etabsBase.X - placedBase.X,
                                                  etabsBase.Y - placedBase.Y,
                                                  etabsBase.Z - placedBase.Z);

            MyTransformation transformation = new MyTransformation();
            transformation.SetAlignment(translationInMM, rotationInDegrees, mirrored);
            transformation.Diagnostics = mirrored ? "mirrored about the X axis before rotating" : "";
            return transformation;
        }

        /// <summary>
        /// Works out whether the two models have opposite handedness from a third pair of
        /// matched points, so the user does not have to know the answer in advance.
        ///
        /// Comparing the winding of the triangle the three points form in each model settles
        /// it. The third point has to sit clearly off the line through the first two, since a
        /// straight line says nothing about handedness; when it does not, this returns false
        /// and explains why rather than guessing.
        /// </summary>
        public static bool TryDetectMirror(MyPoint revitBaseInFeet, MyPoint etabsBaseInMeters,
                                           MyPoint revitRotationInFeet, MyPoint etabsRotationInMeters,
                                           MyPoint revitThirdInFeet, MyPoint etabsThirdInMeters,
                                           out bool mirrored, out string diagnostics)
        {
            mirrored = false;
            diagnostics = "";

            if (revitBaseInFeet == null || etabsBaseInMeters == null ||
                revitRotationInFeet == null || etabsRotationInMeters == null ||
                revitThirdInFeet == null || etabsThirdInMeters == null)
            {
                diagnostics = "three complete point pairs are needed to detect mirroring";
                return false;
            }

            MyPoint revitBase = ScaleFeetToMM(revitBaseInFeet);
            MyPoint revitSecond = ScaleFeetToMM(revitRotationInFeet);
            MyPoint revitThird = ScaleFeetToMM(revitThirdInFeet);

            MyPoint etabsBase = ScaleMetersToMM(etabsBaseInMeters);
            MyPoint etabsSecond = ScaleMetersToMM(etabsRotationInMeters);
            MyPoint etabsThird = ScaleMetersToMM(etabsThirdInMeters);

            double revitWinding = Cross(revitBase, revitSecond, revitThird);
            double etabsWinding = Cross(etabsBase, etabsSecond, etabsThird);

            const double windingFloor = 1000d; // mm^2
            if (Math.Abs(revitWinding) < windingFloor || Math.Abs(etabsWinding) < windingFloor)
            {
                diagnostics = "the third point is in line with the first two, so mirroring could not be detected";
                return false;
            }

            mirrored = revitWinding * etabsWinding < 0;
            diagnostics = mirrored
                ? "detected: the models have opposite handedness"
                : "detected: the models have the same handedness";
            return true;
        }

        /// <summary>Z component of (b-a) x (c-a): positive is counter clockwise.</summary>
        private static double Cross(MyPoint a, MyPoint b, MyPoint c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static MyPoint Reflect(MyPoint p, bool mirrored)
        {
            return mirrored ? new MyPoint(p.X, -p.Y, p.Z) : new MyPoint(p.X, p.Y, p.Z);
        }

        /// <summary>Folds an angle into -180..180 so the reported rotation reads naturally.</summary>
        public static double NormalizeDegrees(double degrees)
        {
            degrees = degrees % 360d;
            if (degrees > 180d)
            {
                degrees -= 360d;
            }
            else if (degrees < -180d)
            {
                degrees += 360d;
            }
            return degrees;
        }

        private static MyPoint ScaleFeetToMM(MyPoint p)
        {
            return p == null ? new MyPoint() : new MyPoint(p.X * feetToMM, p.Y * feetToMM, p.Z * feetToMM);
        }

        private static MyPoint ScaleMetersToMM(MyPoint p)
        {
            return p == null ? new MyPoint() : new MyPoint(p.X * metersToMM, p.Y * metersToMM, p.Z * metersToMM);
        }

        public override string ToString()
        {
            return $"dx {Math.Round(translation.X)} mm, dy {Math.Round(translation.Y)} mm, " +
                   $"dz {Math.Round(translation.Z)} mm, rotation {Math.Round(rotation, 3)} deg" +
                   (isMirroredAboutX ? ", mirrored" : "");
        }
    }
}
