using System;
using System.Collections.Generic;

namespace Traffic.Ui
{
    /// <summary>What the lane tool's card can do to the lane that is picked.</summary>
    public enum LaneAct { None, Add, Remove }

    /// <summary>
    /// The small card that sits beside a lane picked with the lane tool, with a button for each
    /// thing that can be done to it. Like the toolbar it lays itself out and draws onto an
    /// ISurface, and has no Unity references.
    /// </summary>
    public sealed class LaneCard
    {
        /// <summary>The picked lane is one added along part of its road: Remove takes just that away.</summary>
        public bool Added;

        private double x, y;
        private const double Wide = 276, High = 104, ButtonW = 124, ButtonH = 40;
        private static readonly LaneAct[] Acts = { LaneAct.Add, LaneAct.Remove };

        /// <summary>Place the card by the spot on the screen where the picked lane was clicked: above it, or below if there is no room.</summary>
        public void Layout(double anchorX, double anchorY, double screenW, double screenH)
        {
            x = Math.Max(8, Math.Min(screenW - Wide - 8, anchorX - Wide / 2));
            y = anchorY - High - 30;
            if (y < 8) y = Math.Min(screenH - High - 8, anchorY + 30);
        }

        private void Place(int i, out double bx, out double by)
        {
            bx = x + 10 + i * (ButtonW + 8);
            by = y + 30;
        }

        public bool Contains(double px, double py)
        {
            return px >= x && px < x + Wide && py >= y && py < y + High;
        }

        public LaneAct ItemAt(double px, double py)
        {
            for (int i = 0; i < Acts.Length; i++)
            {
                double bx, by;
                Place(i, out bx, out by);
                if (px >= bx && px < bx + ButtonW && py >= by && py < by + ButtonH) return Acts[i];
            }
            return LaneAct.None;
        }

        public void Draw(ISurface s, double mouseX, double mouseY)
        {
            LaneAct over = ItemAt(mouseX, mouseY);
            Dock.Card(s, x, y, Wide, High, 16);
            s.Text(Added ? "ADDED LANE" : "LANE", x + 12, y + 9, Wide - 24, 14, 10, true, 0, Dock.Dim);
            for (int i = 0; i < Acts.Length; i++)
            {
                double bx, by;
                Place(i, out bx, out by);
                s.Fill(bx, by, ButtonW, ButtonH, 12, over == Acts[i] ? Dock.Hover : Dock.Lift);
                s.Icon(Acts[i] == LaneAct.Add ? "plus" : "minus", bx + 12, by + 11, 18, Dock.Light);
                s.Text(Acts[i] == LaneAct.Add ? "Add lane" : "Remove", bx + 38, by, ButtonW - 44, ButtonH, 14, true, 0, Dock.Light);
            }
            string tip = over == LaneAct.Add ? "One more lane on this side of the road."
                : over == LaneAct.Remove ? (Added ? "Take this added lane away." : "Take a lane off this side of the road.")
                : "Or drag along a road to add a part lane.";
            s.Text(tip, x + 12, y + 76, Wide - 24, 18, 11, false, 0, Dock.Dim);
        }
    }
}
