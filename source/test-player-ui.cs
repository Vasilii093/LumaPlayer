using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using Luma;
class PlayerUiTests {
 static BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(Player p,string n){return typeof(Player).GetField(n,F).GetValue(p);}
 static void Call(Player p,string n){typeof(Player).GetMethod(n,F).Invoke(p,null);}
 static void Check(bool ok,string s){if(!ok)throw new Exception(s);}
 [STAThread] static void Main(){
  using(var p=new Player(null)) using(var ms=new MemoryStream()) {
   var key=typeof(Form).GetField("EVENT_SHOWN",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
   var events=(System.ComponentModel.EventHandlerList)typeof(System.ComponentModel.Component).GetProperty("Events",F).GetValue(p,null);
   events.RemoveHandler(key,events[key]);
   p.Show(); Application.DoEvents();
   var h=p.Handle;
   var w=new StreamWriter(ms,new UTF8Encoding(false)){AutoFlush=true};
   typeof(Player).GetField("writer",F).SetValue(p,w);
   typeof(Player).GetField("ready",F).SetValue(p,true);
   var mode=(ComboBox)Field(p,"motion");
   Check(mode.Items.Count==7,"Unexpected modes");
   mode.SelectedIndex=0; ms.SetLength(0); mode.SelectedIndex=1;
   Check(Encoding.UTF8.GetString(ms.ToArray()).Contains("interpolate-rife-resident.vpy"),"Native RIFE missing");
   var heightProperty=typeof(Player).GetProperty("ProcessingHeight",F);
   var fpsProperty=typeof(Player).GetProperty("ProcessingFps",F);
   int[] heights={720,720,720,900,900,1020,1080};
   int[] rates={60,60,90,60,72,60,60};
   for(int i=0;i<heights.Length;i++) {
    ms.SetLength(0);mode.SelectedIndex=i;
    Check((int)heightProperty.GetValue(p,null)==heights[i],"Wrong height at "+i);
    Check((int)fpsProperty.GetValue(p,null)==rates[i],"Wrong FPS at "+i);
    if(i>=2)Check(!Encoding.UTF8.GetString(ms.ToArray()).Contains("vapoursynth"),"Cache mode started live filter");
   }
   Check(PreRenderSession.RifeScript(23,900)==(PreRenderSession.RifeThroughput ? "interpolate-rife-throughput.vpy" : "interpolate-rife-resident.vpy") && PreRenderSession.RifeScript(23,1080)=="interpolate-rife-resident.vpy","HD native cache missing");
   mode.SelectedIndex=1;
   var seek=(ClickSeekBar)Field(p,"seek"); var sh=seek.Handle;
   typeof(Player).GetField("duration",F).SetValue(p,100.0);
   var click=typeof(ClickSeekBar).GetMethod("OnMouseUp",F);
   ms.SetLength(0); click.Invoke(seek,new object[]{new MouseEventArgs(MouseButtons.Left,1,seek.Width/2,10,0)});
   Check(Math.Abs(seek.Value-5000)<100,"Seek click not mapped to midpoint");
   Check(Encoding.UTF8.GetString(ms.ToArray()).Contains("absolute+exact"),"Seek command absent");
   click.Invoke(seek,new object[]{new MouseEventArgs(MouseButtons.Left,1,0,10,0)}); Check(seek.Value==0,"Seek start");
   click.Invoke(seek,new object[]{new MouseEventArgs(MouseButtons.Left,1,seek.Width,10,0)}); Check(seek.Value==10000,"Seek end");
   var bounds=p.Bounds; Call(p,"Fullscreen");
   var panel=(Panel)Field(p,"controls"); var video=(Panel)Field(p,"video");
   Check(panel.Dock==DockStyle.None,"Fullscreen panel is not overlay");
   var vb=video.Bounds; panel.Hide(); p.PerformLayout(); Check(video.Bounds==vb,"Hiding panel resizes video");
   Call(p,"Fullscreen"); Check(p.Bounds==bounds && panel.Dock==DockStyle.None,"Window restore failed");
   var fit=(ComboBox)Field(p,"quality"); ms.SetLength(0); fit.SelectedIndex=1;
   Check(Encoding.UTF8.GetString(ms.ToArray()).Contains("panscan"),"Fill command absent");
   typeof(Player).GetField("currentFile",F).SetValue(p,"test.mkv");
   typeof(Player).GetField("lastInteraction",F).SetValue(p,DateTime.UtcNow.AddSeconds(-4));
   typeof(Player).GetField("lastPointer",F).SetValue(p,Cursor.Position);
   Call(p,"UpdateChrome"); Check(!panel.Visible,"Windowed autohide failed");
   Call(p,"ShowChrome"); Check(panel.Visible,"Panel reveal failed");
   Check(((ComboBox)Field(p,"windowSize")).Items.Count==5,"1600p absent");
   Check(!((ComboBox)Field(p,"protection")).Visible,"Protection still in main UI");
   var graph=(string)typeof(Player).GetMethod("AudioGraph",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{6,new int[]{2,0,3,0,-2}});
   File.WriteAllText("work/audio-test-graph.txt",graph);
   using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,new Rectangle(0,0,panel.Width,panel.Height));bitmap.Save("work/modern-ui-preview.png");}
   var session=new PreRenderSession(Path.GetFullPath("outputs/LumaPlayer-Optimized"),"unused",0,100,60,720,1,false,"auto",15,PreRenderSession.DefaultLimit,23,true,true);
   typeof(Player).GetField("cache",F).SetValue(p,session);
   var chunks=(System.Collections.Generic.Dictionary<string,PreparedChunk>)Field(p,"scheduled");
   for(int i=0;i<3;i++){string file=Path.Combine(session.Folder,i.ToString("D6")+".ts");File.WriteAllBytes(file,new byte[188]);chunks.Add(file,new PreparedChunk{Path=file,Start=i*15,Length=15});}
   var seekPrepared=typeof(Player).GetMethod("SeekPrepared",F);
   Check((bool)seekPrepared.Invoke(p,new object[]{20.0}),"Cached seek not handled");
   Check(Object.ReferenceEquals(Field(p,"cache"),session)&&session.Work==null,"Producer replaced or started");
   Check((double)Field(p,"cacheOrigin")==15.0,"Wrong cache origin");
   Check((bool)seekPrepared.Invoke(p,new object[]{5.0}),"Cached backwards seek failed");
   Check(!(bool)seekPrepared.Invoke(p,new object[]{55.0}),"Unprepared seek wrongly accepted");
   Check(chunks.Count==3,"Cached seek discarded chunks");
   // Injected test chunks bypass Ready; mark the timeline just as that event does.
   typeof(Player).GetField("cacheTimelineDirty",F).SetValue(p,true);
   var refresh=typeof(Player).GetMethod("UpdateCacheStatus",F,null,Type.EmptyTypes,null);
   refresh.Invoke(p,null);
   var rangeField=typeof(ClickSeekBar).GetField("cached",F);
   Check(((double[][])rangeField.GetValue(seek)).Length==1,"Initial cache range missing");
   typeof(Player).GetField("position",F).SetValue(p,61.0);
   refresh.Invoke(p,null);
   var retained=(double[][])rangeField.GetValue(seek);
   Check(chunks.Count==1 && retained.Length==1 && retained[0][0]==.3 && retained[0][1]==.45,"Expired ranges retained or unplayed cache deleted");
   Check(Encoding.UTF8.GetString(ms.ToArray()).Contains("\"aid\":\"auto\""),"Cache inherited a source audio ID");
   typeof(Player).GetField("selectedAudio",F).SetValue(p,"3");
   ms.SetLength(0);Call(p,"ReturnToLive");
   Check(Encoding.UTF8.GetString(ms.ToArray()).Contains("\"aid\":\"3\""),"Return to source lost selected track");
   Check((string)Field(p,"selectedAudio")=="3","Cache track overwrote source selection");
   ms.SetLength(0);
   // This test writes commands to memory; no media is decoded. Own the path
   // fixture so the new-file state test does not depend on an old output folder.
   string sourceFixture=Path.GetTempFileName();
   try { typeof(Player).GetMethod("LoadFile",F).Invoke(p,new object[]{sourceFixture}); }
   finally { File.Delete(sourceFixture); }
   Check((string)Field(p,"selectedAudio")=="auto" && !(bool)Field(p,"sourceLoaded"),"New file retained track selection or old loaded state");
   Check(Encoding.UTF8.GetString(ms.ToArray()).Contains("\"aid\":\"auto\""),"New source did not reset audio");
   Check(PreRenderSession.NormalizeAudioTrack(false)=="no" && PreRenderSession.NormalizeAudioTrack("False")=="no","Boolean aid leaked into command line");
   Check(PreRenderSession.NormalizeAudioTrack(null)=="auto" && PreRenderSession.NormalizeAudioTrack("3")=="3" && PreRenderSession.NormalizeAudioTrack("3 --bad")=="auto","Invalid audio normalization");
   using(var normalized=new PreRenderSession(Path.GetFullPath("outputs/LumaPlayer-Optimized"),"unused",0,20,60,900,1,false,"False"))
    Check((string)typeof(PreRenderSession).GetField("audio",F).GetValue(normalized)=="no","Constructor missed normalization");
   var sourceFailure=typeof(PreRenderSession).GetMethod("IsSourceFailure",BindingFlags.Static|BindingFlags.NonPublic);
   Check((bool)sourceFailure.Invoke(null,new object[]{"[mkv] mkv metadata beyond end of file - incomplete file?"}),"Truncated file not rejected");
   Check((bool)sourceFailure.Invoke(null,new object[]{"Invalid audio PTS: 1.280000 -> 2776.800000"}),"Large timestamp jump not rejected");
   Check(!(bool)sourceFailure.Invoke(null,new object[]{"Invalid audio PTS: 1.280000 -> 1.300000"}),"Small audio correction rejected");
   var ranges=ClickSeekBar.CacheRanges(100,new[]{new PreparedChunk{Start=40,Length=15},new PreparedChunk{Start=10,Length=15},new PreparedChunk{Start=25,Length=15},new PreparedChunk{Start=80,Length=30}});
   Check(ranges.Length==2 && ranges[0][0]==.1 && ranges[0][1]==.55 && ranges[1][0]==.8 && ranges[1][1]==1,"Cache bounds/gaps/merging wrong");
   seek.SetCache(100,new[]{new PreparedChunk{Start=40,Length=15}});seek.Value=2000;
   using(var bitmap=new Bitmap(seek.Width,seek.Height)) {
    seek.DrawToBitmap(bitmap,new Rectangle(0,0,seek.Width,seek.Height));
    int x=8+(seek.Width-16)/2,y=seek.Height/2-1;
    Check(bitmap.GetPixel(x,y).ToArgb()==Color.FromArgb(49,196,178).ToArgb(),"Cached range not drawn in teal");
    bitmap.Save("work/cache-timeline-preview.png");
    seek.SetCache(0,new PreparedChunk[0]);seek.DrawToBitmap(bitmap,new Rectangle(0,0,seek.Width,seek.Height));
    Check(bitmap.GetPixel(x,y).ToArgb()==Color.FromArgb(61,66,80).ToArgb(),"Cancelled cache remains on timeline");
   }
   ((System.Windows.Forms.Timer)Field(p,"chromeTimer")).Dispose(); Call(p,"Shutdown"); w.Dispose();
  }
  Console.WriteLine("PASS: HD selection, exact seek, overlay, source/cache audio IDs, false aid normalization, new-file reset, merged cache ranges/colors/clear");
 }
}

