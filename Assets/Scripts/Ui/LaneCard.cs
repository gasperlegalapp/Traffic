using System;
using System.Collections.Generic;

namespace Traffic.Ui
{
    /// <summary>What the lane tool's card can do to the lane that is picked.</summary>
    public enum LaneAct { None, Add, Remove, Left, Straight, Right, Middle, Reverse }

    /// <summary>
    /// The small card that sits beside a lane picked with the lane tool, with a button for each
    /// thing that can be done to it. Like the toolbar it lays itself out and draws onto an
    /// ISurface, and has no Unity references.
    /// </summary>
    public sealed class LaneCard
    {
        /// <summary>The picked lane is one added along part of its road: Remove takes just that away.</summary>
        public bool Added;
        /// <summary>The picked lane is the turning lane down the middle of its road.</summary>
        public bool Middle;
        /// <summary>The road could be given a turning lane down its middle, so the card offers one.</summary>
        public bool CanMiddle;
        /// <summary>The road has a median. What it can be given, or has, is then left-turn lanes in the median, not a lane down the whole middle.</summary>
        public bool Median;
        /// <summary>The picked lane could be turned round to run the other way, so the card offers that.</summary>
        public bool CanReverse;
        /// <summary>The picked lane leads to an intersection with a light or signs, so the card offers the ways it may go there.</summary>
        public bool Turns;
        /// <summary>Left, straight on, right: whether there is a road that way, and whether the lane may go that way.</summary>
        public readonly bool[] TurnThere = new bool[3], TurnMay = new bool[3];

        private double x, y, high;
        private const double Wide = 276, ButtonW = 124, ButtonH = 40, ArrowW = 52;
        private static readonly LaneAct[] Acts = { LaneAct.Add, LaneAct.Remove };
        private static readonly LaneAct[] Ways = { LaneAct.Left, LaneAct.Straight, LaneAct.Right };
        private static readonly string[] WayIcons = { "goleft", "goahead", "goright" };

        /// <summary>Place the card by the spot on the screen where the picked lane was clicked: above it, or below if there is no room.</summary>
        public void Layout(double anchorX, double anchorY, double screenW, double screenH)
        {
            high = 104 + (Turns ? 68 : 0) + (CanMiddle ? 48 : 0) + (CanReverse ? 48 : 0);
            x = Math.Max(8, Math.Min(screenW - Wide - 8, anchorX - Wide / 2));
            y = anchorY - high - 30;
            if (y < 8) y = Math.Min(screenH - high - 8, anchorY + 30);
        }

        private void Place(int i, out double bx, out double by)
        {
            bx = x + 10 + i * (ButtonW + 8);
            by = y + 30;
        }

        private void PlaceWay(int i, out double bx, out double by)
        {
            bx = x + 10 + i * (ArrowW + 8);
            by = y + 98 + (CanMiddle ? 48 : 0) + (CanReverse ? 48 : 0);
        }

        /// <summary>The top of the button that turns the lane round: under the first row, and under the turning-lane button when that is there.</summary>
        private double ReverseY => y + 78 + (CanMiddle ? 48 : 0);

        public bool Contains(double px, double py)
        {
            return px >= x && px < x + Wide && py >= y && py < y + high;
        }

        public LaneAct ItemAt(double px, double py)
        {
            for (int i = 0; i < Acts.Length; i++)
            {
                double bx, by;
                Place(i, out bx, out by);
                if (Middle && Acts[i] == LaneAct.Add) continue;
                if (px >= bx && px < bx + ButtonW && py >= by && py < by + ButtonH) return Acts[i];
            }
            if (CanMiddle && px >= x + 10 && px < x + Wide - 10 && py >= y + 78 && py < y + 78 + ButtonH) return LaneAct.Middle;
            if (CanReverse && px >= x + 10 && px < x + Wide - 10 && py >= ReverseY && py < ReverseY + ButtonH) return LaneAct.Reverse;
            for (int i = 0; Turns && i < Ways.Length; i++)
            {
                double bx, by;
                PlaceWay(i, out bx, out by);
                if (TurnThere[i] && px >= bx && px < bx + ArrowW && py >= by && py < by + ButtonH) return Ways[i];
            }
            return LaneAct.None;
        }

        public void Draw(ISurface s, double mouseX, double mouseY)
        {
            LaneAct over = ItemAt(mouseX, mouseY);
            Dock.Card(s, x, y, Wide, high, 16);
            s.Text(Middle ? (Median ? "LEFT-TURN LANES" : "TURNING LANE") : Added ? "ADDED LANE" : "LANE", x + 12, y + 9, Wide - 24, 14, 10, true, 0, Dock.Dim);
            if (CanMiddle)
            {
                s.Fill(x + 10, y + 78, Wide - 20, ButtonH, 12, over == LaneAct.Middle ? Dock.Hover : Dock.Lift);
                s.Icon("goleft", x + 22, y + 78 + 11, 18, Dock.Light);
                s.Text(Median ? "Left-turn lanes in the median" : "Turning lane in the middle", x + 48, y + 78, Wide - 68, ButtonH, 14, true, 0, Dock.Light);
            }
            if (CanReverse)
            {
                s.Fill(x + 10, ReverseY, Wide - 20, ButtonH, 12, over == LaneAct.Reverse ? Dock.Hover : Dock.Lift);
                s.Icon("replace", x + 22, ReverseY + 11, 18, Dock.Light);
                s.Text("Run this lane the other way", x + 48, ReverseY, Wide - 68, ButtonH, 14, true, 0, Dock.Light);
            }
            for (int i = 0; i < Acts.Length; i++)
            {
                double bx, by;
                Place(i, out bx, out by);
                if (Middle && Acts[i] == LaneAct.Add) continue;
                s.Fill(bx, by, ButtonW, ButtonH, 12, over == Acts[i] ? Dock.Hover : Dock.Lift);
                s.Icon(Acts[i] == LaneAct.Add ? "plus" : "minus", bx + 12, by + 11, 18, Dock.Light);
                s.Text(Acts[i] == LaneAct.Add ? "Add lane" : "Remove", bx + 38, by, ButtonW - 44, ButtonH, 14, true, 0, Dock.Light);
            }
            if (Turns)
            {
                // The ways this lane may go at the light ahead: lit when it may, plain when it may not, faint when there is no road that way.
                s.Text("AT THE INTERSECTION AHEAD, THIS LANE MAY GO", x + 12, y + 80 + (CanMiddle ? 48 : 0) + (CanReverse ? 48 : 0), Wide - 24, 14, 9, true, 0, Dock.Dim);
                for (int i = 0; i < Ways.Length; i++)
                {
                    double bx, by;
                    PlaceWay(i, out bx, out by);
                    bool on = TurnThere[i] && TurnMay[i];
                    s.Fill(bx, by, ArrowW, ButtonH, 12, on ? Dock.Accent : over == Ways[i] ? Dock.Hover : Dock.Lift);
                    s.Icon(WayIcons[i], bx + (ArrowW - 22) / 2, by + 9, 22, on ? Dock.Ink : TurnThere[i] ? Dock.Light : 0x40E7EDF0);
                }
            }
            string tip = over == LaneAct.Add ? "One more lane on this side of the road."
                : over == LaneAct.Remove ? (Middle ? (Median ? "Take the left-turn lanes out, leaving the median." : "Take the turning lane out of the road.") : Added ? "Take this added lane away." : "Take a lane off this side of the road.")
                : over == LaneAct.Middle ? (Median ? "The median makes way for a left-turn lane at each intersection." : "A lane between the two directions to turn left from.")
                : over == LaneAct.Reverse ? "Turn it round: a one-way street runs the other way."
                : over == LaneAct.Left ? "Whether this lane may turn left."
                : over == LaneAct.Straight ? "Whether this lane may go straight on."
                : over == LaneAct.Right ? "Whether this lane may turn right."
                : "Or drag along a road to add a part lane.";
            s.Text(tip, x + 12, y + high - 28, Wide - 24, 18, 11, false, 0, Dock.Dim);
        }
    }
}
