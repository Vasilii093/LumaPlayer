using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Luma {
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static void Main(string[] args) {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e) {
                try {
                    string logs=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"logs");
                    Directory.CreateDirectory(logs);
                    File.AppendAllText(Path.Combine(logs,"ui-error.txt"),DateTime.Now.ToString("s")+"\n"+e.Exception+"\n\n");
                } catch(IOException) {} catch(UnauthorizedAccessException) {}
                using(var dialog=new ThreadExceptionDialog(e.Exception)) {
                    if(dialog.ShowDialog()==DialogResult.Abort) Application.Exit();
                }
            };
            bool first;
            using(var mutex=new Mutex(true,"Local\\LumaPlayer.SingleInstance",out first)) {
                if(!first) { MessageBox.Show("Luma Player уже открыт.","Luma"); return; }
                PreRenderSession.CleanAbandoned(AppDomain.CurrentDomain.BaseDirectory);
                Application.Run(new Player(args.Length > 0 ? args[0] : null));
            }
        }
    }
    static class Rounded {
        public static GraphicsPath Path(RectangleF bounds,float radius) {
            var path=new GraphicsPath();
            float d=Math.Max(1,Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height)));
            path.AddArc(bounds.Left,bounds.Top,d,d,180,90);
            path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
            path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);
            path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        public static void Region(Control control,float radius) {
            if(control.Width<2 || control.Height<2)return;
            using(var path=Path(new RectangleF(0,0,control.Width,control.Height),radius)) {
                var old=control.Region;control.Region=new Region(path);if(old!=null)old.Dispose();
            }
        }
        public static void Fill(Graphics g,Color color,RectangleF bounds,float radius) {
            if(bounds.Width<=0 || bounds.Height<=0)return;
            using(var path=Path(bounds,radius))using(var brush=new SolidBrush(color))g.FillPath(brush,path);
        }
        public static Color Lift(Color color,int amount) {return Color.FromArgb(Math.Min(255,color.R+amount),Math.Min(255,color.G+amount),Math.Min(255,color.B+amount));}
    }
    sealed class RoundedPanel : Panel {
        public RoundedPanel(){DoubleBuffered=true;ResizeRedraw=true;}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Rounded.Region(this,22);}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);if(Width<3 || Height<3)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=Rounded.Path(new RectangleF(.5f,.5f,Width-1,Height-1),22))
            using(var pen=new Pen(Color.FromArgb(49,54,67)))e.Graphics.DrawPath(pen,path);
        }
    }
    sealed class RoundedButton : Button {
        bool hover,pressed;
        public RoundedButton() {
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
            BackColor=Color.FromArgb(32,36,47);ForeColor=Color.White;Cursor=Cursors.Hand;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        }
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Rounded.Region(this,11);}
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;g.Clear(Parent==null ? BackColor : Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=Enabled ? Rounded.Lift(BackColor,pressed ? 25 : hover ? 14 : 0) : Color.FromArgb(27,30,39);
            Rounded.Fill(g,fill,new RectangleF(.5f,.5f,Width-1,Height-1),11);
            if(Focused && ShowFocusCues)using(var path=Rounded.Path(new RectangleF(1.5f,1.5f,Width-3,Height-3),10))
                using(var pen=new Pen(Color.FromArgb(167,155,255)))g.DrawPath(pen,path);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(6,pressed ? 1 : 0,Width-12,Height),Enabled ? ForeColor : Color.FromArgb(125,133,151),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;base.OnMouseEnter(e);Invalidate();}
        protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;base.OnMouseLeave(e);Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;base.OnMouseDown(e);Invalidate();}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;base.OnMouseUp(e);Invalidate();}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space)pressed=true;base.OnKeyDown(e);Invalidate();}
        protected override void OnKeyUp(KeyEventArgs e){pressed=false;base.OnKeyUp(e);Invalidate();}
    }
    sealed class RoundedComboBox : ComboBox {
        [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] struct ComboInfo {public int Size;public NativeRect Item,Button;public int ButtonState;public IntPtr Combo,Edit,List;}
        [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr window,ref ComboInfo info);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out NativeRect rectangle);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
        bool hover;
        protected override void OnDropDown(EventArgs e) {
            base.OnDropDown(e);
            BeginInvoke((Action)delegate {
                if(IsDisposed || !DroppedDown)return;
                var info=new ComboInfo();info.Size=Marshal.SizeOf(typeof(ComboInfo));NativeRect rectangle;
                if(!GetComboBoxInfo(Handle,ref info) || !GetWindowRect(info.List,out rectangle))return;
                IntPtr region=CreateRoundRectRgn(0,0,rectangle.Right-rectangle.Left+1,rectangle.Bottom-rectangle.Top+1,16,16);
                if(region!=IntPtr.Zero && SetWindowRgn(info.List,region,true)==0)DeleteObject(region);
                // On success Windows owns the region and releases it with the popup.
            });
        }
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Rounded.Region(this,10);}
        protected override void OnMouseEnter(EventArgs e){hover=true;base.OnMouseEnter(e);Invalidate();}
        protected override void OnMouseLeave(EventArgs e){hover=false;base.OnMouseLeave(e);Invalidate();}
        protected override void OnSelectedIndexChanged(EventArgs e){base.OnSelectedIndexChanged(e);Invalidate();}
        void DrawFace(Graphics g) {
            using(var background=new SolidBrush(Parent==null ? BackColor : Parent.BackColor))g.FillRectangle(background,ClientRectangle);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            Rounded.Fill(g,Enabled && hover ? Rounded.Lift(BackColor,10) : BackColor,new RectangleF(.5f,.5f,Width-1,Height-1),10);
            using(var path=Rounded.Path(new RectangleF(.5f,.5f,Width-1,Height-1),10))
            using(var pen=new Pen(Focused ? Color.FromArgb(140,126,250) : Color.FromArgb(67,73,90)))g.DrawPath(pen,path);
            Color text=Enabled ? ForeColor : Color.FromArgb(130,139,158);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(12,0,Math.Max(1,Width-44),Height),text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            using(var pen=new Pen(text,1.6f)) {pen.StartCap=pen.EndCap=LineCap.Round;float x=Width-20,y=Height/2f;g.DrawLines(pen,new[]{new PointF(x-4,y-2),new PointF(x,y+2),new PointF(x+4,y-2)});}
        }
        protected override void WndProc(ref Message m) {
            base.WndProc(ref m);
            // Keep native selection, keyboard navigation and accessibility. Only
            // replace the closed face; the popup still uses the native list.
            if(m.Msg==0xF && IsHandleCreated)using(var g=Graphics.FromHwnd(Handle))DrawFace(g);
            else if((m.Msg==0x317 || m.Msg==0x318) && m.WParam!=IntPtr.Zero)using(var g=Graphics.FromHdc(m.WParam))DrawFace(g);
        }
    }
    sealed class RoundedVolume : TrackBar {
        void DrawFace(Graphics g) {
            using(var background=new SolidBrush(BackColor))g.FillRectangle(background,ClientRectangle);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            float y=Height/2f,x=10+(Width-20)*(Value-Minimum)/(float)Math.Max(1,Maximum-Minimum);
            Rounded.Fill(g,Color.FromArgb(61,66,80),new RectangleF(10,y-2,Math.Max(1,Width-20),4),2);
            Rounded.Fill(g,Color.FromArgb(140,126,250),new RectangleF(10,y-2,Math.Max(1,x-10),4),2);
            using(var b=new SolidBrush(Enabled ? Color.White : Color.Gray))g.FillEllipse(b,x-5,y-5,10,10);
            if(Focused && ShowFocusCues)using(var p=Rounded.Path(new RectangleF(1,1,Width-2,Height-2),8))using(var pen=new Pen(Color.FromArgb(140,126,250)))g.DrawPath(pen,p);
        }
        protected override void WndProc(ref Message m) {
            base.WndProc(ref m);
            if(m.Msg==0xF && IsHandleCreated)using(var g=Graphics.FromHwnd(Handle))DrawFace(g);
            else if((m.Msg==0x317 || m.Msg==0x318) && m.WParam!=IntPtr.Zero)using(var g=Graphics.FromHdc(m.WParam))DrawFace(g);
        }
    }
    sealed class ClickSeekBar : Control {
        int current;
        double[][] cached=new double[0][];
        public int Minimum { get; set; }
        public int Maximum { get; set; }
        public int Value { get { return current; } set { int next=Math.Max(Minimum,Math.Min(Maximum,value)); if(next!=current){current=next;Invalidate();} } }
        public ClickSeekBar() {
            Minimum=0; Maximum=10000; TabStop=true; Cursor=Cursors.Hand;
            AccessibleName="Позиция видео"; AccessibleRole=AccessibleRole.Slider;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        }
        public static double[][] CacheRanges(double duration,IEnumerable<PreparedChunk> chunks) {
            var ranges=new List<double[]>();
            if(duration<=0 || Double.IsNaN(duration) || Double.IsInfinity(duration)) return ranges.ToArray();
            foreach(var chunk in chunks) {
                double a=Math.Max(0,chunk.Start),b=Math.Min(duration,chunk.Start+chunk.Length);
                if(b>a) ranges.Add(new double[]{a/duration,b/duration});
            }
            ranges.Sort(delegate(double[] a,double[] b){return a[0].CompareTo(b[0]);});
            var merged=new List<double[]>();
            foreach(var range in ranges) {
                if(merged.Count>0 && range[0]<=merged[merged.Count-1][1]+0.0000001)
                    merged[merged.Count-1][1]=Math.Max(merged[merged.Count-1][1],range[1]);
                else merged.Add(range);
            }
            return merged.ToArray();
        }
        public void SetCache(double duration,IEnumerable<PreparedChunk> chunks) {
            var next=CacheRanges(duration,chunks);
            bool same=next.Length==cached.Length;
            if(same) for(int i=0;i<next.Length;i++) if(next[i][0]!=cached[i][0] || next[i][1]!=cached[i][1]){same=false;break;}
            if(!same){cached=next;Invalidate();}
        }
        public double FractionAt(int x) { return Math.Max(0,Math.Min(1,(x-8)/(double)Math.Max(1,Width-16))); }
        void SetAt(int x) {
            Value=Minimum+(int)Math.Round(FractionAt(x)*(Maximum-Minimum));
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            float width=Math.Max(1,Width-16),y=Height/2f;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Rounded.Fill(e.Graphics,Color.FromArgb(61,66,80),new RectangleF(8,y-3,width,6),3);
            var saved=e.Graphics.Save();
            using(var clip=Rounded.Path(new RectangleF(8,y-3,width,6),3))e.Graphics.SetClip(clip);
            using(var cache=new SolidBrush(Color.FromArgb(49,196,178))) foreach(var range in cached)
                e.Graphics.FillRectangle(cache,8+(float)range[0]*width,y-3,Math.Max(1,(float)(range[1]-range[0])*width),6);
            float x=8+width*(Value-Minimum)/Math.Max(1,Maximum-Minimum);
            // Keep the upper half of cached history visible behind the playhead too.
            using(var played=new SolidBrush(Color.FromArgb(140,126,250))) e.Graphics.FillRectangle(played,8,y,x-8,3);
            e.Graphics.Restore(saved);
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var thumb=new SolidBrush(Color.White)) e.Graphics.FillEllipse(thumb,x-5,y-5,10,10);
            if(Focused && ShowFocusCues)using(var path=Rounded.Path(new RectangleF(2,2,Width-4,Height-4),8))using(var pen=new Pen(Color.FromArgb(98,91,150)))e.Graphics.DrawPath(pen,path);
        }
        protected override bool IsInputKey(Keys keyData) {
            Keys key=keyData&Keys.KeyCode;
            return key==Keys.Left || key==Keys.Right || key==Keys.Home || key==Keys.End || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e) {
            if(e.KeyCode==Keys.Left) Value-=Math.Max(1,(Maximum-Minimum)/200);
            else if(e.KeyCode==Keys.Right) Value+=Math.Max(1,(Maximum-Minimum)/200);
            else if(e.KeyCode==Keys.Home) Value=Minimum;
            else if(e.KeyCode==Keys.End) Value=Maximum;
            else {base.OnKeyDown(e);return;}
            e.Handled=true; base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left) { Focus(); Capture=true; SetAt(e.X); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if(Capture && e.Button==MouseButtons.Left) SetAt(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if(e.Button==MouseButtons.Left) { SetAt(e.X); Capture=false; } base.OnMouseUp(e); }
    }
    sealed class Player : Form {
        readonly Color bg = Color.FromArgb(17,19,24), muted = Color.FromArgb(153,162,179);
        Panel video = new Panel(), controls = new RoundedPanel();
        Label empty = new Label(), info = new Label(), time = new Label();
        ClickSeekBar seek = new ClickSeekBar();
        TrackBar volume = new RoundedVolume();
        readonly ToolTip seekTip=new ToolTip();
        int lastSeekTipSecond=-1, lastSeekTipX=-1000;
        bool captionHidden;
        DateTime lastCaptionInteraction=DateTime.UtcNow;
        [DllImport("user32.dll", EntryPoint="GetWindowLongW")] static extern int GetWindowStyle(IntPtr window,int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongW")] static extern int SetWindowStyle(IntPtr window,int index,int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int cx,int cy,uint flags);
        readonly List<Dictionary<string,object>> audioTracks=new List<Dictionary<string,object>>();
        int audioTracksRequest;
        bool sourceLoaded;
        string playbackNotice="";
        ComboBox quality = new RoundedComboBox(), motion = new RoundedComboBox(), protection = new RoundedComboBox();
        Button play;
        bool dialogOpen;
        int soundGain;
        readonly int[] soundBands=new int[5];
        ComboBox windowSize = new RoundedComboBox();
        readonly System.Windows.Forms.Timer chromeTimer=new System.Windows.Forms.Timer();
        DateTime lastInteraction=DateTime.UtcNow;
        Point lastPointer;
        bool previousLeft;
        FormWindowState oldState;
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        Button audioButton, subButton;
        ComboBox buffer = new RoundedComboBox();
        PreRenderSession cache;
        CacheStream cacheStream;
        double cacheOrigin;
        readonly Queue<PreparedChunk> prepared = new Queue<PreparedChunk>();
        readonly Dictionary<string,PreparedChunk> scheduled = new Dictionary<string,PreparedChunk>(StringComparer.OrdinalIgnoreCase);
        readonly List<PreparedChunk> expiredChunks=new List<PreparedChunk>();
        readonly Stopwatch cacheUiClock=Stopwatch.StartNew();
        long cacheUiUpdated;
        bool cacheTimelineDirty;
        bool cacheStarted, cacheFileLoaded;
        double position;
        string selectedAudio="auto", cacheMessage="";
        Process engine;
        StreamWriter writer;
        readonly object sendLock = new object();
        volatile bool closing;
        bool dragging, fullscreen, ready, pipelineFailed;
        Rectangle oldBounds;
        double duration;
        int requestId;
        double dropped;
        int videoParamsRequest;
        string decoder = "—";
        string currentFile, pipeName = "luma-player";
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string startupFile;
        public Player(string file) {
            startupFile = file;
            Text = "Luma Player";
            BackColor = bg; ForeColor = Color.White;
            Font = new Font("Segoe UI", 10);
            Size = new Size(1440,800); MinimumSize = new Size(1100,600);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true; AllowDrop = true;
            video.Dock = DockStyle.Fill; video.BackColor = Color.FromArgb(9,11,15);
            controls.Dock = DockStyle.None; controls.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom; controls.Height = 158; controls.BackColor = bg;
            Controls.Add(video); Controls.Add(controls); controls.BringToFront();
            empty.Text = "L U M A\n\nОткройте видео или перетащите файл сюда";
            empty.TextAlign = ContentAlignment.MiddleCenter;
            empty.Font = new Font("Segoe UI",18); empty.ForeColor = muted;
            empty.Dock = DockStyle.Fill; video.Controls.Add(empty);
            empty.Click += delegate { Open(); };
            seek.Minimum = 0; seek.Maximum = 10000;
            seek.MouseMove += delegate(object sender,MouseEventArgs e) {
                if(duration<=0 || Double.IsNaN(duration) || Double.IsInfinity(duration)) return;
                int seconds=(int)Math.Min(int.MaxValue,Math.Floor(duration*seek.FractionAt(e.X)));
                if(seconds==lastSeekTipSecond && Math.Abs(e.X-lastSeekTipX)<16) return;
                lastSeekTipSecond=seconds; lastSeekTipX=e.X;
                seekTip.Show(Clock(seconds),seek,Math.Max(0,Math.Min(seek.Width-1,e.X)),-30,2000);
            };
            seek.MouseLeave += delegate { seekTip.Hide(seek); lastSeekTipSecond=-1; lastSeekTipX=-1000; };
            seek.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            seek.SetBounds(15,8,1045,26); seek.BackColor = bg;
            seek.MouseDown += delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) dragging = true; };
            seek.MouseUp += delegate(object sender,MouseEventArgs e) { if(e.Button!=MouseButtons.Left)return; if(duration>0) SeekTo(duration*seek.Value/10000.0); dragging=false; };
            seek.KeyUp += delegate(object sender,KeyEventArgs e) { if(duration>0 && (e.KeyCode==Keys.Left || e.KeyCode==Keys.Right || e.KeyCode==Keys.Home || e.KeyCode==Keys.End)) SeekTo(duration*seek.Value/10000.0); };
            controls.Controls.Add(seek);
            var open = Button("Открыть",18,42,100, delegate { Open(); });
            play = Button("▶",126,42,48, delegate { Send("cycle","pause"); });
            Button("−10 c",182,42,65, delegate { SeekTo(position-10); });
            Button("+10 c",255,42,65, delegate { SeekTo(position+10); });
            time.SetBounds(335,48,185,25); time.Text = "00:00 / 00:00"; time.ForeColor = muted;
            controls.Controls.Add(time);
            audioButton=Button("Аудио",540,42,76,ShowAudioTracks);
            subButton=Button("Субтитры",624,42,96, delegate { Send("cycle","sub"); Send("show-text","Субтитры: ${current-tracks/sub/title} ${current-tracks/sub/lang}",2500); });
            Button("⛶",728,42,45, delegate { Fullscreen(); });
            volume.AutoSize=false; volume.SetBounds(785,44,130,30); volume.Minimum=0; volume.Maximum=100; volume.Value=70;
            volume.TickStyle=TickStyle.None; volume.BackColor=bg;
            volume.ValueChanged += delegate { Send("set_property","volume",volume.Value); };
            controls.Controls.Add(volume);
            quality.SetBounds(18,88,190,30); StyleCombo(quality);
            quality.Items.AddRange(new object[]{"Вписать в экран","Заполнить с обрезкой","Растянуть на экран"});
            quality.SelectedIndex=0; quality.SelectedIndexChanged += delegate { ApplyScreenFit(); };
            motion.SetBounds(220,88,220,30); StyleCombo(motion);
            motion.DropDownWidth=330;
            motion.Items.AddRange(new object[]{"Плавность: исходная","60 FPS · RIFE 720p","60 FPS · RIFE 900p (кэш)","60 FPS · RIFE 1080p (кэш)"});
            motion.SelectedIndex=1; motion.SelectedIndexChanged += delegate { ApplyFilters(); };
            controls.Controls.Add(quality); controls.Controls.Add(motion);
            protection.SetBounds(452,88,280,30); StyleCombo(protection); protection.DropDownWidth=330;
            protection.Items.AddRange(new object[]{"Защита: смена сцен","Защита: сцены и движение","Защита выключена"});
            protection.SelectedIndex=1; protection.SelectedIndexChanged += delegate { ApplyFilters(); };
            controls.Controls.Add(protection);
            Button("Диагностика",744,86,126,delegate { ShowGpu(); });
            windowSize.SetBounds(880,88,195,30); StyleCombo(windowSize);
            windowSize.Items.AddRange(new object[]{"Размер: по экрану","Окно: 1280 × 720","Окно: 1920 × 1080","Окно: 2560 × 1440","Окно: 2560 × 1600"});
            windowSize.SelectedIndex=0;
            windowSize.SelectedIndexChanged += delegate { if(!fullscreen) ApplyWindowSize(); };
            controls.Controls.Add(windowSize);
            buffer.SetBounds(18,132,235,30); StyleCombo(buffer);
            buffer.Items.AddRange(new object[]{"Запас перед стартом: 15 с","Запас перед стартом: 30 с","Запас перед стартом: 60 с","Запас перед стартом: 120 с"});
            buffer.SelectedIndex=1; controls.Controls.Add(buffer);
            Button("Подготовить кэш",265,130,170,delegate { BeginCache(position); });
            Button("Обычный режим",447,130,170,delegate { ReturnToLive(); });
            var cacheLabel=new Label { Text="Дисковый кэш: до 10 ГБ",ForeColor=muted };
            cacheLabel.SetBounds(630,136,300,25); controls.Controls.Add(cacheLabel);
            info.SetBounds(22,176,1030,32); info.ForeColor=muted; info.Text="Подготовка движка…";
            info.Anchor=AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            controls.Controls.Add(info);
            // Compact transport; infrequently used settings live in a separate sheet.
            protection.Visible=false; buffer.Visible=false; windowSize.Visible=false; cacheLabel.Visible=false;
            foreach(Control item in controls.Controls) {
                if(item is Button && item.Text=="Диагностика") item.Visible=false;
                if(item is Button && item.Text=="Подготовить кэш") item.SetBounds(452,88,165,36);
                if(item is Button && item.Text=="Обычный режим") item.SetBounds(627,88,150,36);
            }
            Button("Настройки",787,88,140,delegate { ShowSettings(); });
            Button("Звук",937,88,125,delegate { ShowSound(); });
            info.SetBounds(22,132,1030,22); info.Font=new Font("Segoe UI",9);
            info.AutoEllipsis=true;
            play.BackColor=Color.FromArgb(94,94,225);
            Resize += delegate { LayoutChrome(); };
            LayoutChrome();
            KeyDown += HandleKey;
            DragEnter += delegate(object s, DragEventArgs e) { if(e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect=DragDropEffects.Copy; };
            DragDrop += delegate(object s, DragEventArgs e) { var f=(string[])e.Data.GetData(DataFormats.FileDrop); if(f.Length>0) LoadFile(f[0]); };
            FormClosing += delegate { chromeTimer.Stop(); chromeTimer.Dispose(); seekTip.Dispose(); Shutdown(); };
            chromeTimer.Interval=100; chromeTimer.Tick += delegate { UpdateChrome(); }; chromeTimer.Start();
            Shown += delegate { StartEngine(); };
        }
        Button Button(string text,int x,int y,int w,Action action) {
            var b=new RoundedButton { Text=text,Tag=y<80 ? "transport" : "options", BackColor=Color.FromArgb(32,36,45), ForeColor=Color.White };
            b.Cursor=Cursors.Hand; b.FlatAppearance.MouseOverBackColor=Color.FromArgb(60,65,83); b.FlatAppearance.BorderSize=0; b.SetBounds(x,y,w,34); b.Click += delegate { action(); }; controls.Controls.Add(b); return b;
        }
        void StyleCombo(ComboBox c) { c.DropDownStyle=ComboBoxStyle.DropDownList; c.BackColor=Color.FromArgb(32,36,45); c.ForeColor=Color.White; c.FlatStyle=FlatStyle.Flat; c.DrawMode=DrawMode.OwnerDrawFixed; c.ItemHeight=Math.Max(26,Font.Height+12);
            c.DrawItem += delegate(object sender,DrawItemEventArgs e) {
                using(var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0 ? Color.FromArgb(63,65,104) : Color.FromArgb(32,36,45))) e.Graphics.FillRectangle(brush,e.Bounds);
                if(e.Index>=0) TextRenderer.DrawText(e.Graphics,c.Items[e.Index].ToString(),c.Font,new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-10,e.Bounds.Height),c.Enabled ? Color.White : muted,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            };
        }
        void UI(Action a) {
            if(closing || IsDisposed) return;
            try { BeginInvoke((Action)delegate { if(!closing && !IsDisposed) a(); }); }
            catch(InvalidOperationException) {}
        }
        void Shutdown() {
            if(closing) return;
            closing=true;
            seekTip.Dispose();
            var session=cache; cache=null;
            if(cacheStream!=null){cacheStream.Dispose();cacheStream=null;}
            // ReadPipe owns the writer. Disposing it here races its read loop
            // and can flush into a pipe that mpv has already closed.
            Send("quit");
            lock(sendLock) writer=null;
            if(session!=null) session.Dispose();
            try {
                if(engine!=null && !engine.HasExited && !engine.WaitForExit(1500)) engine.Kill();
            } catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}
        }
        void StartEngine() {
            string exe=Path.Combine(root,"engine","mpv.exe");
            if(!File.Exists(exe)) { info.Text="Движок mpv не найден"; MessageBox.Show("Рядом с приложением нужна папка engine с mpv.exe.","Luma"); return; }
            try {
                Directory.CreateDirectory(Path.Combine(root,"logs"));
                var si=new ProcessStartInfo(exe);
                si.UseShellExecute=false; si.CreateNoWindow=true;
                string runtime=Path.Combine(root,"engine","runtime");
                string vs=Path.Combine(runtime,"Lib","site-packages","vapoursynth");
                si.EnvironmentVariables["VSSCRIPT_PATH"]=Path.Combine(vs,"vsscript.dll");
                si.EnvironmentVariables["PYTHONHOME"]=runtime;
                si.EnvironmentVariables["PATH"]=runtime+";"+vs+";"+Environment.GetEnvironmentVariable("PATH");
                si.Arguments="--no-config --idle=yes --keep-open=yes --force-window=no --osc=no --input-default-bindings=no --input-vo-keyboard=no --input-cursor=no --terminal=no --vo=gpu-next --gpu-api=vulkan --hwdec=vulkan --hwdec-extra-frames=16 --hr-seek-framedrop=no --no-resume-playback --interpolation=no --scale=ewa_lanczossharp --volume=70 --wid="+video.Handle.ToInt64()+" --input-ipc-server="+pipeName+" --log-file=\""+Path.Combine(root,"logs","mpv.log")+"\"";
                engine=Process.Start(si);
                engine.EnableRaisingEvents=true;
                engine.Exited += delegate { UI(delegate { ready=false; info.Text="Движок остановлен. Перезапустите плеер."; }); };
                Task.Run((Action)ReadPipe);
            } catch(Exception ex) { info.Text="Ошибка запуска"; MessageBox.Show(ex.Message,"Luma"); }
        }
        void ReadPipe() {
            try {
                using(var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous)) {
                    pipe.Connect(15000);
                    using(var reader=new StreamReader(pipe)) {
                    var w=new StreamWriter(pipe,new System.Text.UTF8Encoding(false)) { AutoFlush=true };
                    try {
                        lock(sendLock) writer=w;
                        Send("observe_property",1,"time-pos"); Send("observe_property",2,"duration");
                        Send("observe_property",3,"pause");
                        Send("observe_property",5,"frame-drop-count"); Send("observe_property",6,"hwdec-current");
                        Send("request_log_messages","warn");
                        UI(delegate { ready=true; info.Text="Готово · откройте файл"; if(startupFile!=null) LoadFile(startupFile); });
                        string line;
                        var json=new JavaScriptSerializer();
                        while(!closing && (line=reader.ReadLine())!=null) {
                            Dictionary<string,object> obj;
                            try { obj=json.Deserialize<Dictionary<string,object>>(line); } catch { continue; }
                            if(obj.ContainsKey("request_id")) {
                                int id=Convert.ToInt32(obj["request_id"]);
                                if(id==audioTracksRequest) {
                                    object tracks=obj.ContainsKey("data") ? obj["data"] : null;
                                    UI(delegate { if(cache==null && id==audioTracksRequest) UpdateAudioTracks(tracks); });
                                    continue;
                                }
                                if(id==videoParamsRequest) {
                                    var parameters=obj.ContainsKey("data") ? obj["data"] as Dictionary<string,object> : null;
                                    UI(delegate {
                                        if(parameters!=null) ApplyFilters();
                                    });
                                    continue;
                                }
                            }
                            if(obj.ContainsKey("error") && Convert.ToString(obj["error"])!="success") {
                                string error=Convert.ToString(obj["error"]);
                                UI(delegate { info.Text="Команда не выполнена: "+error+" · см. logs/mpv.log"; });
                            }
                            if(!obj.ContainsKey("event")) continue;
                            string ev=Convert.ToString(obj["event"]);
                            if(ev=="log-message" && obj.ContainsKey("text")) {
                                string message=Convert.ToString(obj["text"]);
                                if(message.Contains("Disabling filter") || message.Contains("Creating filter") || message.Contains("Script evaluation failed")) {
                                    UI(delegate { pipelineFailed=true; UpdateStatus(); });
                                }
                            }
                            if(ev=="file-loaded") UI(delegate { empty.Visible=false; if(cache!=null) { cacheFileLoaded=cacheStarted; } else { sourceLoaded=true; ReadSourceParams(); } });
                            if(ev=="end-file" && obj.ContainsKey("reason") && Convert.ToString(obj["reason"])=="eof")
                                UI(delegate { if(cache!=null) CacheEnded(); });
                            if(ev=="end-file" && obj.ContainsKey("reason") && Convert.ToString(obj["reason"])=="error")
                                UI(delegate { empty.Visible=true; info.Text="Не удалось открыть файл · см. logs/mpv.log"; });
                            if(ev!="property-change" || !obj.ContainsKey("data") || obj["data"]==null) continue;
                            string name=Convert.ToString(obj["name"]); object data=obj["data"];
                            UI(delegate {
                                if(name=="duration" && cache==null) duration=Convert.ToDouble(data);
                                if(name=="pause") play.Text=Convert.ToBoolean(data) ? "▶" : "Ⅱ";
                                if(name=="time-pos" && (cache==null || cacheFileLoaded)) { double t=Convert.ToDouble(data)+(cache!=null ? cacheOrigin : 0); position=t; time.Text=Clock(t)+" / "+Clock(duration); if(!dragging && duration>0) seek.Value=Math.Max(0,Math.Min(10000,(int)(t/duration*10000))); if(cache!=null) UpdateCacheStatus(false); }
                                if(name=="frame-drop-count") { dropped=Convert.ToDouble(data); UpdateStatus(); }
                                if(name=="hwdec-current") { decoder=Convert.ToString(data); UpdateStatus(); }
                            });
                        }
                    } finally { CloseWriter(w); }
                    }
                }
            } catch(Exception ex) { UI(delegate { info.Text="Соединение с движком: "+ex.Message; }); }
            finally { lock(sendLock) writer=null; }
        }
        void CloseWriter(StreamWriter owned) {
            // StreamWriter.Dispose is not thread safe with WriteLine/Flush. The
            // read loop must take the same lock as Send before clearing buffers.
            lock(sendLock) {
                if(Object.ReferenceEquals(writer,owned))writer=null;
                try {owned.Dispose();} catch(IOException) {} catch(ObjectDisposedException) {}
            }
        }
        void Send(params object[] command) {
            lock(sendLock) {
                if(writer==null) return;
                try { writer.WriteLine(new JavaScriptSerializer().Serialize(new { command=command, request_id=++requestId })); }
                catch(IOException) { }
                catch(ObjectDisposedException) { }
            }
        }
        void ReadSourceParams() {
            lock(sendLock) {
                if(writer==null) return;
                videoParamsRequest=++requestId;
                audioTracksRequest=++requestId;
                var json=new JavaScriptSerializer();
                try {
                    writer.WriteLine(json.Serialize(new { command=new object[]{"get_property","video-params"}, request_id=videoParamsRequest }));
                    writer.WriteLine(json.Serialize(new { command=new object[]{"get_property","track-list"}, request_id=audioTracksRequest }));
                } catch(IOException) {} catch(ObjectDisposedException) {}
            }
        }
        void Open() {
            using(var d=new OpenFileDialog()) {
                d.Title="Открыть видео или аудио";
                d.Filter="Медиафайлы|*.mkv;*.mp4;*.avi;*.mov;*.webm;*.m4v;*.ts;*.mts;*.m2ts;*.mpg;*.mpeg;*.wmv;*.flv;*.vob;*.ogv;*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.wma;*.aiff;*.ape|Все файлы|*.*";
                if(d.ShowDialog()==DialogResult.OK) LoadFile(d.FileName);
            }
        }
        void LoadFile(string path) {
            if(!File.Exists(path)) { MessageBox.Show("Файл не найден: "+path,"Luma"); return; }
            if(!ready) { startupFile=path; return; }
            CancelCache(); currentFile=path; duration=0; position=0; seek.Value=0;
            selectedAudio="auto"; sourceLoaded=false; audioTracksRequest=0; audioTracks.Clear(); playbackNotice="";
            Text=Path.GetFileName(path)+" — Luma Player";
            Send("set_property","vf","");
            Send("loadfile",path,"replace",-1,new Dictionary<string,string>{{"aid","auto"}});
            Send("set_property","pause",false);
        }
        void UpdateAudioTracks(object data) {
            audioTracks.Clear();
            var tracks=data as System.Collections.IEnumerable;
            if(tracks==null)return;
            foreach(object item in tracks) {
                var track=item as Dictionary<string,object>;
                if(track!=null && track.ContainsKey("type") && Convert.ToString(track["type"])=="audio" && track.ContainsKey("id")) audioTracks.Add(track);
            }
        }
        void ShowAudioTracks() {
            if(cache!=null || !sourceLoaded)return;
            var menu=new ContextMenuStrip {BackColor=bg,ForeColor=Color.White,ShowImageMargin=false};
            if(audioTracks.Count==0) menu.Items.Add(new ToolStripMenuItem("В файле нет аудиодорожек") {Enabled=false});
            foreach(var track in audioTracks) {
                string id=PreRenderSession.NormalizeAudioTrack(track["id"]);
                string title=track.ContainsKey("title") ? Convert.ToString(track["title"]) : "Дорожка "+id;
                string lang=track.ContainsKey("lang") ? Convert.ToString(track["lang"]) : "";
                var item=new ToolStripMenuItem(title+(lang.Length>0 ? " · "+lang : ""));
                item.Checked=selectedAudio==id || (selectedAudio=="auto" && track.ContainsKey("selected") && Convert.ToBoolean(track["selected"]));
                item.Click+=delegate { SelectAudioTrack(id); };
                menu.Items.Add(item);
            }
            dialogOpen=true; ShowChrome();
            menu.Closed+=delegate {dialogOpen=false; ShowChrome(); UI(delegate {menu.Dispose();});};
            menu.Show(audioButton,new Point(0,audioButton.Height));
        }
        void SelectAudioTrack(string id) {
            if(cache!=null || !sourceLoaded)return;
            id=PreRenderSession.NormalizeAudioTrack(id);
            if(id=="no")id="auto";
            selectedAudio=id; Send("set_property","aid",id); Send("set_property","mute",false);
        }
        int ProcessingHeight { get { return motion.SelectedIndex==2 ? 900 : motion.SelectedIndex==3 ? 1080 : 720; } }
        void ApplyFilters() {
            if(!ready || cache!=null) return;
            pipelineFailed=false;
            bool rife=motion.SelectedIndex==1;
            Send("set_property","vf","");
            Send("set_property","hwdec",rife ? "auto-copy" : "vulkan");
            string graph="";
            if(rife) {
                string path=Path.Combine(root,"engine","gpu",PreRenderSession.RifeScript(23,720)).Replace('\\','/');
                graph="@luma:vapoursynth=file=%"+System.Text.Encoding.UTF8.GetByteCount(path)+"%"+path+":buffered-frames=4:concurrent-frames=4:user-data=60|720|"+protection.SelectedIndex+"|23|2,@cadence:lavfi=[fps=fps=60:round=near]";
            }
            Send("set_property","vf",graph);
            ApplyScreenFit();
            UpdateStatus();
        }
        void SetCacheControls(bool enabled) {
            motion.Enabled=protection.Enabled=buffer.Enabled=enabled;
            audioButton.Enabled=subButton.Enabled=enabled;
        }
        void CancelCache() {
            var old=cache; cache=null;
            if(cacheStream!=null){cacheStream.Dispose();cacheStream=null;}
            if(old!=null) { Send("stop"); old.Dispose(); }
            prepared.Clear(); scheduled.Clear(); expiredChunks.Clear(); cacheTimelineDirty=false; cacheStarted=false; cacheFileLoaded=false;
            seek.SetCache(0,new PreparedChunk[0]);
            Send("set_property","keep-open","yes");
            SetCacheControls(true);
        }
        void ReturnToLive() {
            if(cache==null) return;
            double resume=position;
            CancelCache();
            sourceLoaded=false;
            Send("set_property","vf","");
            Send("loadfile",currentFile,"replace",-1,new Dictionary<string,string>{{"aid",selectedAudio},{"start",resume.ToString("0.######",System.Globalization.CultureInfo.InvariantCulture)}});
            Send("set_property","pause",false);
        }
        void SeekTo(double target) {
            target=Math.Max(0,Math.Min(Math.Max(0,duration-0.1),target));
            if(cache!=null) { if(!SeekPrepared(target)) BeginCache(target); }
            else Send("seek",target,"absolute+exact");
        }
        bool SeekPrepared(double target) {
            var available=new List<PreparedChunk>(scheduled.Values);
            available.AddRange(prepared);
            available.Sort(delegate(PreparedChunk a,PreparedChunk b){return a.Start.CompareTo(b.Start);});
            int index=available.FindIndex(delegate(PreparedChunk c){return target>=c.Start && target<c.Start+c.Length && File.Exists(c.Path);});
            if(index<0)return false;
            // Replace only the reader. The producer, model and disk cache remain alive.
            cacheFileLoaded=false;
            Send("stop");
            if(cacheStream!=null)cacheStream.Dispose();
            var session=cache;
            cacheStream=new CacheStream();
            var reader=cacheStream;
            reader.Failed=delegate(string message){UI(delegate{if(cache==session && cacheStream==reader){cacheMessage="Ошибка потока кэша: "+message;UpdateCacheStatus();}});};
            while(prepared.Count>0){var chunk=prepared.Dequeue();scheduled[chunk.Path]=chunk;}
            cacheOrigin=available[index].Start; position=target;
            for(int i=index;i<available.Count;i++)reader.Add(available[i]);
            if(cache.Completed)reader.Finish();
            cacheStarted=true;
            LoadCacheStream(target-cacheOrigin);
            cacheMessage="Переход в готовый кэш · без повторной подготовки";
            UpdateCacheStatus();return true;
        }
        void LoadCacheStream(double offset) {
            Send("loadfile",cacheStream.Url,"replace",-1,new Dictionary<string,string>{{"aid","auto"},{"demuxer","lavf"},{"demuxer-lavf-format","mpegts"},{"cache","yes"},{"cache-secs","5"},{"demuxer-max-bytes","32MiB"},{"demuxer-max-back-bytes","0"},{"force-seekable","yes"},{"hr-seek","yes"},{"start",offset.ToString("0.######",System.Globalization.CultureInfo.InvariantCulture)}});
            Send("set_property","pause",false);
        }
        void BeginCache(double from) {
            if(!ready || currentFile==null || duration<=0 || (cache==null && !sourceLoaded)) { info.Text="Дождитесь открытия видео"; return; }
            playbackNotice="";
            CancelCache();
            from=Math.Max(0,Math.Min(duration-0.1,from));
            // Cached rendering uses RIFE and NVENC, independent of live Optical Flow.
            if(motion.SelectedIndex==0) motion.SelectedIndex=1;
            int fps=60;
            PreRenderSession session;
            try { session=new PreRenderSession(root,currentFile,from,duration,fps,ProcessingHeight,
                protection.SelectedIndex,false,selectedAudio,15,PreRenderSession.DefaultLimit,23,true,true); }
            catch(Exception ex) { MessageBox.Show("Не удалось создать кэш: "+ex.Message,"Luma"); return; }
            cache=session; position=from; cacheOrigin=from;
            cacheStream=new CacheStream();
            cacheStream.Failed=delegate(string message){UI(delegate{if(cache==session){cacheMessage="Ошибка потока кэша: "+message;UpdateCacheStatus();}});};
            session.Measured=delegate(double seconds,double elapsed) { UI(delegate { if(cache!=session) return; cacheMessage="Подготовка "+(seconds/elapsed).ToString("0.00")+"×"+(seconds<elapsed ? " · запас расходуется быстрее пополнения" : ""); }); }; cacheMessage="Подготовка начального запаса…";
            SetCacheControls(false);
            Send("stop"); Send("set_property","keep-open","no"); Send("set_property","vf",""); Send("set_property","hwdec","vulkan");
            Send("set_property","prefetch-playlist",true);
            Send("set_property","gapless-audio","yes");
            session.Ready=delegate(PreparedChunk chunk) { UI(delegate {
                if(cache!=session) return;
                prepared.Enqueue(chunk); cacheTimelineDirty=true; StartPrepared(false); UpdateCacheStatus();
            }); };
            session.Status=delegate(string message) { UI(delegate {
                if(cache!=session) return;
                cacheMessage=message; StartPrepared(session.Completed); UpdateCacheStatus();
            }); };
            session.Failed=delegate(string message) { UI(delegate {
                if(cache!=session) return;
                cacheMessage="Подготовка остановлена · откройте диагностику";
                Directory.CreateDirectory(Path.Combine(root,"logs"));
                File.WriteAllText(Path.Combine(root,"logs","prerender-error.txt"),message);
                if(!cacheStarted && prepared.Count==0) {
                    ReturnToLive();
                    playbackNotice=message.StartsWith("Исходный файл") ? "Файл повреждён или недокачан · кэш остановлен · подробности в диагностике" : "Не удалось подготовить кэш · включён исходник · подробности в диагностике";
                    UpdateStatus();return;
                }
                StartPrepared(true);
                UpdateCacheStatus();
            }); };
            session.Start(); UpdateCacheStatus();
        }
        void StartPrepared(bool finished) {
            if(cache==null) return;
            if(prepared.Count==0){if(cacheStream!=null && (finished || cache.Completed))cacheStream.Finish();return;}
            double seconds=0; foreach(var chunk in prepared) seconds+=chunk.Length;
            int requested=new int[]{15,30,60,120}[buffer.SelectedIndex];
            if(!cacheStarted && !finished && seconds+0.01<requested) return;
            while(prepared.Count>0) {
                var chunk=prepared.Dequeue(); scheduled.Add(chunk.Path,chunk); cacheStream.Add(chunk);
            }
            if(!cacheStarted) {
                cacheStarted=true; cacheFileLoaded=false;
                LoadCacheStream(0);
            }
            if(finished || cache.Completed) cacheStream.Finish();
        }
        void AdvanceStream() {
            if(cache==null)return;
            expiredChunks.Clear();
            foreach(var chunk in scheduled.Values) {
                if(position>=chunk.Start+chunk.Length+30)expiredChunks.Add(chunk);
            }
            foreach(var chunk in expiredChunks){scheduled.Remove(chunk.Path);cache.Consume(chunk);cacheTimelineDirty=true;}
        }
        void CacheEnded() {
            if(cache==null)return;
            if(cache.Completed)position=duration;
            AdvanceStream();cacheFileLoaded=false;UpdateCacheStatus();
        }
        void UpdateCacheStatus() { UpdateCacheStatus(true); }
        void UpdateCacheStatus(bool force) {
            if(cache==null) return;
            long now=cacheUiClock.ElapsedMilliseconds;
            if(!force && now-cacheUiUpdated<250)return;
            cacheUiUpdated=now;
            AdvanceStream();
            if(cacheTimelineDirty) {
                var ranges=new List<PreparedChunk>(scheduled.Values); ranges.AddRange(prepared);
                seek.SetCache(duration,ranges); cacheTimelineDirty=false;
            }
            double ahead=0; foreach(var chunk in prepared) ahead+=chunk.Length;
            foreach(var chunk in scheduled.Values) ahead+=Math.Max(0,chunk.Start+chunk.Length-Math.Max(position,chunk.Start));
            cache.UpdatePlaybackBuffer(ahead,cacheStarted);
            string state=cacheStarted && cacheFileLoaded ? "Просмотр кэша" : cache.Completed && prepared.Count==0 ? "Просмотр завершён" : "Ожидание готового видео";
            info.Text=state+" · впереди "+ahead.ToString("0")+" с · "+(cache.Bytes/(1024.0*1024*1024)).ToString("0.00")+" / 10 ГБ · "+(cache.LastSpeed>0 ? cache.LastSpeed.ToString("0.00")+"× · " : "")+(cache.LastSpeed>0 && cache.LastSpeed<1 ? "медленнее просмотра · " : "")+cacheMessage;
        }
        void UpdateStatus() {
            if(cache!=null) { UpdateCacheStatus(); return; }
            if(playbackNotice.Length>0) {info.Text=playbackNotice;return;}
            if(pipelineFailed) { info.Text="GPU-фильтр не запущен · откройте диагностику"; return; }
            if(motion.SelectedIndex>=2){info.Text="RIFE "+ProcessingHeight+"p · эксперимент · сейчас оригинал · нажмите «Подготовить кэш»";return;}
            string mode=motion.SelectedIndex==0 ? "Оригинал" : "RIFE 720p";
            int target=60;
            info.Text=mode+(motion.SelectedIndex>0 ? " · цель "+target+" FPS" : "")+" · пропуски вывода "+dropped.ToString("0")+
                (motion.SelectedIndex>0 ? (protection.SelectedIndex==2 ? " · без защиты" : protection.SelectedIndex==1 ? " · осторожный режим" : " · защита сцен") : "");
        }
        void LayoutChrome() {
            int margin=16,pad=18,gap=8,width=Math.Max(320,ClientSize.Width-margin*2);
            int rowHeight=Math.Max(36,Font.Height+16),y=46;
            controls.SuspendLayout();
            controls.Width=width;
            seek.SetBounds(pad,10,width-pad*2,26);
            for(int row=0;row<2;row++) {
                int x=pad;
                foreach(Control item in controls.Controls) {
                    bool transport=(item is Button && Convert.ToString(item.Tag)=="transport") || item==time || item==volume;
                    bool options=(item is Button && Convert.ToString(item.Tag)=="options" && item.Text!="Диагностика") || item==quality || item==motion;
                    if(row==0 ? !transport : !options)continue;
                    int preferred;
                    if(item==volume)preferred=140;
                    else if(item==time)preferred=TextRenderer.MeasureText("0:00:00 / 0:00:00",Font).Width+18;
                    else if(item==quality || item==motion) {
                        preferred=0;
                        foreach(object choice in ((ComboBox)item).Items)preferred=Math.Max(preferred,TextRenderer.MeasureText(choice.ToString(),Font).Width+46);
                        ((ComboBox)item).DropDownWidth=preferred;
                    } else preferred=Math.Max(46,TextRenderer.MeasureText(item.Text,Font).Width+30);
                    preferred=Math.Min(preferred,width-pad*2);
                    if(x>pad && x+preferred>width-pad){x=pad;y+=rowHeight+gap;}
                    item.SetBounds(x,y,preferred,rowHeight);
                    if(item==time)((Label)item).TextAlign=ContentAlignment.MiddleLeft;
                    x+=preferred+gap;
                }
                y+=rowHeight+gap;
            }
            info.SetBounds(pad,y+2,width-pad*2,Math.Max(22,info.Font.Height+4));
            int height=info.Bottom+14;
            controls.SetBounds(margin,Math.Max(0,ClientSize.Height-height-margin),width,height);
            controls.ResumeLayout(false);
        }
        Form SettingsSheet(string title,int height) {
            return new Form { Text=title,Font=Font,BackColor=bg,ForeColor=Color.White,ClientSize=new Size(500,height),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false };
        }
        void ShowSettings() {
            dialogOpen=true; ShowChrome();
            Control[] items={protection,buffer,windowSize};
            int labelHeight=Font.Height+4,fieldHeight=Math.Max(36,Font.Height+18),rowHeight=labelHeight+fieldHeight+14,buttonHeight=Math.Max(36,Font.Height+14);
            using(var sheet=SettingsSheet("Настройки просмотра",20+rowHeight*3+buttonHeight+36)) {
                string[] labels={"Интерполяция","Подготовка видео","Размер окна"};
                for(int i=0;i<items.Length;i++) {
                    sheet.Controls.Add(new Label { Text=labels[i],Bounds=new Rectangle(24,20+i*rowHeight,440,labelHeight),ForeColor=muted });
                    sheet.Controls.Add(items[i]); items[i].SetBounds(24,24+labelHeight+i*rowHeight,450,fieldHeight); items[i].Visible=true;
                }
                var diagnostics=new RoundedButton {Text="Диагностика",Bounds=new Rectangle(24,24+rowHeight*3,180,buttonHeight)};
                diagnostics.Click+=delegate {ShowGpu();}; sheet.Controls.Add(diagnostics);
                try {sheet.ShowDialog(this);} finally { foreach(var item in items) {item.Visible=false; controls.Controls.Add(item);} dialogOpen=false; ShowChrome();}
            }
        }
        static string AudioGraph(int gain,int[] bands) {
            bool enabled=gain!=0; foreach(int band in bands) enabled|=band!=0;
            if(!enabled) return "";
            int[] frequencies={100,300,1000,3000,10000};
            var filters=new List<string>();
            for(int i=0;i<5;i++) if(bands[i]!=0) filters.Add("equalizer=f="+frequencies[i]+":t=o:w=1:g="+bands[i]);
            filters.Add("volume="+gain+"dB");
            filters.Add("alimiter=limit=0.95:level=false:latency=true");
            return "@luma_sound:lavfi=["+String.Join(",",filters.ToArray())+"]";
        }
        void ShowSound() {
            dialogOpen=true; ShowChrome();
            int headerHeight=Font.Height*2+4,rowHeight=Math.Max(43,Font.Height+18),start=18+headerHeight+16;
            int hintTop=start+rowHeight*6+6,buttonY=hintTop+Font.Height*2+20,buttonHeight=Math.Max(36,Font.Height+14);
            using(var sheet=SettingsSheet("Звук · эквалайзер",buttonY+buttonHeight+24)) {
                sheet.Controls.Add(new Label {Text="Усиление тихого звука и настройка тембра",Bounds=new Rectangle(24,18,450,headerHeight),ForeColor=muted});
                string[] names={"Усиление, дБ","Бас · 100 Гц","300 Гц","Голос · 1 кГц","Чёткость · 3 кГц","Высокие · 10 кГц"};
                var values=new NumericUpDown[6];
                for(int i=0;i<6;i++) {
                    sheet.Controls.Add(new Label {Text=names[i],Bounds=new Rectangle(24,start+4+i*rowHeight,270,Font.Height+6)});
                    values[i]=new NumericUpDown {Minimum=i==0 ? 0 : -9,Maximum=i==0 ? 12 : 9,Value=i==0 ? soundGain : soundBands[i-1],Bounds=new Rectangle(340,start+i*rowHeight,120,Font.Height+12),BackColor=Color.FromArgb(32,36,45),ForeColor=Color.White};
                    values[i].BorderStyle=BorderStyle.None;
                    values[i].SizeChanged+=delegate(object sender,EventArgs e){Rounded.Region((Control)sender,7);};
                    Rounded.Region(values[i],7);
                    sheet.Controls.Add(values[i]);
                }
                sheet.Controls.Add(new Label {Text="Ограничитель пиков включается\nвместе с обработкой.",Bounds=new Rectangle(24,hintTop,450,Font.Height*2+4),ForeColor=muted});
                var apply=new RoundedButton {Text="Применить",Bounds=new Rectangle(300,buttonY,160,buttonHeight),BackColor=Color.FromArgb(94,94,225)};
                apply.Click+=delegate {soundGain=(int)values[0].Value;for(int i=0;i<5;i++)soundBands[i]=(int)values[i+1].Value;Send("set_property","af",AudioGraph(soundGain,soundBands));sheet.Close();}; sheet.Controls.Add(apply);
                var reset=new RoundedButton {Text="Сбросить",Bounds=new Rectangle(24,buttonY,140,buttonHeight)};
                reset.Click+=delegate {foreach(var value in values)value.Value=0;};sheet.Controls.Add(reset);
                try {sheet.ShowDialog(this);} finally {dialogOpen=false;ShowChrome();}
            }
        }
        void ShowGpu() {
            MessageBox.Show("Сборка 0.3.3 exp · кэш кадров 1080p: 1024 МиБ · запас до 240 с\n"+
                "RIFE "+ProcessingHeight+"p: генерация 60 FPS на GPU с последующим масштабированием под экран.\n\n"+
                "Кэш до 10 ГБ; просмотренные части удаляются. Бирюзовые участки шкалы уже готовы: переход внутри них не перезапускает подготовку.\n\n"+
                "Декодер: "+decoder+"\nПропуски вывода: "+dropped.ToString("0")+"\n"+
                (playbackNotice.Length>0 && File.Exists(Path.Combine(root,"logs","prerender-error.txt")) ? File.ReadAllText(Path.Combine(root,"logs","prerender-error.txt")) : cache!=null ? cacheMessage : "Для подготовки видео заранее нажмите «Подготовить кэш»."),"Диагностика");
        }
        void ApplyScreenFit() {
            Send("set_property","keepaspect",quality.SelectedIndex!=2);
            Send("set_property","panscan",quality.SelectedIndex==1 ? 1.0 : 0.0);
        }
        void ApplyWindowSize() {
            var area=Screen.FromControl(this).WorkingArea;
            var sizes=new Size[]{area.Size,new Size(1280,720),new Size(1920,1080),new Size(2560,1440),new Size(2560,1600)};
            WindowState=FormWindowState.Normal;
            Size wanted=sizes[Math.Max(0,windowSize.SelectedIndex)];
            Size=new Size(Math.Min(wanted.Width,area.Width),Math.Min(wanted.Height,area.Height));
            Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        }
        void SetCaptionVisible(bool visible) {
            if(!IsHandleCreated || fullscreen || captionHidden==!visible) return;
            const int GWL_STYLE=-16, WS_CAPTION=0x00C00000;
            int style=GetWindowStyle(Handle,GWL_STYLE);
            int next=visible ? style|WS_CAPTION : style & ~WS_CAPTION;
            SetWindowStyle(Handle,GWL_STYLE,next);
            SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0004|0x0010|0x0020);
            captionHidden=!visible;
            LayoutChrome();
        }
        void ShowChrome() { controls.Visible=true; controls.BringToFront(); lastInteraction=DateTime.UtcNow; }
        void UpdateChrome() {
            if(closing) return;
            if(currentFile==null || dialogOpen) { lastInteraction=DateTime.UtcNow; lastCaptionInteraction=DateTime.UtcNow; if(!fullscreen) SetCaptionVisible(true); return; }
            Point pointer=Cursor.Position, local=PointToClient(pointer);
            if(!fullscreen && WindowState!=FormWindowState.Minimized) {
                Rectangle b=Bounds;
                bool nearTop=pointer.X>=b.Left && pointer.X<b.Right && pointer.Y>=b.Top && pointer.Y<b.Top+48;
                if(GetForegroundWindow()==Handle && nearTop) { lastCaptionInteraction=DateTime.UtcNow; SetCaptionVisible(true); }
                else if((DateTime.UtcNow-lastCaptionInteraction).TotalSeconds>3) SetCaptionVisible(false);
            }
            bool inside=ClientRectangle.Contains(local), left=(GetAsyncKeyState(1)&0x8000)!=0;
            bool moved=pointer!=lastPointer;
            bool menu=quality.DroppedDown || motion.DroppedDown || protection.DroppedDown || buffer.DroppedDown || windowSize.DroppedDown;
            if(GetForegroundWindow()==Handle && inside && ((left && !previousLeft) || (moved && local.Y>=ClientSize.Height-48))) ShowChrome();
            if(menu || dragging || (controls.Visible && inside && local.Y>=controls.Top && (moved || left))) lastInteraction=DateTime.UtcNow;
            if(!menu && !dragging && (DateTime.UtcNow-lastInteraction).TotalSeconds>3) controls.Visible=false;
            lastPointer=pointer; previousLeft=left;
        }
        void Fullscreen() {
            if(!fullscreen) {
                SetCaptionVisible(true);
                var screen=Screen.FromControl(this).Bounds;
                oldState=WindowState; oldBounds=oldState==FormWindowState.Normal ? Bounds : RestoreBounds;
                fullscreen=true; WindowState=FormWindowState.Normal; FormBorderStyle=FormBorderStyle.None; Bounds=screen;
                // Overlay controls instead of resizing the video when the panel appears.
                controls.Dock=DockStyle.None;
                controls.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;
                LayoutChrome();
                ShowChrome();
            } else {
                fullscreen=false; controls.Visible=true; controls.Dock=DockStyle.None;
                FormBorderStyle=FormBorderStyle.Sizable; captionHidden=false; lastCaptionInteraction=DateTime.UtcNow; Bounds=oldBounds; WindowState=oldState; LayoutChrome(); ShowChrome();
            }
        }
        void HandleKey(object sender,KeyEventArgs e) {
            if(e.KeyCode==Keys.Escape && (quality.DroppedDown || motion.DroppedDown || protection.DroppedDown || buffer.DroppedDown || windowSize.DroppedDown))return;
            if((quality.ContainsFocus || motion.ContainsFocus || protection.ContainsFocus || volume.ContainsFocus || seek.ContainsFocus)
                && (e.KeyCode==Keys.Left || e.KeyCode==Keys.Right)) return;
            if(e.Control && e.KeyCode==Keys.O) Open();
            else if(e.KeyCode==Keys.Space) Send("cycle","pause");
            else if(e.KeyCode==Keys.F11) Fullscreen();
            else if(e.KeyCode==Keys.Escape && fullscreen) Fullscreen();
            else if(e.KeyCode==Keys.Left) SeekTo(position-5);
            else if(e.KeyCode==Keys.Right) SeekTo(position+5);
            else return;
            e.SuppressKeyPress=true;
        }
        static string Clock(double s) { var t=TimeSpan.FromSeconds(Math.Max(0,s)); return s>=3600 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss"); }
    }
}


