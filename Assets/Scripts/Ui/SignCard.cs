using System;
using Traffic.Sim;

namespace Traffic.Ui
{
    /// <summary>What the signs tool's card can do.</summary>
    public enum SignAct { None, Control, Slower, Faster, UsualSpeed }

    /// <summary>
    /// The card that sits beside a lane picked with the signs tool. It sets what runs the
    /// intersection that lane leads to (a light, stop signs, a yield sign, or nothing) and the
    /// speed limit posted on the lane's road. Like the toolbar it lays itself out and draws
    /// onto an ISurface, and has no Unity references.
    /// </summary>
    public sealed class SignCard
    {
        /// <summary>The picked lane leads to an intersection that a light or signs can run.</summary>
        public bool Intersection;
        /// <summary>What runs that intersection now, as seen from the picked lane's road, and whether the game chose it.</summary>
        public Control Now;
        public bool Automatic;
        /// <summary>The speed limit on the picked lane's road, miles per hour, and whether the player posted it.</summary>
        public int Mph;
        public bool Posted;

        /// <summary>The choices for the intersection, in the order the card shows them, two to a row.</summary>
        public static readonly Control[] Choices =
        {
            Control.Light, Control.LightWithArrows,
            Control.AllWayStop, Control.NoSigns,
            Control.Stop, Control.Yield,
            Control.RightOfWay, Control.Automatic,
        };

        private double x, y, high;
        private const double Wide = 316, ButtonW = 144, ButtonH = 36, Gap = 8;

        public static string Name(Control control)
        {
            switch (control)
            {
                case Control.Light: return "Traffic light";
                case Control.LightWithArrows: return "Light, left arrows";
                case Control.AllWayStop: return "All-way stop";
                case Control.NoSigns: return "No signs";
                case Control.Stop: return "Stop sign here";
                case Control.Yield: return "Yield sign here";
                case Control.RightOfWay: return "Right of way";
                default: return "Automatic";
            }
        }

        private static string About(Control control)
        {
            switch (control)
            {
                case Control.Light: return "Each street gets a green in turn. A left turn waits for a gap in the traffic coming the other way. A right turn may be made on red after stopping, and so may a left from one one-way street into another.";
                case Control.LightWithArrows: return "As a traffic light, but left turns go on a green arrow of their own.";
                case Control.AllWayStop: return "Everyone stops. Whoever stopped first goes first.";
                case Control.NoSigns: return "Nothing at all. Whoever gets there first goes first, and of two who arrive together the one on the left gives way.";
                case Control.Stop: return "This street stops at the line, then waits for a gap in the cross street, which does not stop.";
                case Control.Yield: return "This street slows down, and stops only if something is coming on the cross street.";
                case Control.RightOfWay: return "This is the through street: the cross street gets the stop signs.";
                default: return "Let the game choose: stop signs all round where only 25 mph streets meet, and a light otherwise.";
            }
        }

        private double SpeedTop => y + 30 + (Intersection ? 4 * (ButtonH + Gap) + 30 : 0);

        /// <summary>Place the card by the spot on the screen where the picked lane was clicked: above it, or below if there is no room.</summary>
        public void Layout(double anchorX, double anchorY, double screenW, double screenH)
        {
            high = 30 + (Intersection ? 4 * (ButtonH + Gap) + 30 : 0) + 44 + 62;
            x = Math.Max(8, Math.Min(screenW - Wide - 8, anchorX - Wide / 2));
            y = anchorY - high - 30;
            if (y < 8) y = Math.Min(screenH - high - 8, anchorY + 30);
        }

        private void Place(int i, out double bx, out double by)
        {
            bx = x + 10 + (i % 2) * (ButtonW + Gap);
            by = y + 30 + (i / 2) * (ButtonH + Gap);
        }

        public bool Contains(double px, double py)
        {
            return px >= x && px < x + Wide && py >= y && py < y + high;
        }

        /// <summary>What is under a point of the screen. For SignAct.Control, which says which of the choices it is.</summary>
        public SignAct ItemAt(double px, double py, out Control which)
        {
            which = Control.Automatic;
            for (int i = 0; Intersection && i < Choices.Length; i++)
            {
                double bx, by;
                Place(i, out bx, out by);
                if (px < bx || px >= bx + ButtonW || py < by || py >= by + ButtonH) continue;
                which = Choices[i];
                return SignAct.Control;
            }
            double top = SpeedTop;
            if (py >= top && py < top + ButtonH)
            {
                if (px >= x + 10 && px < x + 10 + 44) return SignAct.Slower;
                if (px >= x + 10 + 44 + 116 && px < x + 10 + 44 + 116 + 44) return SignAct.Faster;
                if (Posted && px >= x + Wide - 10 - 84 && px < x + Wide - 10) return SignAct.UsualSpeed;
            }
            return SignAct.None;
        }

        public void Draw(ISurface s, double mouseX, double mouseY)
        {
            Control which;
            SignAct over = ItemAt(mouseX, mouseY, out which);
            Dock.Card(s, x, y, Wide, high, 16);
            if (Intersection)
            {
                s.Text("AT THE INTERSECTION AHEAD", x + 12, y + 9, Wide - 24, 14, 10, true, 0, Dock.Dim);
                for (int i = 0; i < Choices.Length; i++)
                {
                    double bx, by;
                    Place(i, out bx, out by);
                    // What runs it now is lit. Left to the game, "Automatic" is lit too, and the game's choice outlined.
                    bool on = Choices[i] == Control.Automatic ? Automatic : !Automatic && Choices[i] == Now;
                    bool hot = over == SignAct.Control && which == Choices[i];
                    s.Fill(bx, by, ButtonW, ButtonH, 11, on ? Dock.Accent : hot ? Dock.Hover : Dock.Lift);
                    if (Automatic && Choices[i] == Now) s.Stroke(bx, by, ButtonW, ButtonH, 11, 2, Dock.Accent);
                    s.Text(Name(Choices[i]), bx, by, ButtonW, ButtonH, 13, true, 1, on ? Dock.Ink : Dock.Light);
                }
            }
            double top = SpeedTop;
            s.Text("SPEED LIMIT ON THIS ROAD", x + 12, top - 21, Wide - 24, 14, 10, true, 0, Dock.Dim);
            s.Fill(x + 10, top, 44, ButtonH, 11, over == SignAct.Slower ? Dock.Hover : Dock.Lift);
            s.Icon("minus", x + 10 + 13, top + 9, 18, Dock.Light);
            s.Text(Mph + " mph", x + 54, top, 116, ButtonH, 17, true, 1, Posted ? Dock.Accent : Dock.Light);
            s.Fill(x + 10 + 44 + 116, top, 44, ButtonH, 11, over == SignAct.Faster ? Dock.Hover : Dock.Lift);
            s.Icon("plus", x + 10 + 44 + 116 + 13, top + 9, 18, Dock.Light);
            if (Posted)
            {
                s.Fill(x + Wide - 10 - 84, top, 84, ButtonH, 11, over == SignAct.UsualSpeed ? Dock.Hover : Dock.Lift);
                s.Text("Usual", x + Wide - 10 - 84, top, 84, ButtonH, 13, true, 1, Dock.Light);
            }
            string tip = over == SignAct.Control ? About(which)
                : over == SignAct.Slower || over == SignAct.Faster ? "Steps through the speed limits Ohio's law sets out, from 15 mph for an alley to 70 for a freeway in the country."
                : over == SignAct.UsualSpeed ? "Take the posted limit down: back to the usual one for this kind of road."
                : Posted && EditableMap.SpeedLimitFor(Mph).Length > 0 ? "Posted at " + Mph + " mph: the limit for " + EditableMap.SpeedLimitFor(Mph) + "."
                : "The usual limit for this kind of road.";
            s.Text(tip, x + 12, top + ButtonH + 6, Wide - 24, 52, 11, false, 3, Dock.Dim);
        }
    }
}
