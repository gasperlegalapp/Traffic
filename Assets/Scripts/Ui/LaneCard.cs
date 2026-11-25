using System;
using System.Collections.Generic;

namespace Traffic.Ui
{
    /// <summary>What the lane tool's card can do to the lane that is picked.</summary>
    public enum LaneAct { None, Add, Remove, Left, Straight, Right }

    /// <summary>
    /// The small card that sits beside a lane picked with the lane tool, with a button for each
    /// thing that can be done to it. Like the toolbar it lays itself out and draws onto an
    /// ISurface, and has no Unity references.
    /// </summary>
    public sealed class LaneCard
    {
        /// <summary>The picked lane is one added along part of its road: Remove takes just that away.</summary>
        public bool Added;
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
            high = Turns ? 172 : 104;
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
            by = y + 98;
        }

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
                if (px >= bx && px < bx + ButtonW && py >= by && py < by + ButtonH) return Acts[i];
            }
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
            s.Text(Added ? "ADDED LANE" : "LANE", x + 12, y + 9, Wide - 24, 14, 10, true, 0, Dock.Dim);
            for (int i = 0; i < Acts.Length; i++)
            {
                double bx, by;
                Place(i, out bx, out by);
                s.Fill(bx, by, ButtonW, ButtonH, 12, over == Acts[i] ? Dock.Hover : Dock.Lift);
                s.Icon(Acts[i] == LaneAct.Add ? "plus" : "minus", bx + 12, by + 11, 18, Dock.Light);
                s.Text(Acts[i] == LaneAct.Add ? "Add lane" : "Remove", bx + 38, by, ButtonW - 44, ButtonH, 14, true, 0, Dock.Light);
            }
            if (Turns)
            {
                // The ways this lane may go at the light ahead: lit when it may, plain when it may not, faint when there is no road that way.
                s.Text("AT THE INTERSECTION AHEAD, THIS LANE MAY GO", x + 12, y + 80, Wide - 24, 14, 9, true, 0, Dock.Dim);
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
                : over == LaneAct.Remove ? (Added ? "Take this added lane away." : "Take a lane off this side of the road.")
                : over == LaneAct.Left ? "Whether this lane may turn left."
                : over == LaneAct.Straight ? "Whether this lane may go straight on."
                : over == LaneAct.Right ? "Whether this lane may turn right."
                : "Or drag along a road to add a part lane.";
            s.Text(tip, x + 12, y + high - 28, Wide - 24, 18, 11, false, 0, Dock.Dim);
        }
    }
}
