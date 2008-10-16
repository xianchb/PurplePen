/* Copyright (c) 2006-2007, Peter Golde
 * All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without 
 * modification, are permitted provided that the following conditions are 
 * met:
 * 
 * 1. Redistributions of source code must retain the above copyright
 * notice, this list of conditions and the following disclaimer.
 * 
 * 2. Redistributions in binary form must reproduce the above copyright
 * notice, this list of conditions and the following disclaimer in the
 * documentation and/or other materials provided with the distribution.
 * 
 * 3. Neither the name of Peter Golde, nor "Purple Pen", nor the names
 * of its contributors may be used to endorse or promote products
 * derived from this software without specific prior written permission.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
 * BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE
 * USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY
 * OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Drawing;
using System.IO;

using PurplePen.MapModel;

namespace PurplePen
{
    // The course formatter transforms a CourseView into a abstract description of a course, which
    // is a CourseLayout. It does not include the description block itself.
    static class CourseFormatter
    {
        // Format the given CourseView into a bunch of course objects, and add it to the given course Layout
        public static void FormatCourseToLayout(SymbolDB symbolDB, CourseView courseView, CourseLayout courseLayout, CourseLayer layer)
        {
            EventDB eventDB = courseView.EventDB;
            CourseView.CourseViewKind kind = courseView.Kind;
            float scaleRatio = courseView.ScaleRatio;
            List<CourseView.ControlView> controlViews = courseView.ControlViews;
            CourseObj courseObj;

            // Go through all the specials in the view and process them to create course objects
            foreach(Id<Special> specialId in courseView.SpecialIds) {
                courseObj = CreateSpecial(eventDB, symbolDB, courseView, scaleRatio, specialId, layer);
                if (courseObj != null)
                    courseLayout.AddCourseObject(courseObj);
            }

            // Go through all the controls in the view and process them to create controls and legs.
            for (int controlIndex = 0; controlIndex < controlViews.Count; ++controlIndex) {
                CourseView.ControlView controlView = controlViews[controlIndex];

                // Get the angles of the legs into and out of this control, in radians.
                double angleOut = ComputeAngleOut(eventDB, courseView, controlIndex);

                // Get the normal course object associated with this control.
                courseObj = CreateCourseObject(eventDB, scaleRatio, courseView.PrintScale, controlView, angleOut);
                if (courseObj != null) {
                    courseObj.layer = layer;
                    courseLayout.AddCourseObject(courseObj);
                }

                // If this course-control indicates custom placement, place the number/code now (so it influences auto-placed numbers).
                if (CustomPlaceNumber(eventDB, controlView)) {
                    if (kind == CourseView.CourseViewKind.Normal)
                        courseObj = CreateControlNumber(eventDB, scaleRatio, controlView, courseLayout);
                    else
                        courseObj = CreateCode(eventDB, scaleRatio, controlView, courseLayout);

                    if (courseObj != null) {
                        courseObj.layer = layer;
                        courseLayout.AddCourseObject(courseObj);
                    }
                }

                if (kind == CourseView.CourseViewKind.Normal) {
                    // Get the object(s) associated with the leg(s) to the next control.
                    if (controlView.legTo != null) {
                        for (int leg = 0; leg < controlView.legTo.Length; ++leg) {
                            CourseObj[] courseObjs = CreateLeg(eventDB, scaleRatio, controlView, controlViews[controlView.legTo[leg]], controlView.legId[leg]);
                            if (courseObjs != null) {
                                foreach (CourseObj o in courseObjs) {
                                    o.layer = layer;
                                    courseLayout.AddCourseObject(o);
                                }
                            }
                        }
                    }
                }
            }

            // No go through each control again and add an automatically placed number/code to each. We do this last so that the placement
            // of all fixed-position objects influences the auto-positioned numbers so that they don't interfere.
            for (int controlIndex = 0; controlIndex < controlViews.Count; ++controlIndex) {
                CourseView.ControlView controlView = controlViews[controlIndex];
                CourseObj courseObject;

                // Only place numbers WITHOUT custom number placement. Those with custom placement were done previously above.
                if (! CustomPlaceNumber(eventDB, controlView)) {
                    if (kind == CourseView.CourseViewKind.Normal)
                        courseObject = CreateControlNumber(eventDB, scaleRatio, controlView, courseLayout);
                    else
                        courseObject = CreateCode(eventDB, scaleRatio, controlView, courseLayout);

                    if (courseObject != null) {
                        courseObject.layer = layer;
                        courseLayout.AddCourseObject(courseObject);
                    }
                }
            }
        }

        // Does this control view have a custom number placement?
        private static bool CustomPlaceNumber(EventDB eventDB, CourseView.ControlView controlView)
        {
            return ((controlView.courseControlId.IsNotNone && eventDB.GetCourseControl(controlView.courseControlId).customNumberPlacement) ||
                        (controlView.courseControlId.IsNone && eventDB.GetControl(controlView.controlId).customCodeLocation));
        }

        // Create the control number text object, avoiding existing objects on the map.
        private static CourseObj CreateControlNumber(EventDB eventDB, float scaleRatio, CourseView.ControlView controlView, IEnumerable<CourseObj> existingObjects)
        {
            ControlPoint control = eventDB.GetControl(controlView.controlId);
            CourseControl courseControl = eventDB.GetCourseControl(controlView.courseControlId);
            PointF controlLocation = control.location;
            PointF textCenterLocation;
            string text;

            if (control.kind == ControlPointKind.Normal) {
                text = controlView.ordinal.ToString();
                if (controlView.variation != 0)
                    text += controlView.variation.ToString();

                // Figure out where the control number goes.
                if (courseControl.customNumberPlacement) {
                    textCenterLocation = new PointF(controlLocation.X + courseControl.numberDeltaX, controlLocation.Y + courseControl.numberDeltaY);
                }
                else {
                    textCenterLocation = GetTextLocation(controlLocation, (CourseAppearance.controlRadius + CourseAppearance.controlNumberCircleDistance) * scaleRatio,
                                                                                  text, CourseAppearance.controlNumberFont, scaleRatio, existingObjects);
                }

                return new ControlNumberCourseObj(controlView.controlId, controlView.courseControlId, scaleRatio, text, textCenterLocation);
            }
            else {
                // Only normal controls have numbers.
                return null;
            }
        }

        // Create the control code text object, avoiding existing objects on the map.
        private static CourseObj CreateCode(EventDB eventDB, float scaleRatio, CourseView.ControlView controlView, IEnumerable<CourseObj> existingObjects)
        {
            ControlPoint control = eventDB.GetControl(controlView.controlId);
            CourseControl courseControl = controlView.courseControlId.IsNotNone ? eventDB.GetCourseControl(controlView.courseControlId) : null;
            PointF controlLocation = control.location;
            string text;
            PointF textCenterLocation;
            float distanceFromCenter = (CourseAppearance.controlRadius + CourseAppearance.codeCircleDistance) * scaleRatio;

            if (control.kind == ControlPointKind.Normal) {
                text = control.code;

                if (courseControl != null && courseControl.customNumberPlacement) {
                    textCenterLocation = new PointF(controlLocation.X + courseControl.numberDeltaX, controlLocation.Y + courseControl.numberDeltaY);
                }
                else if (courseControl == null && control.customCodeLocation) {
                    textCenterLocation = GetRectangleCenter(controlLocation, distanceFromCenter, (float) (control.codeLocationAngle * Math.PI / 180F), GetTextSize(text, CourseAppearance.controlCodeFont, scaleRatio));
                }
                else {
                    textCenterLocation = GetTextLocation(controlLocation, distanceFromCenter,
                                                                                 text, CourseAppearance.controlCodeFont, scaleRatio, existingObjects);
                }

                return new CodeCourseObj(controlView.controlId, controlView.courseControlId, scaleRatio, text, textCenterLocation);
            }
            else {
                // Only normal controls get codes.
                return null;
            }
        }

        // Find a location for the control that is furthest possible from surrounding course objects.
#if TEST
        internal
#endif
        static PointF GetTextLocation(PointF controlLocation, float distanceFromCenter, string text, FontDesc font, float scaleRatio, IEnumerable<CourseObj> list)
        {
            const double deltaAngle = Math.PI / 16;             // angle to increase by each time when testing an angle.

            // Get a list of all nearby objects that we want to stay away from.
            List<CourseObj> nearbyObjects = GetNearbyObjects(list, controlLocation, distanceFromCenter * 4);

            // Get the size of the text.
            SizeF textSize = GetTextSize(text, font, scaleRatio);

            // Try 32 different locations for the number, finding which angle has the largest distance from nearby objects.
            // Start at the default angle, so if all angles are equally good that is the one we pick.
            PointF bestPoint = new PointF();
            double bestDistance = -1;

            for (double angle = CourseAppearance.defaultControlNumberAngle; 
                   angle < CourseAppearance.defaultControlNumberAngle + 2 * Math.PI; 
                   angle += deltaAngle) 
            {
                PointF pt = GetRectangleCenter(controlLocation, distanceFromCenter, angle, textSize);
                double distanceFromNearby = GetMinDistanceFromNearby(pt, nearbyObjects);

                if (distanceFromNearby > bestDistance) {
                    bestPoint = pt;
                    bestDistance = distanceFromNearby;
                }
            }

            return bestPoint; 
        }

        // Get all object that are within a distance of a given point, but not actually a control circle AT that point.
        private static List<CourseObj> GetNearbyObjects(IEnumerable<CourseObj> list, PointF pt, float distance)
        {
            List<CourseObj> result = new List<CourseObj>();
            foreach (CourseObj obj in list) {
                if (obj is ControlCourseObj && ((ControlCourseObj) obj).location == pt)
                    continue;     // ignore the control circle.

                if (obj.DistanceFromPoint(pt) <= distance)
                    result.Add(obj);
            }

            return result;
        }

        // Given a point and list of course objects, determine the minimum distance from that point
        // to an object on that list. Returns 0 if the list is empty.
        private static double GetMinDistanceFromNearby(PointF pt, List<CourseObj> list)
        {
            double minDistance = 1000000;

            if (list.Count == 0)
                return 0;

            foreach (CourseObj courseObj in list) {
                double distance = courseObj.DistanceFromPoint(pt);
                if (distance < minDistance)
                    minDistance = distance;
            }

            return minDistance;
        }

        // Find the size of the given text in the given font/size. Only the size of digits/capital letters is returned.
#if TEST
        internal
#endif
        static SizeF GetTextSize(string text, FontDesc font, float scaleRatio)
        {
            Graphics g = Util.GetHiresGraphics();
            using (Font f = font.GetScaledFont(scaleRatio)) {
                SizeF size = g.MeasureString(text, f, new PointF(0, 0), StringFormat.GenericTypographic);

                // We really want the size of just the digits/capital letters. So, reduce by the descender size from 
                // bottom and top (no way to get offset from top of box to top of cap letters).
                FontFamily family = f.FontFamily;
                float descender = family.GetCellDescent(f.Style) * font.EmHeight * scaleRatio / family.GetEmHeight(f.Style);
                size.Height = size.Height - 2 * descender;

                return size;
            }
        }

        // Given a center point, distance from that point, angle, and rectangle size, find the point that is the center of a 
        // rectangle of the given size, at the given angle from the point, and the edge/corner of the rectangle
        // is the given distance from the point.
#if TEST
        internal
#endif
        static PointF GetRectangleCenter(PointF centerPt, float distanceFromCenter, double angle, SizeF rectSize)
        {
            double w2 = rectSize.Width / 2;              // half the width
            double h2 = rectSize.Height / 2;              // half the height
            double r = distanceFromCenter;              // shorter variable name
            double tangent = Math.Tan(angle);         // tangent of the angle.
            PointF rectCenter;

            if (w2 == 0 && h2 == 0) {
                // Degenerate case: rectangle is empty.
                rectCenter = new PointF((float) (Math.Cos(angle) * r), (float) (Math.Sin(angle) * r));
            }
            else if (w2 > 0 && Math.Abs(tangent) >= (h2 + r) / w2) {
                // Case 1: the top of bottom edge of the rectangle touches the circle.
                rectCenter = new PointF((float) ((h2 + r) / tangent), (float) (h2 + r));
                if (Math.Sin(angle) < 0) {
                    // top edge of rectangle touches the bottom of the circle.
                    rectCenter.X = -rectCenter.X;
                    rectCenter.Y = -rectCenter.Y;
                }
            }
            else if (Math.Abs(tangent) <= h2 / (w2 + r)) {
                // Case 2: the left or right edge of the rectangle touches the circle.
                rectCenter = new PointF((float) (r + w2), (float) ((r + w2) * tangent));
                if (Math.Cos(angle) < 0) {
                    // right edge of rectangle touches left of circle
                    rectCenter.X = -rectCenter.X;
                    rectCenter.Y = -rectCenter.Y;
                }
            }
            else {
                // Case 3: a corner of the rectangle touches the circle
                double normalAngle = Math.Atan2(Math.Abs(Math.Sin(angle)), Math.Abs(Math.Cos(angle)));  // normalize to first quadrant.
                double angleRect = Math.Atan2(h2, w2);
                double radiusRect = Math.Sqrt(h2 * h2 + w2 * w2);
                double alpha = normalAngle - angleRect;
                double beta = Math.Asin(Math.Sin(alpha) / r * radiusRect);
                double angleToTouch = normalAngle + beta;

                rectCenter = new PointF((float) (Math.Cos(angleToTouch) * r + w2), (float) (Math.Sin(angleToTouch) * r + h2));

                if (Math.Cos(angle) < 0)
                    rectCenter.X = - rectCenter.X;
                if (Math.Sin(angle) < 0)
                    rectCenter.Y = - rectCenter.Y;
            }

            return new PointF(rectCenter.X + centerPt.X, rectCenter.Y + centerPt.Y);
        }

        // Create the course objects associated with this special. Assign the given layer to it.
        static CourseObj CreateSpecial(EventDB eventDB, SymbolDB symbolDB, CourseView courseView, float scaleRatio, Id<Special> specialId, CourseLayer normalLayer)
        {
            Special special = eventDB.GetSpecial(specialId);
            CourseObj courseObj = null;

            switch (special.kind) {
            case SpecialKind.FirstAid:
                courseObj = new FirstAidCourseObj(specialId, scaleRatio, special.locations[0]); break;
            case SpecialKind.Water:
                courseObj = new WaterCourseObj(specialId, scaleRatio, special.locations[0]); break;
            case SpecialKind.OptCrossing:
                courseObj = new CrossingCourseObj(Id<ControlPoint>.None, Id<CourseControl>.None, specialId, scaleRatio, special.orientation, special.locations[0]); break;
            case SpecialKind.Forbidden:
                courseObj = new ForbiddenCourseObj(specialId, scaleRatio, special.locations[0]); break;
            case SpecialKind.RegMark:
                courseObj = new RegMarkCourseObj(specialId, scaleRatio, special.locations[0]); break;
            case SpecialKind.Boundary:
                courseObj = new BoundaryCourseObj(specialId, scaleRatio, new SymPath(special.locations)); break;
            case SpecialKind.OOB:
                courseObj = new OOBCourseObj(specialId, scaleRatio, special.locations); break;
            case SpecialKind.Dangerous:
                courseObj = new DangerousCourseObj(specialId, scaleRatio, special.locations); break;
            case SpecialKind.Text:
            case SpecialKind.CourseName:
                string text = (special.kind == SpecialKind.CourseName) ? courseView.CourseName : special.text;
                FontStyle fontStyle = special.fontBold ? FontStyle.Bold : FontStyle.Regular;
                if (special.fontItalic)
                    fontStyle |= FontStyle.Italic;
                RectangleF boundingRect = RectangleF.FromLTRB((float)Math.Min(special.locations[0].X, special.locations[1].X), (float)Math.Min(special.locations[0].Y, special.locations[1].Y),
                                                                                              (float)Math.Max(special.locations[0].X, special.locations[1].X), (float)Math.Max(special.locations[0].Y, special.locations[1].Y));
                courseObj = new BasicTextCourseObj(specialId, text, boundingRect, special.fontName, fontStyle);
                break;

            case SpecialKind.Descriptions:
                DescriptionKind descKind;
                DescriptionLine[] description = GetCourseDescription(eventDB, symbolDB, courseView.BaseCourseId, out descKind);
                courseObj = new DescriptionCourseObj(specialId, special.locations[0], (float) Util.Distance(special.locations[0], special.locations[1]), symbolDB, description, descKind);
                break;

            default:
                Debug.Fail("bad special kind");
                return null;
            }

            if (special.kind == SpecialKind.Descriptions)
                courseObj.layer = CourseLayer.Descriptions;
            else
                courseObj.layer = normalLayer;

            return courseObj;
        }

        // Return the description and description kind for a given CourseView.
        public static DescriptionLine[] GetCourseDescription(EventDB eventDB, SymbolDB symbolDB, Id<Course> courseId, out DescriptionKind descKind)
        {
            CourseView courseViewDescription;
            DescriptionLine[] description;
            bool noTextOrSymbols = false;

            // For all controls, show the longest description we have, and don't show text and symbols (just the grid).
            if (courseId.IsNone) {
                courseId = FindLongestDescription(eventDB, symbolDB);
                noTextOrSymbols = true;
            }

            // Get the course view for the description we're using.
            if (courseId.IsNone)
                courseViewDescription = CourseView.CreateAllControlsView(eventDB);   // only happens if there are no active courses.
            else
                courseViewDescription = CourseView.CreateCourseView(eventDB, courseId);

            // Create the description. Note the courseId is None only if we're both in all controls, and there are no courses.
            descKind = QueryEvent.GetDefaultDescKind(eventDB, courseId);
            description = DescriptionFormatter.CreateDescription(courseViewDescription, symbolDB, descKind == DescriptionKind.Symbols);
            if (noTextOrSymbols)
                DescriptionFormatter.ClearTextAndSymbols(description);

            return description;
        }

        // Find the longest description we have. If we have no courses, then return None.
        static Id<Course> FindLongestDescription(EventDB eventDB, SymbolDB symbolDB)
        {
            int longest = 0;
            Id<Course> longestCourse = Id<Course>.None;

            foreach (Id<Course> courseId in eventDB.AllCourseIds) {
                DescriptionKind descKind = QueryEvent.GetDefaultDescKind(eventDB, courseId);
                DescriptionLine[] description = DescriptionFormatter.CreateDescription(CourseView.CreateCourseView(eventDB, courseId), symbolDB, descKind == DescriptionKind.Symbols);
                if (description.Length > longest) {
                    longest = description.Length;
                    longestCourse = courseId;
                }
            }

            return longestCourse;
        }

        // Create the object associated with the control/start/finish etc with this control view.
        // AngleOut is the direction IN RADIANs leaving the control.
        static CourseObj CreateCourseObject(EventDB eventDB, float scaleRatio, float printScale, CourseView.ControlView controlView, double angleOut)
        {
            ControlPoint control = eventDB.GetControl(controlView.controlId);
            uint gaps = QueryEvent.GetControlGaps(eventDB, controlView.controlId, printScale);
            CourseObj courseObj = null;

            switch (control.kind) {
            case ControlPointKind.Start:
                courseObj = new StartCourseObj(controlView.controlId, controlView.courseControlId, scaleRatio, double.IsNaN(angleOut) ? 0 : (float)Util.RadiansToDegrees(angleOut), control.location);
                break;

            case ControlPointKind.Finish:
                courseObj = new FinishCourseObj(controlView.controlId, controlView.courseControlId, scaleRatio, gaps, control.location);
                break;

            case ControlPointKind.Normal:
                courseObj = new ControlCourseObj(controlView.controlId, controlView.courseControlId, scaleRatio, gaps, control.location);
                break;

            case ControlPointKind.CrossingPoint:
                courseObj = new CrossingCourseObj(controlView.controlId, controlView.courseControlId, Id<Special>.None, scaleRatio, control.orientation, control.location);
                break;

            default:
                Debug.Fail("bad control kind");
                return null;
            }

            return courseObj;
        }


        // Get all the locations in the course exception controlView.
        private static PointF[] GetOtherLocations(EventDB eventDB, CourseView courseView, CourseView.ControlView controlViewExcept)
        {
            List<PointF> list = new List<PointF>();

            foreach (CourseView.ControlView controlView in courseView.ControlViews) {
                if (controlView != controlViewExcept)
                    list.Add(eventDB.GetControl(controlView.controlId).location);
            }

            return list.ToArray();
        }

        // Create a single object associated with the leg from courseControlId1 to courseControlId2. Does not consider
        // flagging (but does consider bends and gaps.) Used for highlighting on the map. 
        public static CourseObj CreateSimpleLeg(EventDB eventDB, float scaleRatio, Id<CourseControl> courseControlId1, Id<CourseControl> courseControlId2)
        {
            Id<ControlPoint> controlId1 = eventDB.GetCourseControl(courseControlId1).control;
            Id<ControlPoint> controlId2 = eventDB.GetCourseControl(courseControlId2).control;
            ControlPoint control1 = eventDB.GetControl(controlId1);
            ControlPoint control2 = eventDB.GetControl(controlId2);
            LegGap[] gaps;

            SymPath legPath = GetLegPath(eventDB, control1.location, control1.kind, controlId1, control2.location, control2.kind, controlId2, scaleRatio, out gaps);
            if (legPath == null)
                return null;

            return new LegCourseObj(controlId1, courseControlId1, courseControlId2, scaleRatio, legPath, gaps);
        }

        // Create the objects associated with the leg from controlView1 to controlView2. Could be multiple because
        // a leg may be partly flagged, and so forth. Gaps do not create separate course objects.
        private static CourseObj[] CreateLeg(EventDB eventDB, float scaleRatio, CourseView.ControlView controlView1, CourseView.ControlView controlView2, Id<Leg> legId)
        {
            ControlPoint control1 = eventDB.GetControl(controlView1.controlId);
            ControlPoint control2 = eventDB.GetControl(controlView2.controlId);
            Leg leg = (legId.IsNotNone) ? eventDB.GetLeg(legId) : null;
            List<SymPath> paths = new List<SymPath>();     // paths for each segment of the leg.
            List<LegGap[]> gapsList = new List<LegGap[]>();     // gaps for each segment of the leg.
            List<bool> isFlagged = new List<bool>();             // indicates if each segment is flagged or not.

            LegGap[] gaps;                // What kind of gaps are present? Null array if none 

            // Get the path of the line, and the gaps.
            SymPath legPath = GetLegPath(eventDB, control1.location, control1.kind, controlView1.controlId, control2.location, control2.kind, controlView2.controlId, scaleRatio, out gaps);
            if (legPath == null)
                return null;

            // What kind of flagging does this leg have (none/full/begin/end)?
            FlaggingKind flagging = QueryEvent.GetLegFlagging(eventDB, controlView1.controlId, controlView2.controlId, legId);

            // Based on flagging kind, set up the paths/isFlagged lists. Add in gaps as part of it.
            if (flagging == FlaggingKind.Begin || flagging == FlaggingKind.End) {
                // Flagging is partial. We need to split the path into two.
                SymPath beginPath, endPath;
                legPath.Split(leg.flagStartStop, out beginPath, out endPath);

                paths.Add(beginPath);
                gapsList.Add(gaps);
                isFlagged.Add(flagging == FlaggingKind.Begin);

                // Update gaps for the end part.
                if (gaps != null) {
                    gaps = (LegGap[]) gaps.Clone();
                    for (int i = 0; i < gaps.Length; ++i)
                        gaps[i].distanceFromStart -= beginPath.Length;
                }

                paths.Add(endPath);
                gapsList.Add(gaps);
                isFlagged.Add(flagging == FlaggingKind.End);
            }
            else {
                // flagging is not partial. A single path is OK.
                paths.Add(legPath);
                gapsList.Add(gaps);
                isFlagged.Add(flagging == FlaggingKind.All);
            }

            // Create course objects for this leg from the paths/isFlagged lists.
            CourseObj[] objs = new CourseObj[paths.Count];
            for (int i = 0; i < paths.Count; ++i) {
                if (isFlagged[i]) 
                    objs[i] = new FlaggedLegCourseObj(controlView1.controlId, controlView1.courseControlId, controlView2.courseControlId, scaleRatio, paths[i], gapsList[i]);
                else
                    objs[i] = new LegCourseObj(controlView1.controlId, controlView1.courseControlId, controlView2.courseControlId, scaleRatio, paths[i], gapsList[i]);
            }

            return objs;
        }

        // Create a path from pt1 to pt2, with a radius aroudn the points correct for the given control kind. If the leg would
        // be of zero length, return null. The controlIds for the start and end points are optional -- if supplied, they are used
        // to deal with bends and gaps. If either is None, then the legs don't use bends or gaps. Returns the gaps to used
        // with the radius subtracted from them.
        public static SymPath GetLegPath(EventDB eventDB, PointF pt1, ControlPointKind kind1, Id<ControlPoint> controlId1, PointF pt2, ControlPointKind kind2, Id<ControlPoint> controlId2, float scaleRatio, out LegGap[] gaps)
        {
            PointF[] bends = null;
            gaps = null;

            // Get bends and gaps if controls were supplied.
            if (controlId1.IsNotNone && controlId2.IsNotNone) {
                Id<Leg> legId = QueryEvent.FindLeg(eventDB, controlId1, controlId2);
                Leg leg = (legId.IsNotNone) ? eventDB.GetLeg(legId) : null;

                // Get the path of the line.
                if (leg != null) {
                    bends = leg.bends;
                    gaps = QueryEvent.GetLegGaps(eventDB, controlId1, controlId2);
                }
            }

            return GetLegPath(pt1, GetLegRadius(kind1, scaleRatio), pt2, GetLegRadius(kind2, scaleRatio), bends, gaps);
        }

        // Create a path from pt1 to pt2, with the given radius around the legs. If the leg would
        // be of zero length, return null. If bends is non-null, then the path should include those bends.
        // If gaps is non-null, updates the gaps by subtracting the radius from them.
        private static SymPath GetLegPath(PointF pt1, double radius1, PointF pt2, double radius2, PointF[] bends, LegGap[] gaps)
        {
            double legLength = Util.Distance(pt1, pt2);

            // Check for no leg.
            if (legLength <= radius1 + radius2)
                return null;

            int bendCount = (bends == null) ? 0 : bends.Length;
            PointF[] coords = new PointF[2 + bendCount];
            PointKind[] kinds = new PointKind[2 + bendCount];

            // Set the end points.
            coords[0] = Util.DistanceAlongLine(pt1, (bendCount > 0) ? bends[0] : pt2, radius1);
            coords[coords.Length - 1] = Util.DistanceAlongLine(pt2, (bendCount > 0) ? bends[bends.Length - 1] : pt1, radius2);

            // Set the bends.
            if (bendCount > 0)
                Array.Copy(bends, 0, coords, 1, bendCount);

            // Create the path.
            for (int i = 0; i < kinds.Length; ++i)
                kinds[i] = PointKind.Normal;

            // Update the gaps (if any).
            if (gaps != null) {
                for (int i = 0; i < gaps.Length; ++i)
                    gaps[i].distanceFromStart -= (float) radius1;
            }

            return new SymPath(coords, kinds);
        }

        // Get the radius of where the leg should start from a control point of a given kind.
        private static double GetLegRadius(ControlPointKind controlKind, float scaleRatio)
        {
            double radius;

            switch (controlKind) {
            case ControlPointKind.CrossingPoint:
                radius = CourseAppearance.crossingRadius; break;

            case ControlPointKind.Finish:
                radius = CourseAppearance.finishRadius;
                break;

            case ControlPointKind.Start:
                radius = CourseAppearance.startRadius;
                break;

            case ControlPointKind.Normal:
                radius = CourseAppearance.controlRadius;
                break;

            default:
                Debug.Fail("Bad kind");
                return 0;
            }

            return radius * scaleRatio;
        }

        // Get the angle from the given control index to the next control. 
        public static double ComputeAngleOut(EventDB eventDB, CourseView courseView, int controlIndex)
        {
            PointF pt1 = eventDB.GetControl(courseView.ControlViews[controlIndex].controlId).location;

            // Get index of next control.
            int nextControlIndex = courseView.GetNextControl(controlIndex);
            if (nextControlIndex < 0)
                return double.NaN;

            // By default, the location of the next control is the direction.
            PointF pt2 = eventDB.GetControl(courseView.ControlViews[nextControlIndex].controlId).location;

            // If there is a custom leg, then use the location of the first bend instead. 
            Id<Leg> legId = QueryEvent.FindLeg(eventDB, courseView.ControlViews[controlIndex].controlId, courseView.ControlViews[nextControlIndex].controlId);
            if (legId.IsNotNone) {
                Leg leg = eventDB.GetLeg(legId);
                if (leg.bends != null && leg.bends.Length > 0)
                    pt2 = leg.bends[0];
            }

            return Math.Atan2(pt2.Y - pt1.Y, pt2.X - pt1.X);
        }
    }
}
