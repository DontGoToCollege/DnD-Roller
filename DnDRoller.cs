// D&D Dice Roller: a portable, offline dice roller for Windows.
// Built by Build-DnDRoller.ps1 with the C# 5 compiler that ships with Windows, so it sticks to C# 5 syntax.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("D&D Dice Roller")]
[assembly: System.Reflection.AssemblyProduct("D&D Dice Roller")]

public static class App
{
    public const string Version = "2.0.2";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}

// ---------------------------------------------------------------- dice engine

public class DiceException : Exception { public DiceException(string message) : base(message) { } }

public class RollResult
{
    public long Total;
    public readonly List<string> Parts = new List<string>();
    public int Natural;     // kept face of the first single-d20 roll (1d20, advantage, 2d20kh1...), 0 if none
    public bool AdvUsed;
}

public static class Dice
{
    public const int MaxDice = 100, MaxSides = 1000;
    const long MaxNumber = 1000000;

    // adv: 0 normal, 1 advantage, 2 disadvantage. It applies once, to the first plain 1d20 in the roll.
    public static RollResult Roll(string expression, Random rng, int adv)
    {
        return new Parser(expression, rng, adv).Run();
    }

    // op: 0 none, 1 add, 2 subtract, 3 multiply
    public static long ApplyEach(long value, int op, long operand)
    {
        checked
        {
            switch (op)
            {
                case 1: return value + operand;
                case 2: return value - operand;
                case 3: return value * operand;
                default: return value;
            }
        }
    }

    class Parser
    {
        readonly string s;
        readonly Random rng;
        readonly int adv;
        readonly RollResult r = new RollResult();
        int pos;

        public Parser(string text, Random rng, int adv)
        {
            s = (text ?? "").ToLowerInvariant().Replace(" ", "")
                .Replace("×", "*").Replace("÷", "/").Replace("−", "-");
            this.rng = rng;
            this.adv = adv;
        }

        char Peek { get { return pos < s.Length ? s[pos] : '\0'; } }
        bool PeekDigit { get { return Peek >= '0' && Peek <= '9'; } }

        public RollResult Run()
        {
            if (s.Length == 0) throw new DiceException("Type a roll first, like 2d6+3.");
            try { r.Total = Expr(); }
            catch (OverflowException) { throw new DiceException("That number is too big."); }
            if (pos < s.Length)
                throw new DiceException("\"" + s.Substring(pos) + "\" isn't part of a roll. Try something like 2d6+3.");
            return r;
        }

        long Expr()
        {
            long v = Term();
            while (Peek == '+' || Peek == '-')
            {
                char op = s[pos++];
                long t = Term();
                v = checked(op == '+' ? v + t : v - t);
            }
            return v;
        }

        long Term()
        {
            long v = Unary();
            while (Peek == '*' || Peek == 'x' || Peek == '/')
            {
                char op = s[pos++];
                long t = Unary();
                if (op != '/') { v = checked(v * t); continue; }
                if (t == 0) throw new DiceException("You can't divide by zero.");
                long q = v / t;
                if (v % t != 0 && (v < 0) != (t < 0)) q--;   // D&D rounds down
                v = q;
            }
            return v;
        }

        long Unary()
        {
            if (Peek == '-') { pos++; return checked(-Unary()); }
            if (Peek == '+') { pos++; return Unary(); }
            return Primary();
        }

        long Primary()
        {
            if (Peek == '(')
            {
                pos++;
                long v = Expr();
                if (Peek != ')') throw new DiceException("A bracket is missing its closing \")\".");
                pos++;
                return v;
            }
            long n = PeekDigit ? Number() : -1;
            if (Peek == 'd') { pos++; return DiceTerm(n < 0 ? 1 : n); }
            if (n < 0) throw new DiceException(pos < s.Length ? "\"" + Peek + "\" doesn't belong there." : "The roll ends too early.");
            return n;
        }

        long Number()
        {
            int start = pos;
            while (PeekDigit) pos++;
            long v;
            if (!long.TryParse(s.Substring(start, pos - start), NumberStyles.None, CultureInfo.InvariantCulture, out v) || v > MaxNumber)
                throw new DiceException("Numbers can go up to " + MaxNumber.ToString("N0") + ".");
            return v;
        }

        long DiceTerm(long count)
        {
            long sides;
            if (Peek == '%') { pos++; sides = 100; }
            else if (PeekDigit) sides = Number();
            else throw new DiceException("Add a die size after the d, like d6.");
            if (count < 1 || count > MaxDice) throw new DiceException("Roll 1 to " + MaxDice + " dice at a time.");
            if (sides < 2 || sides > MaxSides) throw new DiceException("Dice can have 2 to " + MaxSides + " sides.");

            // keep / drop: kh, kl, dh, dl (k alone = kh, d+number = dl)
            string tag = null;
            if (pos + 1 < s.Length)
            {
                string two = s.Substring(pos, 2);
                if (two == "kh" || two == "kl" || two == "dh" || two == "dl") { tag = two; pos += 2; }
            }
            if (tag == null && Peek == 'k') { tag = "kh"; pos++; }
            else if (tag == null && Peek == 'd' && pos + 1 < s.Length && s[pos + 1] >= '0' && s[pos + 1] <= '9') { tag = "dl"; pos++; }
            long k = 0;
            if (tag != null)
            {
                k = PeekDigit ? Number() : 1;
                bool keep = tag[0] == 'k';
                if (k > count || (keep && k < 1))
                    throw new DiceException(keep ? "You can keep 1 to " + count + " of those dice." : "You can drop up to " + count + " of those dice.");
            }

            int n = (int)count;
            var rolls = new List<int>();
            for (int i = 0; i < n; i++) rolls.Add(rng.Next(1, (int)sides + 1));
            string label = count + "d" + sides;

            if (tag == null && n == 1 && sides == 20 && adv != 0 && !r.AdvUsed)
            {
                r.AdvUsed = true;
                int other = rng.Next(1, 21);
                int pick = adv == 1 ? Math.Max(rolls[0], other) : Math.Min(rolls[0], other);
                r.Parts.Add("1d20 " + (adv == 1 ? "advantage" : "disadvantage") + " [" + rolls[0] + ", " + other + "] → " + pick);
                if (r.Natural == 0) r.Natural = pick;
                return pick;
            }

            int keepCount = tag == null ? n : tag[0] == 'k' ? (int)k : n - (int)k;
            bool keepHigh = tag == "kh" || tag == "dl";
            var order = Enumerable.Range(0, n).OrderBy(i => rolls[i]).ToList();
            var kept = new bool[n];
            foreach (int i in keepHigh ? order.Skip(n - keepCount) : order.Take(keepCount)) kept[i] = true;
            long sum = 0;
            var shown = new List<string>();
            for (int i = 0; i < n; i++)
            {
                if (kept[i]) sum += rolls[i];
                shown.Add(kept[i] ? rolls[i].ToString() : "(" + rolls[i] + ")");
            }
            r.Parts.Add(label + (tag != null ? tag + k : "") + " [" + string.Join(", ", shown) + "] = " + sum);
            if (sides == 20 && keepCount == 1 && r.Natural == 0) r.Natural = (int)sum;
            return sum;
        }
    }
}

// ---------------------------------------------------------------- storage

public class SavedRoll
{
    public string Name, Expr;
    public int Times = 1, Op, OpValue = 1, Adv;

    public string ToLine()
    {
        return string.Join("\t", new[] { Store.Clean(Name), Store.Clean(Expr), Times.ToString(), Op.ToString(), OpValue.ToString(), Adv.ToString() });
    }

    public static SavedRoll Parse(string line)
    {
        var f = line.Split('\t');
        if (f.Length < 2 || f[0].Length == 0 || f[1].Length == 0) return null;
        var s = new SavedRoll { Name = f[0], Expr = f[1] };
        if (f.Length >= 6)
        {
            int.TryParse(f[2], out s.Times); int.TryParse(f[3], out s.Op); int.TryParse(f[4], out s.OpValue); int.TryParse(f[5], out s.Adv);
        }
        s.Times = Math.Max(1, Math.Min(100, s.Times));
        s.Op = Math.Max(0, Math.Min(3, s.Op));
        s.OpValue = Math.Max(0, Math.Min(1000, s.OpValue));
        s.Adv = Math.Max(0, Math.Min(2, s.Adv));
        return s;
    }
}

public class HistoryEntry
{
    public DateTime Time;
    public string Title, Value, Details;
    public int Flag;   // 1 natural 20, 2 natural 1

    public string ToLine()
    {
        return string.Join("\t", new[] { Time.ToString("o", CultureInfo.InvariantCulture), Store.Clean(Title), Store.Clean(Value), Store.Clean(Details), Flag.ToString() });
    }

    public static HistoryEntry Parse(string line)
    {
        var f = line.Split('\t');
        DateTime t;
        if (f.Length < 5 || !DateTime.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) return null;
        var h = new HistoryEntry { Time = t, Title = f[1], Value = f[2], Details = f[3] };
        int.TryParse(f[4], out h.Flag);
        return h;
    }

    public override string ToString()
    {
        return Time.ToString("yyyy-MM-dd HH:mm:ss") + "  " + Title + " = " + Value + "    " + Details;
    }
}

public static class Store
{
    public const string SavedFile = "DnDRoller-saved.txt", HistoryFile = "DnDRoller-history.txt";
    static string dir;

    // Next to the exe so it stays portable; falls back to AppData when that folder is read-only.
    public static string Dir
    {
        get
        {
            if (dir != null) return dir;
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            try
            {
                string probe = Path.Combine(exeDir, ".dnd-roller-write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                dir = exeDir;
            }
            catch
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DnD-Roller");
                Directory.CreateDirectory(dir);
            }
            return dir;
        }
    }

    public static string[] Read(string name)
    {
        try
        {
            string p = Path.Combine(Dir, name);
            return File.Exists(p) ? File.ReadAllLines(p, Encoding.UTF8) : new string[0];
        }
        catch { return new string[0]; }
    }

    public static bool Write(string name, IEnumerable<string> lines)
    {
        try
        {
            string p = Path.Combine(Dir, name), tmp = p + ".tmp";
            File.WriteAllLines(tmp, lines, Encoding.UTF8);
            if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
            return true;
        }
        catch { return false; }
    }

    public static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
}

// ---------------------------------------------------------------- theme

public static class T
{
    public static float S = 1f;
    public static int P(float v) { return (int)Math.Round(v * S); }

    public static readonly Color
        Bg = Rgb(0x121214), Card = Rgb(0x1C1C1E), Fill = Rgb(0x2C2C2E), FillHover = Rgb(0x3A3A3C), FillPress = Rgb(0x48484A),
        Thumb = Rgb(0x5A5A5E), Text = Rgb(0xF5F5F7), Text2 = Rgb(0x98989F), Text3 = Rgb(0x636368),
        Accent = Rgb(0xFF9F0A), AccentHover = Rgb(0xFFB133), AccentPress = Rgb(0xE08A00), OnAccent = Rgb(0x1E1404),
        Green = Rgb(0x30D158), Red = Rgb(0xFF453A);

    public static Font Title, Body, BodyStrong, Caption, Small, SmallStrong, Mono, Value, Symbol;
    public static string DisplayFamily;

    public static void Init(float scale)
    {
        S = scale;
        string text = Has("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        string semi = Has("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold" : "Segoe UI Semibold";
        DisplayFamily = Has("Segoe UI Variable Display Semib") ? "Segoe UI Variable Display Semib" : "Segoe UI Semibold";
        string mono = Has("Cascadia Mono") ? "Cascadia Mono" : "Consolas";
        Title = new Font(DisplayFamily, 20f);
        Body = new Font(text, 10f);
        BodyStrong = new Font(semi, 10f);
        Caption = new Font(semi, 8f);
        Small = new Font(text, 8.5f);
        SmallStrong = new Font(semi, 8.5f);
        Mono = new Font(mono, 13f);
        Value = new Font(DisplayFamily, 14f);
        Symbol = new Font(semi, 14f);
    }

    static bool Has(string family)
    {
        foreach (var f in FontFamily.Families) if (f.Name == family) return true;
        return false;
    }

    static Color Rgb(int hex) { return Color.FromArgb((hex >> 16) & 255, (hex >> 8) & 255, hex & 255); }
    public static Color Alpha(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
        if (rad < 1f) { p.AddRectangle(r); return p; }
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
    {
        using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
    }

    public static void StrokeRound(Graphics g, Color c, float w, RectangleF r, float rad)
    {
        using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
    }

    public static void Ring(Graphics g, Rectangle bounds, float rad)
    {
        float w = P(2);
        var r = new RectangleF(bounds.X + w / 2, bounds.Y + w / 2, bounds.Width - w, bounds.Height - w);
        StrokeRound(g, Alpha(Accent, 200), w, r, rad - w / 2);
    }

    public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
    public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;

    public static void Draw(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
    {
        TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    public static int Measure(string s, Font f)
    {
        return TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
    }

    public static string Num(long v) { return (v < 0 ? "−" : "") + Math.Abs((decimal)v).ToString("N0"); }
}

static class Native
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, string l);

    public static void DarkTitle(Form f, Color caption)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(f.Handle, 20, ref on, 4);                       // dark title bar (Windows 10 20H1+)
            int c = caption.R | (caption.G << 8) | (caption.B << 16);
            DwmSetWindowAttribute(f.Handle, 35, ref c, 4);                        // caption colour (Windows 11)
        }
        catch { }
    }

    public static void DarkScrollbars(Control c) { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
    public static void Cue(TextBox t, string text) { try { SendMessage(t.Handle, 0x1501, (IntPtr)1, text); } catch { } }
}

// ---------------------------------------------------------------- controls

public class Card : Panel
{
    public string Title;
    public readonly List<Tuple<Rectangle, string>> Labels = new List<Tuple<Rectangle, string>>();

    public Card(string title)
    {
        Title = title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = T.Card;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : T.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        T.FillRound(g, T.Card, new RectangleF(0, 0, Width, Height), T.P(16));
        T.StrokeRound(g, Color.FromArgb(14, 255, 255, 255), 1f, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), T.P(16));
        if (!string.IsNullOrEmpty(Title))
            T.Draw(g, Title.ToUpperInvariant(), T.Caption, new Rectangle(T.P(18), T.P(12), Width - T.P(36), T.P(18)), T.Text2, T.Left);
        foreach (var l in Labels) T.Draw(g, l.Item2, T.Body, l.Item1, T.Text2, T.Left);
    }
}

public class Surface : Control
{
    protected bool Hot, Down;

    public Surface()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
    }

    protected Color Back { get { return Parent != null ? Parent.BackColor : T.Bg; } }
    protected bool ShowRing { get { return Focused && ShowFocusCues; } }

    protected Graphics Begin(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        return g;
    }

    protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hot = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}

public enum Look { Primary, Secondary, Plain, Tile }

public class PillButton : Surface
{
    public Look Look;
    public int Sides;          // die tiles draw an icon for this die
    public bool Closable;      // saved-roll chips show an x that raises CloseClicked
    public event EventHandler CloseClicked;
    bool closeDown;

    public PillButton(string text, Look look)
    {
        Text = text;
        Look = look;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = text;
        Cursor = Cursors.Hand;
    }

    public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

    Rectangle CloseRect { get { int w = T.P(28); return new Rectangle(Width - w - T.P(2), 0, w, Height); } }
    Font TextFont { get { return Look == Look.Secondary && Closable ? T.Body : T.BodyStrong; } }

    public int PreferredWidth { get { return T.Measure(Text, TextFont) + T.P(Closable ? 44 : 36); } }

    protected override void OnMouseDown(MouseEventArgs e) { closeDown = Closable && CloseRect.Contains(e.Location); base.OnMouseDown(e); }

    protected override void OnClick(EventArgs e)
    {
        if (closeDown) { closeDown = false; if (CloseClicked != null) CloseClicked(this, EventArgs.Empty); return; }
        base.OnClick(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) PerformClick();
        else if (e.KeyCode == Keys.Delete && Closable && CloseClicked != null) CloseClicked(this, EventArgs.Empty);
        base.OnKeyUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e) { if (Closable) Invalidate(); base.OnMouseMove(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float rad = Look == Look.Tile ? T.P(12) : Height / 2f;
        Color fill, fore;
        switch (Look)
        {
            case Look.Primary: fill = Down ? T.AccentPress : Hot ? T.AccentHover : T.Accent; fore = T.OnAccent; break;
            case Look.Plain: fill = Down ? T.FillHover : Hot ? T.Fill : Back; fore = T.Accent; break;
            default: fill = Down ? T.FillPress : Hot ? T.FillHover : T.Fill; fore = T.Text; break;
        }
        if (!Enabled) { fill = Look == Look.Plain ? Back : T.Fill; fore = T.Text3; }
        T.FillRound(g, fill, new RectangleF(0, 0, Width, Height), rad);
        if (Look == Look.Primary && Enabled)
            T.StrokeRound(g, Color.FromArgb(50, 255, 255, 255), 1f, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), rad);

        if (Look == Look.Tile)
        {
            float icon = T.P(24);
            DieIcon.Draw(g, Sides, new PointF(Width / 2f, T.P(12) + icon / 2f), icon, Hot ? T.Accent : T.Text2, T.P(1.6f));
            T.Draw(g, Text, T.BodyStrong, new Rectangle(0, Height - T.P(28), Width, T.P(20)), fore, T.Center);
        }
        else if (Closable)
        {
            var tr = new Rectangle(T.P(16), 0, CloseRect.Left - T.P(16), Height);
            T.Draw(g, Text, TextFont, tr, fore, T.Left | TextFormatFlags.EndEllipsis);
            var cr = CloseRect;
            bool closeHot = Hot && cr.Contains(PointToClient(MousePosition));
            float cx = cr.X + cr.Width / 2f, cy = Height / 2f, a = T.P(3.5f);
            if (closeHot) using (var b = new SolidBrush(T.FillPress)) g.FillEllipse(b, cx - T.P(10), cy - T.P(10), T.P(20), T.P(20));
            using (var pen = new Pen(closeHot ? T.Text : T.Text2, T.P(1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, cx - a, cy - a, cx + a, cy + a);
                g.DrawLine(pen, cx + a, cy - a, cx - a, cy + a);
            }
        }
        else T.Draw(g, Text, TextFont, ClientRectangle, fore, T.Center | TextFormatFlags.EndEllipsis);

        if (ShowRing) T.Ring(g, ClientRectangle, rad);
    }
}

static class DieIcon
{
    static PointF[] Poly(PointF c, float r, int n, float startDeg)
    {
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            double a = (startDeg + 360.0 * i / n) * Math.PI / 180;
            pts[i] = new PointF(c.X + r * (float)Math.Cos(a), c.Y + r * (float)Math.Sin(a));
        }
        return pts;
    }

    public static void Draw(Graphics g, int sides, PointF c, float size, Color color, float w)
    {
        float r = size / 2f;
        using (var pen = new Pen(color, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            switch (sides)
            {
                case 4:
                    var tri = Poly(new PointF(c.X, c.Y + r * 0.15f), r * 1.05f, 3, -90);
                    g.DrawPolygon(pen, tri);
                    g.DrawLine(pen, tri[0], new PointF(c.X, tri[1].Y));
                    break;
                case 6:
                    using (var p = T.Round(new RectangleF(c.X - r * 0.8f, c.Y - r * 0.8f, r * 1.6f, r * 1.6f), r * 0.3f)) g.DrawPath(pen, p);
                    using (var b = new SolidBrush(color)) g.FillEllipse(b, c.X - w, c.Y - w, w * 2, w * 2);
                    break;
                case 8:
                    var dia = Poly(c, r, 4, -90);
                    g.DrawPolygon(pen, dia);
                    g.DrawLine(pen, dia[3], dia[1]);
                    break;
                case 10:
                    var kite = new[] { new PointF(c.X, c.Y - r), new PointF(c.X + r * 0.9f, c.Y + r * 0.05f), new PointF(c.X, c.Y + r), new PointF(c.X - r * 0.9f, c.Y + r * 0.05f) };
                    g.DrawPolygon(pen, kite);
                    g.DrawLine(pen, kite[0], new PointF(c.X, c.Y + r * 0.35f));
                    g.DrawLine(pen, kite[1], new PointF(c.X, c.Y + r * 0.35f));
                    g.DrawLine(pen, kite[3], new PointF(c.X, c.Y + r * 0.35f));
                    break;
                case 12:
                    g.DrawPolygon(pen, Poly(c, r, 5, -90));
                    g.DrawPolygon(pen, Poly(c, r * 0.45f, 5, 90));
                    break;
                case 20:
                    var hex = Poly(c, r, 6, -90);
                    var inner = Poly(new PointF(c.X, c.Y + r * 0.1f), r * 0.55f, 3, -90);
                    g.DrawPolygon(pen, hex);
                    g.DrawPolygon(pen, inner);
                    g.DrawLine(pen, inner[0], hex[0]);
                    g.DrawLine(pen, inner[1], hex[2]);
                    g.DrawLine(pen, inner[2], hex[4]);
                    break;
                default:
                    g.DrawEllipse(pen, c.X - r * 0.9f, c.Y - r * 0.9f, r * 1.8f, r * 1.8f);
                    g.DrawEllipse(pen, c.X - r * 0.4f, c.Y - r * 0.4f, r * 0.8f, r * 0.8f);
                    break;
            }
        }
    }
}

public class Stepper : Surface
{
    int val, hotPart, repeatDir;
    public int Min, Max;
    public bool Signed;
    public event EventHandler ValueChanged;
    readonly Timer repeat = new Timer();
    readonly Timer typeReset = new Timer { Interval = 1500 };
    string typed = "";

    public Stepper(string name, int min, int max, int value)
    {
        AccessibleName = name;
        AccessibleRole = AccessibleRole.SpinButton;
        Min = min; Max = max; val = value;
        repeat.Tick += delegate { repeat.Interval = 55; Value = val + repeatDir; };
        typeReset.Tick += delegate { typeReset.Stop(); typed = ""; Invalidate(); };
    }

    public int Value
    {
        get { return val; }
        set
        {
            int v = Math.Max(Min, Math.Min(Max, value));
            if (v == val) return;
            val = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    Rectangle MinusRect { get { return new Rectangle(0, 0, Height + T.P(6), Height); } }
    Rectangle PlusRect { get { return new Rectangle(Width - Height - T.P(6), 0, Height + T.P(6), Height); } }
    string Format(int v) { return v < 0 ? "−" + (-v) : Signed && v > 0 ? "+" + v : v.ToString(); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int part = MinusRect.Contains(e.Location) ? -1 : PlusRect.Contains(e.Location) ? 1 : 0;
        if (part != hotPart) { hotPart = part; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        typed = "";
        if (e.Button != MouseButtons.Left || hotPart == 0) return;
        repeatDir = hotPart;
        Value = val + repeatDir;
        repeat.Interval = 400;
        repeat.Start();
    }

    protected override void OnMouseUp(MouseEventArgs e) { repeat.Stop(); base.OnMouseUp(e); }
    protected override void OnMouseLeave(EventArgs e) { repeat.Stop(); hotPart = 0; base.OnMouseLeave(e); }
    protected override void OnLostFocus(EventArgs e) { repeat.Stop(); typed = ""; base.OnLostFocus(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Value = val + (e.Delta > 0 ? 1 : -1); base.OnMouseWheel(e); }

    protected override bool IsInputKey(Keys k)
    {
        return k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up: case Keys.Right: case Keys.Add: case Keys.Oemplus: typed = ""; Value = val + 1; break;
            case Keys.Down: case Keys.Left: typed = ""; Value = val - 1; break;
            case Keys.PageUp: typed = ""; Value = val + 10; break;
            case Keys.PageDown: typed = ""; Value = val - 10; break;
            case Keys.Subtract: case Keys.OemMinus:
                if (Min < 0 && typed.Length == 0) { typed = "-"; typeReset.Stop(); typeReset.Start(); Invalidate(); }
                else { typed = ""; Value = val - 1; }
                break;
            case Keys.Back:
                if (typed.Length > 0) { typed = typed.Substring(0, typed.Length - 1); ApplyTyped(); }
                break;
            case Keys.Escape: typed = ""; Invalidate(); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (e.KeyChar >= '0' && e.KeyChar <= '9' && typed.Length < 6) { typed += e.KeyChar; ApplyTyped(); e.Handled = true; }
        base.OnKeyPress(e);
    }

    void ApplyTyped()
    {
        int v;
        if (int.TryParse(typed, out v)) Value = v;
        typeReset.Stop(); typeReset.Start();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float rad = Height / 2f;
        using (var path = T.Round(new RectangleF(0, 0, Width, Height), rad))
        {
            using (var b = new SolidBrush(T.Fill)) g.FillPath(b, path);
            if (Enabled && hotPart != 0)
            {
                g.SetClip(path);
                using (var b = new SolidBrush(Down ? T.FillPress : T.FillHover)) g.FillRectangle(b, hotPart < 0 ? MinusRect : PlusRect);
                g.ResetClip();
            }
        }
        float arm = T.P(5);
        var m = MinusRect; var p = PlusRect;
        float my = Height / 2f, mx = m.X + m.Width / 2f + T.P(2), px = p.X + p.Width / 2f - T.P(2);
        using (var pen = new Pen(Enabled && val > Min ? T.Text : T.Text3, T.P(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(pen, mx - arm, my, mx + arm, my);
        using (var pen = new Pen(Enabled && val < Max ? T.Text : T.Text3, T.P(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(pen, px - arm, my, px + arm, my);
            g.DrawLine(pen, px, my - arm, px, my + arm);
        }
        var tr = new Rectangle(m.Right, 0, p.Left - m.Right, Height);
        string shown = typed.Length > 0 ? typed.Replace("-", "−") : Format(val);
        T.Draw(g, shown, T.BodyStrong, tr, !Enabled ? T.Text3 : typed.Length > 0 ? T.Accent : T.Text, T.Center);
        if (ShowRing) T.Ring(g, ClientRectangle, rad);
    }

    protected override AccessibleObject CreateAccessibilityInstance() { return new Acc(this); }

    class Acc : ControlAccessibleObject
    {
        public Acc(Stepper s) : base(s) { }
        public override string Value { get { return ((Stepper)Owner).Value.ToString(); } }
    }
}

public class Segmented : Surface
{
    public readonly string[] Items;
    public event EventHandler Changed;
    int sel;
    float thumb;
    readonly Timer anim = new Timer { Interval = 15 };

    public Segmented(string name, params string[] items)
    {
        Items = items;
        AccessibleName = name;
        AccessibleRole = AccessibleRole.ComboBox;
        anim.Tick += delegate
        {
            thumb += (sel - thumb) * 0.3f;
            if (Math.Abs(sel - thumb) < 0.01f) { thumb = sel; anim.Stop(); }
            Invalidate();
        };
    }

    public int SelectedIndex
    {
        get { return sel; }
        set
        {
            int v = Math.Max(0, Math.Min(Items.Length - 1, value));
            if (v == sel) return;
            sel = v;
            if (Visible && IsHandleCreated) anim.Start(); else thumb = sel;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
    }

    float Inset { get { return T.P(2); } }
    float SegW { get { return (Width - 2 * Inset) / Items.Length; } }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left) SelectedIndex = (int)((e.X - Inset) / SegW);
    }

    protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) { SelectedIndex = sel - 1; e.Handled = true; }
        else if (e.KeyCode == Keys.Right) { SelectedIndex = sel + 1; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float rad = T.P(9), w = SegW, inset = Inset;
        T.FillRound(g, T.Fill, new RectangleF(0, 0, Width, Height), rad);
        using (var sep = new Pen(Color.FromArgb(30, 255, 255, 255), 1f))
            for (int i = 1; i < Items.Length; i++)
                if (Math.Abs(i - (thumb + 0.5f)) > 0.9f)
                    g.DrawLine(sep, inset + w * i, Height * 0.28f, inset + w * i, Height * 0.72f);
        if (Enabled)
        {
            var tr = new RectangleF(inset + thumb * w, inset, w, Height - 2 * inset);
            T.FillRound(g, Color.FromArgb(70, 0, 0, 0), new RectangleF(tr.X, tr.Y + 1, tr.Width, tr.Height), rad - inset);
            T.FillRound(g, T.Thumb, tr, rad - inset);
        }
        for (int i = 0; i < Items.Length; i++)
        {
            var r = new Rectangle((int)(inset + w * i), 0, (int)w, Height);
            T.Draw(g, Items[i], Items[i].Length == 1 ? T.Symbol : i == sel ? T.BodyStrong : T.Body, r, !Enabled ? T.Text3 : i == sel ? T.Text : T.Text2, T.Center | TextFormatFlags.EndEllipsis);
        }
        if (ShowRing) T.Ring(g, ClientRectangle, rad);
    }

    protected override AccessibleObject CreateAccessibilityInstance() { return new Acc(this); }

    class Acc : ControlAccessibleObject
    {
        public Acc(Segmented s) : base(s) { }
        public override string Value { get { var s = (Segmented)Owner; return s.Items[s.SelectedIndex]; } }
    }
}

public class TextField : Panel
{
    public readonly TextBox Box = new TextBox();

    public TextField(Font font, string cue, string name)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = T.Fill;
        Cursor = Cursors.IBeam;
        Box.BorderStyle = BorderStyle.None;
        Box.BackColor = T.Fill;
        Box.ForeColor = T.Text;
        Box.Font = font;
        Box.AccessibleName = name;
        Controls.Add(Box);
        Box.GotFocus += delegate { Invalidate(); };
        Box.LostFocus += delegate { Invalidate(); };
        Box.HandleCreated += delegate { Native.Cue(Box, cue); };
        Click += delegate { Box.Focus(); };
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int pad = T.P(14);
        Box.Width = Width - 2 * pad;
        Box.Location = new Point(pad, (Height - Box.Height) / 2);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : T.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        T.FillRound(g, T.Fill, new RectangleF(0, 0, Width, Height), T.P(10));
        if (Box.Focused) T.Ring(g, ClientRectangle, T.P(10));
    }
}

public class Chip
{
    public string Text;
    public int Flag;
    public Chip(string text, int flag) { Text = text; Flag = flag; }
}

public class ResultView : Control
{
    string big = "", caption = "", badge = "";
    int flag;
    List<Chip> chips = new List<Chip>();
    readonly Timer anim = new Timer { Interval = 16 };
    readonly Stopwatch clock = new Stopwatch();
    readonly Random scramble = new Random();
    readonly Dictionary<float, Font> fonts = new Dictionary<float, Font>();
    readonly Font chipFont = new Font(T.DisplayFamily, 11f);
    const int AnimMs = 450;

    public ResultView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Result";
        anim.Tick += delegate
        {
            if (clock.ElapsedMilliseconds >= AnimMs) { anim.Stop(); clock.Stop(); }
            Invalidate();
        };
    }

    public void SetResult(string big, string caption, int flag, List<Chip> chips)
    {
        this.big = big;
        this.caption = caption;
        this.flag = flag;
        this.chips = chips ?? new List<Chip>();
        badge = flag == 1 ? "NATURAL 20  ·  CRITICAL HIT" : flag == 2 ? "NATURAL 1  ·  FUMBLE" : "";
        AccessibleName = "Result " + big + ". " + caption + (badge.Length > 0 ? ". " + badge : "");
        AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        clock.Restart();
        anim.Start();
        Invalidate();
    }

    Font BigFont(Graphics g, string s, float avail)
    {
        for (float size = 54; ; size -= 4)
        {
            Font f;
            if (!fonts.TryGetValue(size, out f)) fonts[size] = f = new Font(T.DisplayFamily, size);
            if (size <= 18 || g.MeasureString(s, f).Width <= avail) return f;
        }
    }

    string Scrambled()
    {
        var sb = new StringBuilder(big.Length);
        foreach (char c in big) sb.Append(c >= '0' && c <= '9' ? (char)('0' + scramble.Next(10)) : c);
        return sb.ToString();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        if (big.Length == 0)
        {
            int y0 = Height / 2 - T.P(30);
            DieIcon.Draw(g, 20, new PointF(Width / 2f, y0), T.P(40), T.Text3, T.P(1.6f));
            T.Draw(g, "Pick a die or type a roll", T.Body, new Rectangle(0, y0 + T.P(30), Width, T.P(22)), T.Text2, T.Center);
            return;
        }

        float t = clock.IsRunning ? Math.Min(1f, clock.ElapsedMilliseconds / (float)AnimMs) : 1f;
        bool rolling = t < 0.55f;
        string shown = rolling ? Scrambled() : big;
        Color color = rolling ? T.Text2 : flag == 1 ? T.Green : flag == 2 ? T.Red : T.Text;
        Font f = BigFont(g, big, Width - T.P(8));
        int bigH = (int)(f.GetHeight(g) * 1.05f);

        // chips: up to two centred rows, the rest folded into "+N more"
        int chipH = T.P(28), gap = T.P(6);
        var rows = new List<List<Tuple<string, int, int>>>();   // text, flag, width
        var row = new List<Tuple<string, int, int>>();
        int rowW = 0, shownCount = 0;
        foreach (var c in chips)
        {
            int w = (int)g.MeasureString(c.Text, chipFont).Width + T.P(18);
            if (row.Count > 0 && rowW + gap + w > Width)
            {
                rows.Add(row);
                if (rows.Count == 2) break;
                row = new List<Tuple<string, int, int>>();
                rowW = 0;
            }
            rowW += (row.Count > 0 ? gap : 0) + w;
            row.Add(Tuple.Create(c.Text, c.Flag, w));
            shownCount++;
        }
        if (rows.Count < 2 && row.Count > 0) rows.Add(row);
        if (shownCount < chips.Count && rows.Count > 0)
        {
            // drop chips from the last row until "+N more" fits beside them
            var last = rows[rows.Count - 1];
            int hidden = chips.Count - shownCount;
            while (true)
            {
                string more = "+" + hidden + " more";
                int mw = (int)g.MeasureString(more, chipFont).Width + T.P(18);
                if (last.Count == 0 || last.Sum(c => c.Item3) + gap * last.Count + mw <= Width) { last.Add(Tuple.Create(more, -1, mw)); break; }
                last.RemoveAt(last.Count - 1);
                hidden++;
            }
        }

        int badgeH = badge.Length > 0 ? T.P(26) : 0, capH = T.P(20);
        int chipsH = rows.Count == 0 ? 0 : rows.Count * (chipH + gap) - gap;
        int total = bigH + (badgeH > 0 ? T.P(6) + badgeH : 0) + T.P(4) + capH + (chipsH > 0 ? T.P(14) + chipsH : 0);
        int y = Math.Max(0, (Height - total) / 2);

        float scale = t >= 0.55f && t < 1f ? 1f + 0.07f * (1f - (t - 0.55f) / 0.45f) : 1f;
        var state = g.Save();
        g.TranslateTransform(Width / 2f, y + bigH / 2f);
        g.ScaleTransform(scale, scale);
        using (var br = new SolidBrush(color))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            g.DrawString(shown, f, br, new RectangleF(-Width, -bigH / 2f, Width * 2, bigH), sf);
        g.Restore(state);
        y += bigH;

        int fade = (int)(255 * Math.Max(0f, Math.Min(1f, (t - 0.45f) / 0.45f)));
        if (badgeH > 0)
        {
            y += T.P(6);
            Color c = flag == 1 ? T.Green : T.Red;
            int bw = T.Measure(badge, T.SmallStrong) + T.P(26);
            var br = new Rectangle((Width - bw) / 2, y, bw, badgeH);
            T.FillRound(g, T.Alpha(c, fade * 45 / 255), br, badgeH / 2f);
            using (var b = new SolidBrush(T.Alpha(c, fade)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(badge, T.SmallStrong, b, br, sf);
            y += badgeH;
        }
        y += T.P(4);
        T.Draw(g, caption, T.Body, new Rectangle(0, y, Width, capH), T.Text2, T.Center | TextFormatFlags.EndEllipsis);
        y += capH;

        if (rows.Count > 0)
        {
            y += T.P(14);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                foreach (var r in rows)
                {
                    int w = r.Sum(c => c.Item3) + gap * (r.Count - 1), x = (Width - w) / 2;
                    foreach (var c in r)
                    {
                        var cr = new Rectangle(x, y, c.Item3, chipH);
                        Color fc = c.Item2 == 1 ? T.Green : c.Item2 == 2 ? T.Red : c.Item2 < 0 ? T.Text2 : T.Text;
                        T.FillRound(g, T.Alpha(c.Item2 == 1 ? T.Green : c.Item2 == 2 ? T.Red : T.Fill, c.Item2 > 0 ? fade * 40 / 255 : fade), cr, chipH / 2f);
                        using (var b = new SolidBrush(T.Alpha(fc, fade))) g.DrawString(c.Item1, chipFont, b, cr, sf);
                        x += c.Item3 + gap;
                    }
                    y += chipH + gap;
                }
        }
    }
}

public class HistoryList : ListBox
{
    readonly ToolTip tip = new ToolTip();
    int tipIndex = -1;

    public HistoryList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        BackColor = T.Card;
        ForeColor = T.Text;
        IntegralHeight = false;
        ItemHeight = T.P(56);
        AccessibleName = "Roll history";
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkScrollbars(this); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexFromPoint(e.Location);
        if (i != tipIndex)
        {
            tipIndex = i;
            tip.SetToolTip(this, i >= 0 && i < Items.Count ? ((HistoryEntry)Items[i]).Details : "");
        }
        base.OnMouseMove(e);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var h = (HistoryEntry)Items[e.Index];
        var g = e.Graphics;
        var b = e.Bounds;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(T.Card)) g.FillRectangle(bg, b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (selected) T.FillRound(g, T.Fill, new RectangleF(b.X + T.P(4), b.Y + T.P(2), b.Width - T.P(8), b.Height - T.P(4)), T.P(10));
        g.SmoothingMode = SmoothingMode.None;

        int pad = T.P(14);
        int vw = Math.Min(T.Measure(h.Value, T.Value), b.Width / 2);
        Color vc = h.Flag == 1 ? T.Green : h.Flag == 2 ? T.Red : T.Text;
        T.Draw(g, h.Value, T.Value, new Rectangle(b.Right - pad - vw, b.Y + T.P(8), vw, T.P(24)), vc, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        T.Draw(g, h.Time.ToString("t"), T.Small, new Rectangle(b.Right - pad - T.P(100), b.Y + T.P(32), T.P(100), T.P(16)), T.Text3, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        int lw = b.Width - 2 * pad - Math.Max(vw, T.P(60)) - T.P(14);
        T.Draw(g, h.Title, T.Body, new Rectangle(b.X + pad, b.Y + T.P(9), lw, T.P(20)), T.Text, T.Left | TextFormatFlags.EndEllipsis);
        T.Draw(g, h.Details, T.Small, new Rectangle(b.X + pad, b.Y + T.P(31), lw, T.P(16)), T.Text2, T.Left | TextFormatFlags.EndEllipsis);
        if (!selected && e.Index < Items.Count - 1)
            using (var pen = new Pen(Color.FromArgb(22, 255, 255, 255)))
                g.DrawLine(pen, b.X + pad, b.Bottom - 1, b.Right - pad, b.Bottom - 1);
    }
}

public class Dialog : Form
{
    readonly TextField field;

    Dialog(string title, string message, string okText, string initial, bool input)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = T.Card;
        ForeColor = T.Text;
        Font = T.Body;

        int w = T.P(400), pad = T.P(22), y = pad, bw = T.P(100), bh = T.P(36);
        int mh = TextRenderer.MeasureText(message, T.Body, new Size(w - 2 * pad, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        Controls.Add(new Label { Text = message, UseMnemonic = false, ForeColor = T.Text, Font = T.Body, Bounds = new Rectangle(pad, y, w - 2 * pad, mh + T.P(4)) });
        y += mh + T.P(16);
        if (input)
        {
            field = new TextField(T.Body, "", title) { Bounds = new Rectangle(pad, y, w - 2 * pad, T.P(40)) };
            field.Box.Text = initial;
            field.Box.MaxLength = 60;
            Controls.Add(field);
            y += T.P(40) + T.P(20);
        }
        var ok = new PillButton(okText, Look.Primary) { Bounds = new Rectangle(w - pad - bw, y, bw, bh) };
        var cancel = new PillButton("Cancel", Look.Secondary) { Bounds = new Rectangle(w - pad - 2 * bw - T.P(10), y, bw, bh) };
        ok.Click += delegate { DialogResult = DialogResult.OK; };
        cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
        Controls.Add(ok);
        Controls.Add(cancel);
        ClientSize = new Size(w, y + bh + pad);
        Shown += delegate { if (field != null) { field.Box.Focus(); field.Box.SelectAll(); } else ok.Focus(); };
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitle(this, T.Card); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
        if (keyData == Keys.Enter)
        {
            var b = ActiveControl as PillButton;
            if (b != null) b.PerformClick(); else DialogResult = DialogResult.OK;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public static string Ask(IWin32Window owner, string title, string message, string initial, string okText)
    {
        using (var d = new Dialog(title, message, okText, initial, true))
            return d.ShowDialog(owner) == DialogResult.OK ? d.field.Box.Text.Trim() : null;
    }

    public static bool Confirm(IWin32Window owner, string title, string message, string okText)
    {
        using (var d = new Dialog(title, message, okText, null, false))
            return d.ShowDialog(owner) == DialogResult.OK;
    }
}

// ---------------------------------------------------------------- main window

public class MainForm : Form
{
    const int MaxHistory = 500;
    static readonly int[] DieSizes = { 4, 6, 8, 10, 12, 20, 100 };
    static readonly string[] OpSymbols = { "", "+", "−", "×" };

    readonly Random rng = new Random();
    readonly List<SavedRoll> saved = new List<SavedRoll>();
    readonly ToolTip tips = new ToolTip();

    Card quickCard, rollCard, repeatCard, resultCard, savedCard, historyCard;
    readonly List<PillButton> tiles = new List<PillButton>();
    Stepper diceStep, modStep, timesStep, opValue;
    Segmented advSeg, opSeg;
    TextField expr;
    Label hint, savedEmpty, historyEmpty;
    PillButton rollBtn, statsBtn, saveBtn, copyBtn, exportBtn, clearBtn;
    ResultView result;
    FlowLayoutPanel savedFlow;
    HistoryList history;
    const string HintText = "Try 2d6+3, 4d6dl1 (drop lowest), 2d20kh1 or (1d8+2)*2";

    public MainForm()
    {
        using (var g = CreateGraphics()) T.Init(g.DpiX / 96f);
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Text = "D&D Dice Roller";
        BackColor = T.Bg;
        ForeColor = T.Text;
        Font = T.Body;
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new Size(Math.Min(T.P(1000), work.Width - T.P(40)), Math.Min(T.P(720), work.Height - T.P(50)));
        MinimumSize = SizeFromClientSize(new Size(T.P(940), T.P(640)));

        quickCard = AddCard("Quick roll");
        foreach (int sides in DieSizes)
        {
            int s = sides;
            var tile = new PillButton("d" + s, Look.Tile) { Sides = s, AccessibleName = "Roll a d" + s };
            tile.Click += delegate { expr.Box.Text = diceStep.Value + "d" + s + ModText(); RollCurrent(); };
            quickCard.Controls.Add(tile);
            tiles.Add(tile);
        }
        diceStep = new Stepper("Number of dice", 1, Dice.MaxDice, 1);
        modStep = new Stepper("Modifier", -999, 999, 0) { Signed = true };
        quickCard.Controls.Add(diceStep);
        quickCard.Controls.Add(modStep);

        rollCard = AddCard("Roll");
        expr = new TextField(T.Mono, "2d6+3", "Dice roll");
        expr.Box.Text = "1d20";
        expr.Box.TextChanged += delegate { ClearError(); };
        hint = new Label { Text = HintText, UseMnemonic = false, AutoEllipsis = true, ForeColor = T.Text2, Font = T.Small, BackColor = T.Card };
        advSeg = new Segmented("Advantage", "Normal", "Advantage", "Disadvantage");
        rollCard.Controls.Add(expr);
        rollCard.Controls.Add(hint);
        rollCard.Controls.Add(advSeg);

        repeatCard = AddCard("Repeat");
        timesStep = new Stepper("Times to roll", 1, 100, 1);
        opSeg = new Segmented("Change each result", "None", "+", "−", "×");
        opValue = new Stepper("Amount for each result", 0, 1000, 2) { Enabled = false };
        opSeg.Changed += delegate { opValue.Enabled = opSeg.SelectedIndex > 0; };
        repeatCard.Controls.Add(timesStep);
        repeatCard.Controls.Add(opSeg);
        repeatCard.Controls.Add(opValue);

        rollBtn = new PillButton("Roll", Look.Primary);
        statsBtn = new PillButton("Roll Stats", Look.Secondary);
        saveBtn = new PillButton("Save Roll", Look.Secondary);
        rollBtn.Click += delegate { RollCurrent(); };
        statsBtn.Click += delegate { RollStats(); };
        saveBtn.Click += delegate { SaveCurrent(); };
        tips.SetToolTip(rollBtn, "Roll (Enter)");
        tips.SetToolTip(statsBtn, "Roll six ability scores: 4d6, drop the lowest");
        tips.SetToolTip(saveBtn, "Keep this roll, with its repeat settings, as a one-click button");
        Controls.Add(rollBtn);
        Controls.Add(statsBtn);
        Controls.Add(saveBtn);

        resultCard = AddCard("Result");
        result = new ResultView { BackColor = T.Card };
        resultCard.Controls.Add(result);

        savedCard = AddCard("Saved rolls");
        savedFlow = new FlowLayoutPanel { BackColor = T.Card, AutoScroll = true, WrapContents = true };
        savedFlow.HandleCreated += delegate { Native.DarkScrollbars(savedFlow); };
        savedEmpty = new Label { Text = "Set up a roll and press Save Roll to keep it here.", UseMnemonic = false, ForeColor = T.Text3, Font = T.Body, BackColor = T.Card, TextAlign = ContentAlignment.MiddleCenter };
        savedCard.Controls.Add(savedFlow);
        savedCard.Controls.Add(savedEmpty);

        historyCard = AddCard("History");
        history = new HistoryList();
        historyEmpty = new Label { Text = "Your rolls will show up here.", UseMnemonic = false, ForeColor = T.Text3, Font = T.Body, BackColor = T.Card, TextAlign = ContentAlignment.MiddleCenter };
        copyBtn = new PillButton("Copy", Look.Plain);
        exportBtn = new PillButton("Save…", Look.Plain);
        clearBtn = new PillButton("Clear", Look.Plain);
        copyBtn.Click += delegate { CopyHistory(); };
        exportBtn.Click += delegate { ExportHistory(); };
        clearBtn.Click += delegate { ClearHistory(); };
        tips.SetToolTip(copyBtn, "Copy the whole history");
        tips.SetToolTip(exportBtn, "Save the history to a text file");
        foreach (Control c in new Control[] { history, historyEmpty, copyBtn, exportBtn, clearBtn }) historyCard.Controls.Add(c);

        // containers first so Tab follows the visual order: left column, buttons, then the right column
        int tab = 0;
        foreach (Control c in new Control[] { quickCard, rollCard, repeatCard, rollBtn, statsBtn, saveBtn, resultCard, savedCard, historyCard }) c.TabIndex = tab++;
        foreach (Control c in tiles.Cast<Control>().Concat(new Control[] { diceStep, modStep, expr, advSeg, timesStep, opSeg, opValue, savedFlow, history, copyBtn, exportBtn, clearBtn }))
            c.TabIndex = tab++;
        foreach (Control c in new Control[] { quickCard, rollCard, repeatCard, resultCard, savedCard, historyCard }) c.TabStop = false;
        result.TabStop = false;

        LoadSaved();
        LoadHistory();
        Resize += delegate { DoLayout(); };
        DoLayout();
        Shown += delegate { expr.Box.Focus(); expr.Box.SelectAll(); };
    }

    Card AddCard(string title)
    {
        var c = new Card(title);
        Controls.Add(c);
        return c;
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitle(this, T.Bg); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int m = T.P(26);
        T.Draw(e.Graphics, "Dice Roller", T.Title, new Rectangle(m, T.P(16), T.P(420), T.P(36)), T.Text, T.Left);
        T.Draw(e.Graphics, "Dungeons & Dragons  ·  v" + App.Version, T.Body, new Rectangle(m, T.P(50), T.P(420), T.P(20)), T.Text2, T.Left);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            var b = ActiveControl as PillButton;
            if (b != null) b.PerformClick(); else RollCurrent();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void DoLayout()
    {
        if (history == null) return;
        int m = T.P(26), gap = T.P(12), lw = T.P(436), pad = T.P(18);
        int inner = lw - 2 * pad;

        quickCard.Bounds = new Rectangle(m, T.P(80), lw, T.P(168));
        int tw = (inner - 6 * T.P(8)) / 7;
        for (int i = 0; i < tiles.Count; i++) tiles[i].Bounds = new Rectangle(pad + i * (tw + T.P(8)) + (inner - 7 * tw - 6 * T.P(8)) / 2, T.P(38), tw, T.P(66));
        int sy = T.P(116);
        quickCard.Labels.Clear();
        quickCard.Labels.Add(Tuple.Create(new Rectangle(pad, sy, T.P(44), T.P(34)), "Dice"));
        diceStep.Bounds = new Rectangle(pad + T.P(44), sy, T.P(112), T.P(34));
        quickCard.Labels.Add(Tuple.Create(new Rectangle(pad + T.P(176), sy, T.P(76), T.P(34)), "Modifier"));
        modStep.Bounds = new Rectangle(pad + T.P(252), sy, inner - T.P(252), T.P(34));

        rollCard.Bounds = new Rectangle(m, quickCard.Bottom + gap, lw, T.P(152));
        expr.Bounds = new Rectangle(pad, T.P(38), inner, T.P(40));
        hint.Bounds = new Rectangle(pad + T.P(2), T.P(82), inner, T.P(18));
        advSeg.Bounds = new Rectangle(pad, T.P(106), inner, T.P(32));

        repeatCard.Bounds = new Rectangle(m, rollCard.Bottom + gap, lw, T.P(134));
        repeatCard.Labels.Clear();
        repeatCard.Labels.Add(Tuple.Create(new Rectangle(pad, T.P(38), T.P(96), T.P(34)), "Roll it"));
        timesStep.Bounds = new Rectangle(pad + T.P(96), T.P(38), T.P(124), T.P(34));
        repeatCard.Labels.Add(Tuple.Create(new Rectangle(pad + T.P(232), T.P(38), T.P(80), T.P(34)), "times"));
        repeatCard.Labels.Add(Tuple.Create(new Rectangle(pad, T.P(84), T.P(96), T.P(34)), "Each result"));
        opSeg.Bounds = new Rectangle(pad + T.P(96), T.P(84), T.P(176), T.P(34));
        opValue.Bounds = new Rectangle(pad + T.P(282), T.P(84), inner - T.P(282), T.P(34));

        int by = repeatCard.Bottom + T.P(14), bh = T.P(44), small = T.P(112), bgap = T.P(8);
        rollBtn.Bounds = new Rectangle(m, by, lw - 2 * (small + bgap), bh);
        statsBtn.Bounds = new Rectangle(rollBtn.Right + bgap, by, small, bh);
        saveBtn.Bounds = new Rectangle(statsBtn.Right + bgap, by, small, bh);

        int rx = m + lw + T.P(20), rw = ClientSize.Width - rx - m;
        resultCard.Bounds = new Rectangle(rx, m, rw, T.P(262));
        result.Bounds = new Rectangle(pad, T.P(34), rw - 2 * pad, T.P(262) - T.P(34) - T.P(14));

        savedCard.Bounds = new Rectangle(rx, resultCard.Bottom + gap, rw, T.P(120));
        savedFlow.Bounds = new Rectangle(T.P(12), T.P(36), rw - T.P(24), T.P(120) - T.P(36) - T.P(10));
        savedEmpty.Bounds = savedFlow.Bounds;

        int hy = savedCard.Bottom + gap;
        historyCard.Bounds = new Rectangle(rx, hy, rw, Math.Max(T.P(160), ClientSize.Height - hy - T.P(22)));
        int x = rw - T.P(10);
        foreach (var b in new[] { clearBtn, exportBtn, copyBtn })
        {
            int w = b.PreferredWidth - T.P(8);
            x -= w;
            b.Bounds = new Rectangle(x, T.P(6), w, T.P(30));
            x -= T.P(2);
        }
        history.Bounds = new Rectangle(T.P(6), T.P(40), rw - T.P(12), historyCard.Height - T.P(40) - T.P(8));
        historyEmpty.Bounds = history.Bounds;
        foreach (Control c in Controls) c.Invalidate(true);
    }

    string ModText()
    {
        int v = modStep.Value;
        return v == 0 ? "" : v > 0 ? "+" + v : v.ToString();
    }

    // ---- rolling

    void RollCurrent()
    {
        string text = expr.Box.Text.Trim();
        int times = timesStep.Value, op = opSeg.SelectedIndex, opv = opValue.Value, adv = advSeg.SelectedIndex;
        var chips = new List<Chip>();
        var details = new List<string>();
        long total = 0;
        int flag = 0;
        bool advUsed = false;
        try
        {
            for (int i = 0; i < times; i++)
            {
                var r = Dice.Roll(text, rng, adv);
                long v = Dice.ApplyEach(r.Total, op, opv);
                total = checked(total + v);
                int f = r.Natural == 20 ? 1 : r.Natural == 1 ? 2 : 0;
                if (times == 1) flag = f;
                advUsed |= r.AdvUsed;
                chips.Add(new Chip(T.Num(v), f));
                string body = string.Join("; ", r.Parts);
                if (!body.EndsWith(" " + r.Total)) body += (body.Length > 0 ? " → " : "") + T.Num(r.Total);   // shows modifiers like +3
                string d = body + (op > 0 ?"  " + OpSymbols[op] + " " + opv + " = " + T.Num(v) : "");
                details.Add(times > 1 ? "#" + (i + 1) + ": " + d : d);
            }
        }
        catch (DiceException ex) { ShowError(ex.Message); return; }
        catch (OverflowException) { ShowError("That number is too big."); return; }

        string desc = Describe(text, op, opv, advUsed ? adv : 0);
        result.SetResult(T.Num(total), times > 1 ? "Total of " + times + " rolls  ·  " + desc : desc, flag, times > 1 ? chips : null);
        AddHistory(times > 1 ? desc + ", rolled " + times + " times" : desc, T.Num(total), string.Join("   ", details), flag);
    }

    static string Describe(string text, int op, int opv, int adv)
    {
        string s = text;
        if (op > 0) s = (text.IndexOfAny(new[] { '+', '-' }, 1) >= 0 ? "(" + text + ")" : text) + " " + OpSymbols[op] + " " + opv;
        if (adv == 1) s += "  ·  advantage";
        if (adv == 2) s += "  ·  disadvantage";
        return s;
    }

    void RollStats()
    {
        var scores = new List<long>();
        var parts = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            var r = Dice.Roll("4d6dl1", rng, 0);
            scores.Add(r.Total);
            parts.Add(r.Parts[0]);
        }
        string big = string.Join("   ", scores);
        result.SetResult(big, "Ability scores  ·  4d6, drop the lowest  ·  total " + scores.Sum(), 0, null);
        AddHistory("Ability scores (4d6, drop lowest)", string.Join(" ", scores), string.Join("; ", parts), 0);
    }

    void ShowError(string message)
    {
        hint.Text = message;
        hint.ForeColor = T.Red;
        System.Media.SystemSounds.Beep.Play();
    }

    void ClearError()
    {
        if (hint.ForeColor == T.Text2) return;
        hint.Text = HintText;
        hint.ForeColor = T.Text2;
    }

    // ---- saved rolls

    void LoadSaved()
    {
        foreach (var line in Store.Read(Store.SavedFile))
        {
            var s = SavedRoll.Parse(line);
            if (s != null) saved.Add(s);
        }
        RebuildSaved();
    }

    bool WriteSaved()
    {
        if (Store.Write(Store.SavedFile, saved.Select(s => s.ToLine()))) return true;
        Dialog.Confirm(this, "Couldn't save", "Your saved rolls couldn't be written to " + Store.Dir + ". They'll be lost when you close the app.", "OK");
        return false;
    }

    void SaveCurrent()
    {
        string text = expr.Box.Text.Trim();
        try { Dice.Roll(text, new Random(), 0); }
        catch (DiceException ex) { ShowError(ex.Message); return; }
        string desc = Describe(text, opSeg.SelectedIndex, opValue.Value, advSeg.SelectedIndex) + (timesStep.Value > 1 ? ", " + timesStep.Value + " times" : "");
        string name = Dialog.Ask(this, "Save roll", "Name this roll so it's one click away next time.", desc, "Save");
        if (string.IsNullOrEmpty(name)) return;
        name = Store.Clean(name);
        var existing = saved.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (!Dialog.Confirm(this, "Replace saved roll", "You already have a roll called “" + existing.Name + "”. Replace it?", "Replace")) return;
            saved.Remove(existing);
        }
        saved.Add(new SavedRoll { Name = name, Expr = text, Times = timesStep.Value, Op = opSeg.SelectedIndex, OpValue = opValue.Value, Adv = advSeg.SelectedIndex });
        WriteSaved();
        RebuildSaved();
    }

    void RebuildSaved()
    {
        savedFlow.SuspendLayout();
        foreach (Control c in savedFlow.Controls.Cast<Control>().ToList()) c.Dispose();
        foreach (var s in saved)
        {
            var roll = s;
            var chip = new PillButton(s.Name, Look.Secondary) { Closable = true, AccessibleName = "Saved roll " + s.Name + ". Press Delete to remove." };
            chip.Size = new Size(Math.Min(chip.PreferredWidth, T.P(260)), T.P(34));
            chip.Margin = new Padding(T.P(4));
            chip.Click += delegate { UseSaved(roll); };
            chip.CloseClicked += delegate { DeleteSaved(roll); };
            string op = roll.Op > 0 ? ", " + OpSymbols[roll.Op] + roll.OpValue + " each" : "";
            tips.SetToolTip(chip, roll.Expr + op + (roll.Times > 1 ? ", " + roll.Times + " times" : "") + (roll.Adv == 1 ? ", advantage" : roll.Adv == 2 ? ", disadvantage" : ""));
            savedFlow.Controls.Add(chip);
        }
        savedFlow.ResumeLayout();
        savedEmpty.Visible = saved.Count == 0;
        savedFlow.Visible = saved.Count > 0;
    }

    void UseSaved(SavedRoll s)
    {
        expr.Box.Text = s.Expr;
        timesStep.Value = s.Times;
        opSeg.SelectedIndex = s.Op;
        opValue.Value = s.OpValue;
        advSeg.SelectedIndex = s.Adv;
        RollCurrent();
    }

    void DeleteSaved(SavedRoll s)
    {
        if (!Dialog.Confirm(this, "Delete saved roll", "Delete “" + s.Name + "”? This can't be undone.", "Delete")) return;
        saved.Remove(s);
        WriteSaved();
        RebuildSaved();
    }

    // ---- history

    void LoadHistory()
    {
        foreach (var line in Store.Read(Store.HistoryFile).Take(MaxHistory))
        {
            var h = HistoryEntry.Parse(line);
            if (h != null) history.Items.Add(h);
        }
        UpdateHistoryState();
    }

    void AddHistory(string title, string value, string details, int flag)
    {
        history.Items.Insert(0, new HistoryEntry { Time = DateTime.Now, Title = title, Value = value, Details = details, Flag = flag });
        while (history.Items.Count > MaxHistory) history.Items.RemoveAt(history.Items.Count - 1);
        history.TopIndex = 0;
        WriteHistory();
        UpdateHistoryState();
    }

    void WriteHistory()
    {
        if (!Store.Write(Store.HistoryFile, history.Items.Cast<HistoryEntry>().Select(h => h.ToLine())))
        {
            hint.Text = "History couldn't be saved to " + Store.Dir;
            hint.ForeColor = T.Red;
        }
    }

    void UpdateHistoryState()
    {
        bool any = history.Items.Count > 0;
        history.Visible = any;
        historyEmpty.Visible = !any;
        copyBtn.Enabled = exportBtn.Enabled = clearBtn.Enabled = any;
    }

    string HistoryText() { return string.Join(Environment.NewLine, history.Items.Cast<HistoryEntry>().Select(h => h.ToString())); }

    void CopyHistory()
    {
        try { Clipboard.SetText(HistoryText()); } catch (ExternalException) { return; }
        copyBtn.Text = "Copied";
        copyBtn.Invalidate();
        var t = new Timer { Interval = 1400 };
        t.Tick += delegate { t.Dispose(); copyBtn.Text = "Copy"; copyBtn.Invalidate(); };
        t.Start();
    }

    void ExportHistory()
    {
        using (var d = new SaveFileDialog { Filter = "Text file (*.txt)|*.txt", FileName = "dice-history-" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt" })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(d.FileName, HistoryText(), Encoding.UTF8); }
            catch (Exception ex) { Dialog.Confirm(this, "Couldn't save", "The history couldn't be saved: " + ex.Message, "OK"); }
        }
    }

    void ClearHistory()
    {
        int n = history.Items.Count;
        if (!Dialog.Confirm(this, "Clear history", "Delete all " + n + (n == 1 ? " roll" : " rolls") + " from the history? This can't be undone.", "Clear")) return;
        history.Items.Clear();
        WriteHistory();
        UpdateHistoryState();
    }
}

// ---------------------------------------------------------------- self-test (run by the build: DnDRoller.exe --selftest)

static class SelfTest
{
    public static int Run()
    {
        var fails = new List<string>();
        var rng = new Random(12345);
        Action<string, long> eq = (e, want) =>
        {
            try { long got = Dice.Roll(e, rng, 0).Total; if (got != want) fails.Add(e + " gave " + got + ", expected " + want); }
            catch (Exception ex) { fails.Add(e + " threw: " + ex.Message); }
        };
        Action<string> bad = e =>
        {
            try { Dice.Roll(e, rng, 0); fails.Add("\"" + e + "\" should be rejected"); }
            catch (DiceException) { }
        };
        Action<bool, string> check = (ok, what) => { if (!ok) fails.Add(what); };

        eq("2+3*4", 14); eq("(2+3)*4", 20); eq("2x3", 6); eq("2 × 3", 6); eq("10/3", 3); eq("-7/2", -4); eq("-5+2", -3); eq("3d6*0", 0);
        foreach (var e in new[] { "", "2d", "d", "3d6+", "101d6", "1d1", "4d6dl5", "4d6kh0", "(1d6", "1/0", "abc", "9999999", "2d6)" }) bad(e);

        var faces = new HashSet<long>();
        for (int i = 0; i < 3000; i++)
        {
            long v = Dice.Roll("4d6dl1", rng, 0).Total; check(v >= 3 && v <= 18, "4d6dl1 out of range: " + v);
            v = Dice.Roll("3d6*5", rng, 0).Total; check(v % 5 == 0 && v >= 15 && v <= 90, "3d6*5 out of range: " + v);
            v = Dice.Roll("d%", rng, 0).Total; check(v >= 1 && v <= 100, "d% out of range: " + v);
            faces.Add(Dice.Roll("1d6", rng, 0).Total);
            var k = Dice.Roll("2d20kh1", rng, 0); check(k.Natural == k.Total && k.Total >= 1 && k.Total <= 20, "2d20kh1 natural wrong");
            var a = Dice.Roll("1d20+1d20", rng, 1);
            check(a.Parts.Count == 2 && a.Parts[0].Contains("advantage") && !a.Parts[1].Contains("advantage"), "advantage must only apply to the first d20");
            var lo = Dice.Roll("4d6kl1", rng, 0).Total; check(lo >= 1 && lo <= 6, "4d6kl1 out of range: " + lo);
        }
        check(faces.Count == 6, "1d6 didn't produce every face");
        check(Dice.ApplyEach(7, 3, 5) == 35 && Dice.ApplyEach(7, 2, 5) == 2 && Dice.ApplyEach(7, 1, 5) == 12 && Dice.ApplyEach(7, 0, 5) == 7, "ApplyEach wrong");

        var sr = SavedRoll.Parse(new SavedRoll { Name = "Fire\tball", Expr = "8d6", Times = 5, Op = 3, OpValue = 5, Adv = 1 }.ToLine());
        check(sr != null && sr.Name == "Fire ball" && sr.Expr == "8d6" && sr.Times == 5 && sr.Op == 3 && sr.OpValue == 5 && sr.Adv == 1, "SavedRoll round-trip failed");
        var he = HistoryEntry.Parse(new HistoryEntry { Time = new DateTime(2026, 1, 2, 3, 4, 5), Title = "t", Value = "12", Details = "d", Flag = 1 }.ToLine());
        check(he != null && he.Time == new DateTime(2026, 1, 2, 3, 4, 5) && he.Value == "12" && he.Flag == 1, "HistoryEntry round-trip failed");

        foreach (var f in fails) Console.Error.WriteLine(f);
        return fails.Count == 0 ? 0 : 1;
    }
}
