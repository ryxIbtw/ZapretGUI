// Контурные значки 24×24 (как в макетах Figma). Хранятся строками SVG-путей и рисуются GDI+ в любом размере.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ZapretGui
{
    static class Icons
    {
        static readonly Dictionary<string, string> Data = new Dictionary<string, string>
        {
            { "play", "M8 5.5v13l10.5-6.5z" },
            { "chat", "M21 11.5a8.4 8.4 0 0 1-12.3 7.4L3 21l2.1-5.7A8.4 8.4 0 1 1 21 11.5z" },
            { "globe", "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0z M2 12h20 M12 2a15.3 15.3 0 0 1 4 10a15.3 15.3 0 0 1-4 10a15.3 15.3 0 0 1-4-10a15.3 15.3 0 0 1 4-10z" },
            { "palette", "M12 22a10 10 0 1 1 10-10c0 2.8-2.2 4-4 4h-2a2 2 0 0 0-1.5 3.3A1.6 1.6 0 0 1 12 22z M6.5 10.5a1 1 0 1 0 2 0a1 1 0 1 0-2 0z M11 7a1 1 0 1 0 2 0a1 1 0 1 0-2 0z M15.5 10.5a1 1 0 1 0 2 0a1 1 0 1 0-2 0z" },
            { "monitor", "M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M8 21h8 M12 17v4" },
            { "tray", "M22 12h-6l-2 3h-4l-2-3H2 M5.45 5.11L2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" },
            { "shield", "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" },
            { "wrench", "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z" },
            { "layers", "M12 2l10 5-10 5L2 7z M2 17l10 5 10-5 M2 12l10 5 10-5" },
            { "download", "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3" },
            { "chevronLeft", "M15 18l-6-6 6-6" },
            { "chevronRight", "M9 18l6-6-6-6" },
            { "chevronDown", "M6 9l6 6 6-6" },
            { "power", "M12 3v8 M6.3 6.8a8 8 0 1 0 11.4 0" },
            { "gear", "M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" },
            { "moon", "M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" },
            { "sun", "M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8z M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41 M2 12h2 M20 12h2 M6.34 17.66l-1.41 1.41 M19.07 4.93l-1.41 1.41" },
            { "check", "M20 6L9 17l-5-5" },
            { "x", "M18 6L6 18 M6 6l12 12" },
            { "alert", "M12 9v4 M12 17h.01 M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" },
            { "info", "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20z M12 16v-4 M12 8h.01" },
            { "help", "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20z M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3 M12 17h.01" },
            { "search", "M11 3a8 8 0 1 0 0 16a8 8 0 1 0 0-16z M21 21l-4.35-4.35" },
            { "list", "M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01" },
        };
        static readonly HashSet<string> Filled = new HashSet<string> { "play" };

        // Для WPF: те же SVG-пути рисуются через Geometry.Parse.
        public static string PathData(string name) { string d; return name != null && Data.TryGetValue(name, out d) ? d : null; }
        public static bool IsFilled(string name) { return name != null && Filled.Contains(name); }
        static readonly Dictionary<string, GraphicsPath> Cache = new Dictionary<string, GraphicsPath>();

        // Рисует значок name в квадрате r цветом c (толщина линии — как у SVG stroke-width=2 на 24 px).
        public static void Draw(Graphics g, string name, RectangleF r, Color c)
        {
            GraphicsPath src = Get(name);
            if (src == null) return;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Math.Min(r.Width, r.Height) / 24f;
            using (var p = (GraphicsPath)src.Clone())
            using (var m = new Matrix())
            {
                m.Translate(r.X, r.Y);
                m.Scale(k, k);
                p.Transform(m);
                if (Filled.Contains(name))
                {
                    using (var b = new SolidBrush(c)) g.FillPath(b, p);
                    using (var pen = new Pen(c, 2f * k) { LineJoin = LineJoin.Round }) g.DrawPath(pen, p);
                }
                else
                    using (var pen = new Pen(c, 2f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawPath(pen, p);
            }
            g.Restore(st);
        }

        static GraphicsPath Get(string name)
        {
            GraphicsPath p;
            if (name == null) return null;
            if (Cache.TryGetValue(name, out p)) return p;
            string d;
            if (!Data.TryGetValue(name, out d)) return null;
            p = Parse(d);
            Cache[name] = p;
            return p;
        }

        // Разбор SVG path: M L H V C S A Z (и строчные — относительные). Достаточно для значков выше.
        static GraphicsPath Parse(string d)
        {
            var path = new GraphicsPath();
            var tokens = Regex.Matches(d, @"[MmLlHhVvCcSsAaZz]|-?\d*\.?\d+(?:e-?\d+)?");
            int i = 0;
            char cmd = 'M';
            PointF cur = PointF.Empty, start = PointF.Empty, lastCtrl = PointF.Empty;
            Func<float> num = () => float.Parse(tokens[i++].Value, CultureInfo.InvariantCulture);
            Func<bool> hasNum = () => i < tokens.Count && !char.IsLetter(tokens[i].Value[0]);
            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i].Value[0])) cmd = tokens[i++].Value[0];
                bool rel = char.IsLower(cmd);
                char C = char.ToUpperInvariant(cmd);
                if (C == 'Z')
                {
                    path.CloseFigure(); cur = start;
                    continue;
                }
                if (!hasNum()) break;
                switch (C)
                {
                    case 'M':
                        {
                            float x = num(), y = num();
                            cur = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            path.StartFigure(); start = cur;
                            cmd = rel ? 'l' : 'L';   // следующие пары чисел — это линии
                            break;
                        }
                    case 'L':
                        {
                            float x = num(), y = num();
                            var p = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            path.AddLine(cur, p); cur = p;
                            break;
                        }
                    case 'H':
                        {
                            float x = num(); var p = new PointF(rel ? cur.X + x : x, cur.Y);
                            path.AddLine(cur, p); cur = p; break;
                        }
                    case 'V':
                        {
                            float y = num(); var p = new PointF(cur.X, rel ? cur.Y + y : y);
                            path.AddLine(cur, p); cur = p; break;
                        }
                    case 'C':
                    case 'S':
                        {
                            PointF c1;
                            if (C == 'C') { float x1 = num(), y1 = num(); c1 = rel ? new PointF(cur.X + x1, cur.Y + y1) : new PointF(x1, y1); }
                            else c1 = new PointF(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y);
                            float x2 = num(), y2 = num(), x = num(), y = num();
                            var c2 = rel ? new PointF(cur.X + x2, cur.Y + y2) : new PointF(x2, y2);
                            var p = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            path.AddBezier(cur, c1, c2, p); lastCtrl = c2; cur = p;
                            break;
                        }
                    case 'A':
                        {
                            float rx = num(), ry = num(); num();   // поворот эллипса не используется
                            bool large = num() != 0, sweep = num() != 0;
                            float x = num(), y = num();
                            var p = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            AddArc(path, cur, p, rx, ry, large, sweep);
                            cur = p;
                            break;
                        }
                    default: i++; break;
                }
                if (C != 'C' && C != 'S') lastCtrl = cur;
            }
            return path;
        }

        // Дуга SVG (окружность или эллипс без поворота) → GraphicsPath.AddArc.
        static void AddArc(GraphicsPath path, PointF p1, PointF p2, float rx, float ry, bool large, bool sweep)
        {
            if (rx == 0 || ry == 0 || (p1.X == p2.X && p1.Y == p2.Y)) { path.AddLine(p1, p2); return; }
            double x1p = (p1.X - p2.X) / 2.0, y1p = (p1.Y - p2.Y) / 2.0;
            double rxs = rx * rx, rys = ry * ry;
            double lam = x1p * x1p / rxs + y1p * y1p / rys;
            if (lam > 1) { double s = Math.Sqrt(lam); rx = (float)(rx * s); ry = (float)(ry * s); rxs = rx * rx; rys = ry * ry; }
            double num = rxs * rys - rxs * y1p * y1p - rys * x1p * x1p;
            double den = rxs * y1p * y1p + rys * x1p * x1p;
            double co = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
            double cxp = co * rx * y1p / ry, cyp = -co * ry * x1p / rx;
            double cx = cxp + (p1.X + p2.X) / 2.0, cy = cyp + (p1.Y + p2.Y) / 2.0;
            double t1 = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
            double t2 = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
            double dt = t2 - t1;
            if (sweep && dt < 0) dt += 2 * Math.PI;
            else if (!sweep && dt > 0) dt -= 2 * Math.PI;
            path.AddArc((float)(cx - rx), (float)(cy - ry), rx * 2, ry * 2, (float)(t1 * 180 / Math.PI), (float)(dt * 180 / Math.PI));
        }
    }
}
