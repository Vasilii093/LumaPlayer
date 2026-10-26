using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
class PresetTest {
static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);}
[STAThread] static int Main(){
try {
Application.EnableVisualStyles();
var a=Assembly.LoadFrom("work/LumaPlayer/LumaPlayer.exe"); var t=a.GetType("Luma.Player");
var p=(Form)Activator.CreateInstance(t,new object[]{null});
var show=t.GetMethod("ShowSound",flags); Exception failure=null;
for(int pass=0;pass<3;pass++){
int stage=pass; var timer=new Timer{Interval=100};
timer.Tick+=delegate {
timer.Stop(); Form d=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text.Contains("эквалайзер"));
try {
Check(d!=null,"dialog missing"); var combo=(ComboBox)d.Controls.Find("SoundPreset",false)[0];
var nums=d.Controls.OfType<NumericUpDown>().OrderBy(n=>n.Top).ToArray();
Check(nums.Length==6 && combo.Items.Count==9,"control count");
if(stage==0){
for(int i=0;i<8;i++){combo.SelectedIndex=i; Check(nums.All(n=>n.Value>=n.Minimum&&n.Value<=n.Maximum),"range");}
combo.SelectedIndex=1; Check(nums[1].Value==-3 && nums[4].Value==3,"dialogue preset");
nums[2].Value=1; Check(combo.SelectedIndex==8,"custom state");
combo.SelectedIndex=4;
using(var b=new Bitmap(d.Width,d.Height)){d.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save("work/equalizer-preview.png");}
d.Controls.OfType<Button>().First(b=>b.Text=="Применить").PerformClick();
} else if(stage==1){Check(combo.SelectedIndex==4 && nums[1].Value==5,"reopen applied");combo.SelectedIndex=2; d.Close();}
else {Check(combo.SelectedIndex==4,"cancel mutated state");d.Controls.OfType<Button>().First(b=>b.Text=="Сбросить").PerformClick();Check(combo.SelectedIndex==0&&nums.All(n=>n.Value==0),"reset");d.Controls.OfType<Button>().First(b=>b.Text=="Применить").PerformClick();}
} catch(Exception ex){failure=ex;if(d!=null)d.Close();}
};
timer.Start();show.Invoke(p,null);timer.Dispose();if(failure!=null)throw failure;
}
Check((int)t.GetField("soundGain",flags).GetValue(p)==0,"gain reset");
Check(((int[])t.GetField("soundBands",flags).GetValue(p)).All(n=>n==0),"bands reset");
p.Dispose(); Console.WriteLine("PASS: presets, manual edits, apply, reopen, cancel, reset");return 0;
} catch(Exception e){Console.WriteLine(e);return 1;}
}
}
