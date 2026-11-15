using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

public class DnDRollerForm : Form
{
    Random rng = new Random();
    NumericUpDown quantity, modifier;
    TextBox expression, history;
    Label result;
    CheckBox advantage, disadvantage;
    Button rollButton;

    public DnDRollerForm()
    {
        Text = "D&D Dice Roller"; Width = 560; Height = 650;
        MinimumSize = new Size(480, 560); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(28, 30, 36); ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel(); root.Dock = DockStyle.Fill; root.Padding = new Padding(18);
        root.ColumnCount = 1; root.RowCount = 6;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var heading = new Label { Text = "D&D DICE ROLLER", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 18F), ForeColor = Color.FromArgb(232,190,112), TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(heading, 0, 0);

        var dice = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0,8,0,0) };
        foreach (int sides in new int[] {4,6,8,10,12,20,100}) { int s = sides; var b = MakeButton("d"+s, 66, 38); b.Click += delegate { expression.Text = ((int)quantity.Value) + "d" + s + ModText(); Roll(); }; dice.Controls.Add(b); }
        root.Controls.Add(dice,0,1);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0,5,0,0) };
        controls.Controls.Add(MakeLabel("Dice")); quantity = new NumericUpDown { Minimum=1, Maximum=100, Value=1, Width=62, BackColor=Color.FromArgb(45,48,56), ForeColor=Color.White }; controls.Controls.Add(quantity);
        controls.Controls.Add(MakeLabel("Modifier")); modifier = new NumericUpDown { Minimum=-999, Maximum=999, Value=0, Width=76, BackColor=Color.FromArgb(45,48,56), ForeColor=Color.White }; controls.Controls.Add(modifier);
        root.Controls.Add(controls,0,2);

        var exprPanel = new FlowLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(0,6,0,0) };
        exprPanel.Controls.Add(MakeLabel("Expression")); expression = new TextBox { Text="1d20", Width=145, Font=new Font("Consolas",12), BackColor=Color.FromArgb(45,48,56), ForeColor=Color.White, BorderStyle=BorderStyle.FixedSingle }; exprPanel.Controls.Add(expression);
        advantage = new CheckBox { Text="Advantage", AutoSize=true, ForeColor=Color.White, Margin=new Padding(12,7,0,0) };
        disadvantage = new CheckBox { Text="Disadvantage", AutoSize=true, ForeColor=Color.White, Margin=new Padding(8,7,0,0) };
        advantage.CheckedChanged += delegate { if(advantage.Checked) disadvantage.Checked=false; };
        disadvantage.CheckedChanged += delegate { if(disadvantage.Checked) advantage.Checked=false; };
        exprPanel.Controls.Add(advantage); exprPanel.Controls.Add(disadvantage); root.Controls.Add(exprPanel,0,3);

        var action = new FlowLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(0,8,0,0) };
        rollButton = MakeButton("ROLL DICE", 150, 44); rollButton.BackColor=Color.FromArgb(153,91,48); rollButton.Click += delegate { Roll(); }; action.Controls.Add(rollButton);
        var clear = MakeButton("Clear history", 115, 44); clear.Click += delegate { history.Clear(); }; action.Controls.Add(clear);
        root.Controls.Add(action,0,4);

        var bottom = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,58)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        result = new Label { Text="Ready to roll", Dock=DockStyle.Fill, Font=new Font("Segoe UI Semibold",22), ForeColor=Color.FromArgb(232,190,112), TextAlign=ContentAlignment.MiddleCenter };
        bottom.Controls.Add(result,0,0);
        history = new TextBox { Dock=DockStyle.Fill, Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Font=new Font("Consolas",10), BackColor=Color.FromArgb(20,22,27), ForeColor=Color.Gainsboro, BorderStyle=BorderStyle.FixedSingle };
        bottom.Controls.Add(history,0,1); root.Controls.Add(bottom,0,5);
        AcceptButton = rollButton;
        expression.KeyDown += delegate(object sender, KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { Roll(); e.SuppressKeyPress=true; } };
    }

    Label MakeLabel(string t) { return new Label { Text=t, AutoSize=true, ForeColor=Color.Gainsboro, Margin=new Padding(0,8,7,0) }; }
    Button MakeButton(string t,int w,int h) { return new Button { Text=t, Width=w, Height=h, FlatStyle=FlatStyle.Flat, BackColor=Color.FromArgb(58,62,72), ForeColor=Color.White, Font=new Font("Segoe UI Semibold",10), Margin=new Padding(4), Cursor=Cursors.Hand }; }
    string ModText() { return ((int)modifier.Value)==0 ? "" : (((int)modifier.Value>0?"+":"")+(int)modifier.Value); }

    void Roll()
    {
        try {
            string input=expression.Text.Trim().ToLowerInvariant().Replace(" ","");
            int sign=1, total=0; List<string> details=new List<string>(); int pos=0;
            if(input.Length==0) throw new Exception("Enter a dice expression, e.g. 2d6+3.");
            while(pos<input.Length) {
                if(input[pos]=='+') { sign=1; pos++; continue; }
                if(input[pos]=='-') { sign=-1; pos++; continue; }
                int start=pos; while(pos<input.Length && Char.IsDigit(input[pos])) pos++;
                int number=0; bool hasNumber=pos>start; if(hasNumber) number=Int32.Parse(input.Substring(start,pos-start));
                if(pos<input.Length && input[pos]=='d') {
                    pos++; int ds=pos; while(pos<input.Length && Char.IsDigit(input[pos])) pos++;
                    if(pos==ds) throw new Exception("Missing die size after d.");
                    int sides=Int32.Parse(input.Substring(ds,pos-ds)); int count=hasNumber?number:1;
                    if(count<1 || count>100 || sides<2 || sides>1000) throw new Exception("Use 1–100 dice with 2–1000 sides.");
                    List<int> rolls=new List<int>(); for(int i=0;i<count;i++) rolls.Add(rng.Next(1,sides+1));
                    if(count==1 && sides==20 && (advantage.Checked || disadvantage.Checked)) {
                        int extra=rng.Next(1,21); int chosen=advantage.Checked?Math.Max(rolls[0],extra):Math.Min(rolls[0],extra);
                        details.Add("2d20 " +(advantage.Checked?"adv":"dis")+" ["+rolls[0]+", "+extra+"] → "+chosen);
                        total += sign*chosen;
                    } else { int subtotal=rolls.Sum(); details.Add(count+"d"+sides+" ["+String.Join(", ",rolls)+"] = "+subtotal); total += sign*subtotal; }
                } else {
                    if(!hasNumber) throw new Exception("Invalid expression. Example: 2d6+3 or d20-1.");
                    details.Add((sign<0?"-":"+")+number); total += sign*number;
                }
                sign=1;
                if(pos<input.Length && input[pos]!='+' && input[pos]!='-') throw new Exception("Invalid expression. Example: 2d6+3 or d20-1.");
            }
            result.Text=total.ToString();
            string line=DateTime.Now.ToString("HH:mm:ss")+"  "+expression.Text+"  = "+total+"\r\n    "+String.Join("; ",details);
            history.AppendText(line+Environment.NewLine);
        } catch(Exception ex) { MessageBox.Show(ex.Message,"Dice expression",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
    }
}

public static class DnDRollerProgram { [STAThread] public static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new DnDRollerForm()); } }
