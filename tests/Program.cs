using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using OpenCvSharp;
using DigitalTwinIndustrialAutomationPlatform.Models;
using DigitalTwinIndustrialAutomationPlatform.Cameras;
using DigitalTwinIndustrialAutomationPlatform.Recognition;

int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);checks++;}
void Reject(Action action,string text){try{action();}catch(Exception ex)when(ex is ArgumentException or System.IO.InvalidDataException){Check(true,text);return;}throw new Exception("Expected rejection: "+text);}
var camA=new CameraDeviceInfo("dshow:device-a","同型号相机","device-a",0,"DirectShow",true,"ready");
var camB=new CameraDeviceInfo("dshow:device-b","同型号相机","device-b",1,"DirectShow",true,"ready");
Check(CameraIdentity.Resolve(camB.Id,new[]{camA,camB}).CaptureIndex==1,"same-name cameras resolved by unique identity");
Check(CameraIdentity.Resolve(camB.Id,new[]{camB with { CaptureIndex=0 },camA with { CaptureIndex=1 }}).CaptureIndex==0,"selected device follows reindexed camera after reconnect");
Check(CameraIdentity.Resolve("DSHOW:DEVICE-A",new[]{camA}).Id==camA.Id,"device identity matches case-insensitively");
bool missing=false;try{CameraIdentity.Resolve(camB.Id,new[]{camA});}catch(InvalidOperationException){missing=true;}
Check(missing,"removed camera is rejected instead of falling back to another device");
bool sdk=false;try{CameraIdentity.Resolve("sdk-only",new[]{new CameraDeviceInfo("sdk-only","工业相机","",null,"SDK",false,"requires SDK")});}catch(InvalidOperationException){sdk=true;}
Check(sdk,"SDK-only inventory item cannot be mistaken for usable camera");
Check(CameraIdentity.Normalize(@"@device:pnp:\\?\USB#VID_1234&PID_5678#SERIAL#{category}")==CameraIdentity.Normalize(@"USB\VID_1234&PID_5678\SERIAL"),"DirectShow and PnP paths normalize for inventory deduplication");
Check(CameraIdentity.IsCameraCandidate("普通设备","Camera",""),"Windows camera class included in system inventory");
Check(CameraIdentity.IsCameraCandidate("Basler ace","USBDevice","Basler"),"vendor industrial camera appears as SDK candidate");
Check(!CameraIdentity.IsCameraCandidate("Speakers","AudioEndpoint","Generic"),"ordinary audio device excluded from camera inventory");
var selectedSettings=new ProcessingSettings{CameraDeviceId=camA.Id,CameraName=camA.Name,CameraIndex=camA.CaptureIndex!.Value};
Check(selectedSettings.Copy().CameraDeviceId==camA.Id,"camera identity survives settings snapshot");
var dir=Path.Combine(Path.GetTempPath(),"产品自动化测试_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
var recipe=new ProcessingSettings{Filter=FilterKind.Median,KernelSize=3,MinArea=80};
using var image=new Mat(180,240,MatType.CV_8UC3,Scalar.White);
Cv2.Rectangle(image,new Rect(60,45,80,90),Scalar.Black,-1);
Cv2.Rectangle(image,new Rect(5,5,3,3),Scalar.Black,-1);
using var measured=ProductPipeline.Measure(image,recipe);
Check(measured.IsSingleTarget,"single-product contour excludes small noise");
Check(measured.Area>6000&&measured.Area<7200,"area measured in expected pixel range");
Check(measured.Perimeter>300&&measured.Perimeter<350,"closed contour perimeter measured");
var entry=ProductStore.Save(dir,"螺栓A",image,measured,recipe);var profile=entry.Profile;
Check(Directory.Exists(entry.DirectoryPath),"custom product directory created");
Check(new[]{"product.json","original.png","processed.png","binary.png"}.All(n=>File.Exists(Path.Combine(entry.DirectoryPath,n))),"parameters and all three images saved together");
var loaded=ProductStore.Load(entry.DirectoryPath);
Check(loaded.Profile.Name=="螺栓A"&&loaded.Profile.Area==measured.Area&&loaded.Profile.Perimeter==measured.Perimeter,"product features round-trip");
Check(ProductPipeline.RecipeKey(loaded.Profile.Recipe)==ProductPipeline.RecipeKey(recipe),"measurement recipe round-trips");
using(var decoded=Cv2.ImDecode(File.ReadAllBytes(Path.Combine(entry.DirectoryPath,"original.png")),ImreadModes.Color))Check(Cv2.Norm(decoded,image,NormTypes.L1)==0,"original image saved losslessly with Unicode path");
Check(ProductStore.Discover(dir).Count()==1&&ProductStore.Discover(entry.DirectoryPath).Count()==1,"single-class and library root discovery");
var staging=Path.Combine(dir,"unfinished.staging");Directory.CreateDirectory(staging);File.WriteAllText(Path.Combine(staging,"product.json"),"{}");
Check(ProductStore.Discover(dir).Count()==1,"unfinished product staging directory excluded from library");
Reject(()=>ProductStore.Save(dir,"",image,measured,recipe),"empty product name rejected");
var savedJson=File.ReadAllText(Path.Combine(entry.DirectoryPath,"product.json"));
File.WriteAllText(Path.Combine(entry.DirectoryPath,"product.json"),savedJson.Replace("\"SchemaVersion\": 2","\"SchemaVersion\": 999"));
Reject(()=>ProductStore.Load(entry.DirectoryPath),"unknown product schema rejected");
File.WriteAllText(Path.Combine(entry.DirectoryPath,"product.json"),savedJson);
var config=new AutomationSettings{SamplingHz=10};
using(var decision=ProductPipeline.Recognize(image,new[]{profile},config,default))Check(decision.Decision.Status==RecognitionStatus.Recognized&&decision.Decision.AreaErrorPercent==0&&decision.Decision.PerimeterErrorPercent==0,"training image recognized with identical pipeline");
using(var shifted=new Mat(180,240,MatType.CV_8UC3,Scalar.White)){
 Cv2.Rectangle(shifted,new Rect(90,60,80,90),Scalar.Black,-1);
 using var result=ProductPipeline.Recognize(shifted,new[]{profile},config,default);Check(result.Decision.Status==RecognitionStatus.Recognized,"translated identical shape recognized");}
using(var blank=new Mat(180,240,MatType.CV_8UC3,Scalar.White)){
 using var result=ProductPipeline.Recognize(blank,new[]{profile},config,default);Check(result.Decision.Status==RecognitionStatus.InvalidTarget,"empty scene rejected");}
using(var two=new Mat(180,240,MatType.CV_8UC3,Scalar.White)){
 Cv2.Rectangle(two,new Rect(20,40,60,90),Scalar.Black,-1);Cv2.Rectangle(two,new Rect(150,40,60,90),Scalar.Black,-1);
 using var m=ProductPipeline.Measure(two,recipe);Check(m.Count==2&&!m.IsSingleTarget,"multiple products rejected for enrollment");
 Reject(()=>ProductStore.Save(dir,"多个",two,m,recipe),"multiple-target product cannot be saved");
 using var result=ProductPipeline.Recognize(two,new[]{profile},config,default);Check(result.Decision.Status==RecognitionStatus.InvalidTarget,"multiple products rejected in recognition");}
using(var wrongSize=new Mat(90,120,MatType.CV_8UC3,Scalar.White)){
 using var result=ProductPipeline.Recognize(wrongSize,new[]{profile},config,default);Check(result.Decision.Status==RecognitionStatus.ResolutionMismatch,"different capture resolution rejected");}
var duplicate=JsonSerializer.Deserialize<ProductProfile>(JsonSerializer.Serialize(profile))!;duplicate.Id=Guid.NewGuid();duplicate.Name="螺栓B";
using(var result=ProductPipeline.Recognize(image,new[]{profile,duplicate},config,default))Check(result.Decision.Status==RecognitionStatus.Ambiguous&&result.Decision.Product==null,"overlapping classes reject ambiguous identification");
Check(ProductPipeline.RelativeError(105,100)==5&&ProductPipeline.RelativeError(95,100)==5,"relative errors cover both sides of tolerance");
Reject(()=>ProductPipeline.RelativeError(100,0),"zero reference rejected");
Reject(()=>new AutomationSettings{Host="http://localhost"}.Validate(),"URL rejected in hostname field");
Reject(()=>new AutomationSettings{Port=0}.Validate(),"invalid port rejected");
Reject(()=>new AutomationSettings{SamplingHz=double.NaN}.Validate(),"NaN sampling rate rejected");
Reject(()=>new AutomationSettings{SamplingHz=0}.Validate(),"zero sampling rate rejected");
using var success=ProductPipeline.Recognize(image,new[]{profile},config,default);
var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
config.Port=((IPEndPoint)listener.LocalEndpoint).Port;
using var robot=new TcpRobotClient();var accepted=listener.AcceptTcpClientAsync();await robot.ConnectAsync(config,default);using var peer=await accepted;
Check(robot.IsConnected,"robot TCP loopback connection established");
using var reader=new StreamReader(peer.GetStream(),new UTF8Encoding(false),false,1024,true);
var runId=Guid.NewGuid();await robot.SendAsync(runId,42,success.Decision,config,default);
var line=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));using(var payload=JsonDocument.Parse(line!)){
 Check(payload.RootElement.GetProperty("productType").GetString()=="螺栓A","UTF-8 JSON product type delivered");
 Check(payload.RootElement.GetProperty("sequence").GetInt64()==42&&payload.RootElement.GetProperty("runId").GetGuid()==runId,"run ID and sequence delivered");
 Check(payload.RootElement.GetProperty("perimeterPx").GetDouble()==measured.Perimeter&&payload.RootElement.GetProperty("areaPx2").GetDouble()==measured.Area,"measured features in robot message");}
Reject(()=>robot.SendAsync(runId,43,new RecognitionDecision(RecognitionStatus.NoMatch,null,null,null,null,null,"no"),config,default).GetAwaiter().GetResult(),"unmatched result cannot be sent");
await peer.GetStream().WriteAsync(Encoding.UTF8.GetBytes("ACK ignored\n"));peer.Dispose();
for(int i=0;i<100&&robot.IsConnected;i++)await Task.Delay(10);
Check(!robot.IsConnected,"remote close detected even after inbound reply");listener.Stop();
// Real loop scheduling with injected synthetic frames and TCP server.
listener=new TcpListener(IPAddress.Loopback,0);listener.Start();config.Port=((IPEndPoint)listener.LocalEndpoint).Port;
accepted=listener.AcceptTcpClientAsync();await robot.ConnectAsync(config,default);using var server=await accepted;using var loopReader=new StreamReader(server.GetStream());
using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(10));
var runWrites=new DigitalTwinIndustrialAutomationPlatform.Services.ShutdownCoordinator();
var frames=new FakeSource(image);var sampleTimes=new List<DateTimeOffset>();var samples=new List<AutomaticSample>();var log=Path.Combine(dir,"run.jsonl");
try{await AutomaticRecognition.RunAsync(()=>frames,new[]{profile},config,robot,log,(sample,raw,result)=>{
 samples.Add(sample);sampleTimes.Add(sample.Time);if(samples.Count==3)stop.Cancel();return Task.CompletedTask;},stop.Token,runWrites);}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
Check(samples.Count==3&&samples.All(s=>s.Sent),"automatic loop recognizes and sends each scheduled sample");
var received=new List<string>();for(int i=0;i<3;i++)received.Add((await loopReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!);
Check(received.Count==3&&received.Select(l=>JsonDocument.Parse(l).RootElement.GetProperty("sequence").GetInt64()).SequenceEqual(new long[]{1,2,3}),"automatic robot messages are ordered NDJSON frames");
Check((sampleTimes[2]-sampleTimes[0]).TotalMilliseconds>=170,"sampling frequency does not spin duplicate analyses");
Check(File.ReadAllLines(log).Length==3,"every completed sample persisted to full run log");
Check(frames.Disposed,"camera source disposed after stop");
Check(await runWrites.FinishAsync([],TimeSpan.FromSeconds(1)),"automatic log writes drain through shutdown coordinator");
var available=server.Available;await Task.Delay(130);Check(server.Available==available,"no further frames sent after stopped task completes");
using(var noMatchStop=new CancellationTokenSource(TimeSpan.FromSeconds(5))){
 int n=0;var different=JsonSerializer.Deserialize<ProductProfile>(savedJson)!;different.Area*=2;
 try{await AutomaticRecognition.RunAsync(()=>new FakeSource(image),new[]{different},config,robot,Path.Combine(dir,"nomatch.jsonl"),(sample,raw,result)=>{
  Check(!sample.Sent&&sample.Decision.Status==RecognitionStatus.NoMatch,"unmatched sampling cycle does not send");if(++n==2)noMatchStop.Cancel();return Task.CompletedTask;},noMatchStop.Token);}catch(OperationCanceledException)when(noMatchStop.IsCancellationRequested){}
 Check(server.Available==0,"unmatched loop generated no TCP payload");}
using(var ambiguousStop=new CancellationTokenSource(TimeSpan.FromSeconds(5))){
 try{await AutomaticRecognition.RunAsync(()=>new FakeSource(image),new[]{profile,duplicate},config,robot,Path.Combine(dir,"ambiguous.jsonl"),(sample,raw,result)=>{
 Check(!sample.Sent&&sample.Decision.Status==RecognitionStatus.Ambiguous,"ambiguous sampling cycle does not send");ambiguousStop.Cancel();return Task.CompletedTask;},ambiguousStop.Token);}catch(OperationCanceledException)when(ambiguousStop.IsCancellationRequested){}
 Check(server.Available==0,"ambiguous loop generated no TCP payload");}
robot.Disconnect();listener.Stop();
using(var disposedRobot=new TcpRobotClient()){
 disposedRobot.Dispose();bool rejected=false;
 try{await disposedRobot.ConnectAsync(config,default);}catch(ObjectDisposedException){rejected=true;}
 Check(rejected,"disposed robot client cannot reconnect during shutdown");
}
// Algorithm and independent threshold coverage using the production pipeline.
using(var clean=new Mat(180,240,MatType.CV_8UC3,Scalar.White)){
 Cv2.Rectangle(clean,new Rect(60,45,80,90),Scalar.Black,-1);
 foreach(var kind in Enum.GetValues<FilterKind>()){
  var r=new ProcessingSettings{Filter=kind,KernelSize=3,MinArea=80,Invert=kind<FilterKind.Sobel};
  using var output=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.Filter(clean,r);
  Check(output.Size()==clean.Size()&&output.Depth()==MatType.CV_8U,$"{kind} filter produces valid 8-bit image");
  if(kind>=FilterKind.Sobel){
   using var flat=new Mat(60,80,MatType.CV_8UC3,new Scalar(120,120,120));
   using var zero=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.Filter(flat,r);
   Check(Cv2.CountNonZero(zero)==0,$"{kind} derivative is zero on constant image");
  }
 }
 foreach(var edge in Enum.GetValues<EdgeKind>()){
  var r=new ProcessingSettings{Filter=FilterKind.Gaussian,KernelSize=3,MinArea=80,Edge=edge,EdgeThreshold=30,CannyLow=25,CannyHigh=90,CloseEdgeGaps=true};
  using var m=ProductPipeline.Measure(clean,r);
  Check(m.IsSingleTarget&&m.Area>5000,$"{edge} produces a measurable single product");
  var e=ProductStore.Save(dir,"算法_"+edge,clean,m,r);
  var again=ProductStore.Load(e.DirectoryPath).Profile;
  Check(again.Recipe.Edge==edge&&ProductPipeline.RecipeKey(again.Recipe)==ProductPipeline.RecipeKey(r),$"{edge} algorithm and thresholds persist");
  using var recognized=ProductPipeline.Recognize(clean,new[]{again},new AutomationSettings{AreaTolerancePercent=0,PerimeterTolerancePercent=0},default);
  Check(recognized.Decision.Status==RecognitionStatus.Recognized&&recognized.Decision.AreaErrorPercent==0&&recognized.Decision.PerimeterErrorPercent==0,$"{edge} saved recipe gives identical automatic recognition");
 }
 // Different recipes must not reuse the same cached measurement.
 var first=new ProcessingSettings{Edge=EdgeKind.Canny,CannyLow=20,CannyHigh=70,MinArea=80};
 var second=first.Copy();second.Edge=EdgeKind.LoG;second.EdgeThreshold=20;
 using var firstM=ProductPipeline.Measure(clean,first);using var secondM=ProductPipeline.Measure(clean,second);
 var firstP=new ProductProfile{Name="Canny class",ImageWidth=240,ImageHeight=180,Area=firstM.Area,Perimeter=firstM.Perimeter,Recipe=first};
 var secondP=new ProductProfile{Name="LoG class",ImageWidth=240,ImageHeight=180,Area=secondM.Area,Perimeter=secondM.Perimeter,Recipe=second};
 using var both=ProductPipeline.Recognize(clean,new[]{firstP,secondP},new AutomationSettings{AreaTolerancePercent=0,PerimeterTolerancePercent=0},default);
 Check(both.Decision.Status==RecognitionStatus.Ambiguous,"multiple distinct saved recipes are measured independently");
 var legacy=JsonSerializer.Deserialize<ProcessingSettings>("{\"Filter\":1,\"KernelSize\":3,\"Threshold\":160,\"Invert\":true,\"MinArea\":80}")!;
 using var old=ProductPipeline.Measure(clean,legacy);
 using var filtered=new Mat();using var gray=new Mat();using var mask=new Mat();Cv2.MedianBlur(clean,filtered,3);Cv2.CvtColor(filtered,gray,ColorConversionCodes.BGR2GRAY);Cv2.Threshold(gray,mask,160,255,ThresholdTypes.BinaryInv);
 Check(legacy.Edge==EdgeKind.ThresholdContour&&Cv2.Norm(mask,old.Binary,NormTypes.L1)==0,"legacy numeric settings still read with default threshold parameters on monochrome sample");
}
using(var pixels=new Mat(1,6,MatType.CV_8UC1)){
 byte[] values=[0,49,50,100,150,255];for(int i=0;i<values.Length;i++)pixels.Set(0,i,values[i]);
 var r=new ProcessingSettings{Segmentation=SegmentationKind.Range,Threshold=50,ThresholdUpper=150,Invert=false};
 using var band=DigitalTwinIndustrialAutomationPlatform.Services.ImageProcessor.Segment(pixels,r);
 Check(Cv2.CountNonZero(band)==3&&band.At<byte>(0,2)==255&&band.At<byte>(0,4)==255,"gray range includes exact lower and upper thresholds");
 r.Invert=true;using var inverse=DigitalTwinIndustrialAutomationPlatform.Services.ImageProcessor.Segment(pixels,r);
 using var combined=new Mat();Cv2.BitwiseXor(band,inverse,combined);Check(Cv2.CountNonZero(combined)==6,"range foreground inversion is exact complement");
 r.Segmentation=SegmentationKind.Fixed;r.Invert=false;r.Threshold=49;using var low=DigitalTwinIndustrialAutomationPlatform.Services.ImageProcessor.Segment(pixels,r);
 r.Threshold=150;using var high=DigitalTwinIndustrialAutomationPlatform.Services.ImageProcessor.Segment(pixels,r);
 Check(Cv2.CountNonZero(low)==4&&Cv2.CountNonZero(high)==1,"different fixed thresholds change segmentation");
 r.Segmentation=SegmentationKind.Otsu;using var otsu=DigitalTwinIndustrialAutomationPlatform.Services.ImageProcessor.Segment(pixels,r);
 using var reference=new Mat();Cv2.Threshold(pixels,reference,0,255,ThresholdTypes.Binary|ThresholdTypes.Otsu);
 Check(Cv2.Norm(otsu,reference,NormTypes.L1)==0,"Otsu segmentation matches native OpenCV");
}
using(var ramp=new Mat(80,100,MatType.CV_8UC1)){
 for(int row=0;row<80;row++)for(int col=0;col<100;col++)ramp.Set(row,col,(byte)(col*2));
 var r=new ProcessingSettings{Edge=EdgeKind.Sobel,EdgeThreshold=5};
 using var low=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.Mask(ramp,r);
 r.EdgeThreshold=100;using var high=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.Mask(ramp,r);
 Check(Cv2.CountNonZero(low)>Cv2.CountNonZero(high),"gradient threshold changes edge mask without per-image normalization");
}
var baseSettings=new ProcessingSettings();var key=ProductPipeline.RecipeKey(baseSettings);
Action<ProcessingSettings>[] changes=[s=>s.Edge=EdgeKind.Canny,s=>s.EdgeThreshold++,s=>s.CannyLow++,s=>s.CannyHigh++,s=>s.Threshold++,s=>s.ThresholdUpper--,s=>s.Segmentation=SegmentationKind.Otsu,s=>s.GaussianSigma=1.2,s=>s.BilateralSigmaColor=35,s=>s.BilateralSigmaSpace=60,s=>s.DerivativeKernelSize=5,s=>s.CloseEdgeGaps=true,s=>s.EdgeClosingSize=5];
Check(changes.All(change=>{var s=baseSettings.Copy();change(s);return ProductPipeline.RecipeKey(s)!=key;}),"every algorithm threshold and parameter changes recipe cache key");
var custom=baseSettings.Copy();custom.Edge=EdgeKind.Scharr;custom.Filter=FilterKind.LoG;custom.GaussianSigma=1.4;custom.BilateralSigmaColor=90;custom.BilateralSigmaSpace=25;custom.Threshold=80;custom.ThresholdUpper=180;custom.EdgeThreshold=37;custom.CannyLow=20;custom.CannyHigh=100;custom.CloseEdgeGaps=true;custom.EdgeClosingSize=5;custom.DerivativeKernelSize=5;custom.Segmentation=SegmentationKind.Range;
var serialized=JsonSerializer.Serialize(custom);var restored=JsonSerializer.Deserialize<ProcessingSettings>(serialized)!;
Check(serialized.Contains("Scharr")&&serialized.Contains("LoG")&&ProductPipeline.RecipeKey(custom)==ProductPipeline.RecipeKey(restored),"human-readable algorithm names and all custom thresholds round-trip");
Reject(()=>new ProcessingSettings{Threshold=-1}.Validate(),"negative gray threshold rejected");
Reject(()=>new ProcessingSettings{Segmentation=SegmentationKind.Range,Threshold=180,ThresholdUpper=80}.Validate(),"reversed gray range rejected");
Reject(()=>new ProcessingSettings{EdgeThreshold=256}.Validate(),"gradient threshold above 255 rejected");
Reject(()=>new ProcessingSettings{CannyLow=150,CannyHigh=50}.Validate(),"reversed Canny thresholds rejected");
Reject(()=>new ProcessingSettings{Edge=(EdgeKind)99}.Validate(),"unknown edge operator rejected");
Reject(()=>new ProcessingSettings{DerivativeKernelSize=9}.Validate(),"unsupported derivative aperture rejected");
Reject(()=>new ProcessingSettings{GaussianSigma=double.NaN}.Validate(),"nonfinite Gaussian sigma rejected");

using(var weak=new Mat(100,120,MatType.CV_8UC1,new Scalar(100))){
 Cv2.Rectangle(weak,new Rect(30,25,50,50),new Scalar(130),-1);
 var r=new ProcessingSettings{Edge=EdgeKind.Canny,CannyLow=10,CannyHigh=30};
 using var low=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.CreateMask(weak,r);
 r.CannyLow=200;r.CannyHigh=240;
 using var high=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.CreateMask(weak,r);
 Check(Cv2.CountNonZero(low)>0&&Cv2.CountNonZero(high)==0,"independent Canny thresholds control low-contrast edge acceptance");
}
// Grayscale-before-filter regressions use colorful input: channelwise median/bilateral
// followed by grayscale is not equivalent to this required pipeline.
using(var color=new Mat(60,80,MatType.CV_8UC3)){
 var random=new Random(731);for(int row=0;row<color.Rows;row++)for(int col=0;col<color.Cols;col++)
  color.Set(row,col,new Vec3b((byte)random.Next(256),(byte)random.Next(256),(byte)random.Next(256)));
 using var snapshot=color.Clone();
 using var gray=DigitalTwinIndustrialAutomationPlatform.Services.ImageProcessor.ToGray(color);
 using var bgra=new Mat();Cv2.CvtColor(color,bgra,ColorConversionCodes.BGR2BGRA);
 foreach(var kind in Enum.GetValues<FilterKind>()){
  var r=new ProcessingSettings{Filter=kind,KernelSize=3};
  using var fromColor=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.Filter(color,r);
  using var fromGray=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.Filter(gray,r);
  using var fromBgra=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.Filter(bgra,r);
  Check(fromColor.Type()==MatType.CV_8UC1&&Cv2.Norm(fromColor,fromGray,NormTypes.L1)==0,$"{kind} converts BGR to gray BEFORE filtering");
  Check(fromBgra.Type()==MatType.CV_8UC1&&Cv2.Norm(fromBgra,fromGray,NormTypes.L1)==0,$"{kind} BGRA input follows identical grayscale pipeline");
 }
 foreach(var edge in Enum.GetValues<EdgeKind>()){
  var r=new ProcessingSettings{Filter=FilterKind.Bilateral,KernelSize=3,Edge=edge};
  using var actual=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.CreateMask(color,r);
  using var expected=DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.CreateMask(gray,r);
  Check(actual.Type()==MatType.CV_8UC1&&Cv2.Norm(actual,expected,NormTypes.L1)==0,$"{edge} masks use grayscale before nonlinear bilateral filtering");
 }
 using var preview=DigitalTwinIndustrialAutomationPlatform.Services.ImageProcessor.Apply(color,ImageOperation.Grayscale,new(),null);
 Check(preview.Image.Channels()==1&&Cv2.Norm(preview.Image,gray,NormTypes.L1)==0,"grayscale preview matches the pipeline's input grayscale");
 Check(Cv2.Norm(color,snapshot,NormTypes.L1)==0,"all grayscale filtering preserves original color input");
}
using(var color=new Mat(180,240,MatType.CV_8UC3,new Scalar(210,235,250))){
 Cv2.Rectangle(color,new Rect(60,45,80,90),new Scalar(170,40,60),-1);
 var r=new ProcessingSettings{Filter=FilterKind.Bilateral,KernelSize=5,MinArea=80,Edge=EdgeKind.Canny};
 using var gray=DigitalTwinIndustrialAutomationPlatform.Services.ImageProcessor.ToGray(color);
 using var trained=ProductPipeline.Measure(color,r);using var reference=ProductPipeline.Measure(gray,r);
 Check(trained.IsSingleTarget&&trained.Area==reference.Area&&trained.Perimeter==reference.Perimeter&&Cv2.Norm(trained.Binary,reference.Binary,NormTypes.L1)==0,"colored product measurements equal explicit grayscale-first reference");
 var e=ProductStore.Save(dir,"灰度流程",color,trained,r);var p=ProductStore.Load(e.DirectoryPath).Profile;
 Check(p.SchemaVersion==2&&p.ProcessingPipeline=="GrayscaleBeforeFilterV1"&&!p.RequiresReenrollment,"new product archive records grayscale pipeline version");
 using var original=Cv2.ImDecode(File.ReadAllBytes(Path.Combine(e.DirectoryPath,"original.png")),ImreadModes.Color);
 Check(Cv2.Norm(original,color,NormTypes.L1)==0,"grayscale processing still saves original color photo losslessly");
 using var result=ProductPipeline.Recognize(color,new[]{p},new AutomationSettings{AreaTolerancePercent=0,PerimeterTolerancePercent=0},default);
 Check(result.Decision.Status==RecognitionStatus.Recognized,"color capture recognized using saved grayscale-first recipe");
 Check(trained.Processed.At<Vec3b>(170,225).Item0==trained.Processed.At<Vec3b>(170,225).Item1&&trained.Processed.At<Vec3b>(170,225).Item1==trained.Processed.At<Vec3b>(170,225).Item2,"measurement annotation uses grayscale backdrop");
 var path=Path.Combine(e.DirectoryPath,"product.json");var json=File.ReadAllText(path);
 File.WriteAllText(path,json.Replace("\"SchemaVersion\": 2","\"SchemaVersion\": 1"));
 var legacy=ProductStore.Load(e.DirectoryPath).Profile;
 Check(legacy.RequiresReenrollment,"legacy product loads for reenrollment instead of discarding original sample");
 Reject(()=>ProductPipeline.Recognize(color,new[]{legacy},new AutomationSettings(),default).Dispose(),"legacy color-filter reference cannot be compared with grayscale-first measurements");
 bool opened=false;
 Reject(()=>AutomaticRecognition.RunAsync(()=>{opened=true;return new FakeSource(color);},new[]{legacy},new AutomationSettings(),new TcpRobotClient(),Path.Combine(dir,"legacy.jsonl"),(_,_,_)=>Task.CompletedTask,default).GetAwaiter().GetResult(),"automatic loop refuses legacy profiles before capture or send");
 Check(!opened,"legacy-profile rejection does not open camera");
}

// Exit regressions: native reads may ignore cancellation, but committed writes must drain.
var fastExit=new DigitalTwinIndustrialAutomationPlatform.Services.ShutdownCoordinator();var exitWatch=Stopwatch.StartNew();
Check(await fastExit.FinishAsync(Array.Empty<Task>(),TimeSpan.FromSeconds(2))&&exitWatch.ElapsedMilliseconds<500,"idle shutdown returns immediately without fixed sleep");
var stuckA=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var stuckB=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var boundedExit=new DigitalTwinIndustrialAutomationPlatform.Services.ShutdownCoordinator();exitWatch.Restart();
Check(!await boundedExit.FinishAsync(new[]{stuckA.Task,stuckB.Task},TimeSpan.FromMilliseconds(80))&&exitWatch.ElapsedMilliseconds<700,"multiple unresponsive workers share bounded shutdown grace");
stuckA.SetException(new IOException("late camera error"));stuckB.SetResult();
var failedExit=new DigitalTwinIndustrialAutomationPlatform.Services.ShutdownCoordinator();
Check(await failedExit.FinishAsync(new[]{Task.FromException(new IOException("camera disconnected")),Task.FromCanceled(new CancellationToken(true))},TimeSpan.FromSeconds(1)),"faulted and canceled worker tasks do not obstruct shutdown");
var durableExit=new DigitalTwinIndustrialAutomationPlatform.Services.ShutdownCoordinator();
var releaseWrite=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var neverReturns=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);bool writeCompleted=false;
var pendingWrite=durableExit.RunWriteAsync(async()=>{await releaseWrite.Task;writeCompleted=true;});
var finishWrite=durableExit.FinishAsync(new[]{neverReturns.Task},TimeSpan.FromMilliseconds(40));
await Task.Delay(100);
Check(!finishWrite.IsCompleted&&!writeCompleted,"pending persistence survives ordinary worker timeout");
releaseWrite.SetResult();await pendingWrite;
Check(!await finishWrite.WaitAsync(TimeSpan.FromSeconds(1))&&writeCompleted,"shutdown drains essential write then returns despite hung camera");
neverReturns.SetResult();bool lateRejected=false;
try{await durableExit.RunWriteAsync(()=>Task.CompletedTask);}catch(OperationCanceledException){lateRejected=true;}
Check(lateRejected,"new writes rejected after shutdown has sealed persistence");
var failedWriteExit=new DigitalTwinIndustrialAutomationPlatform.Services.ShutdownCoordinator();bool writeFailed=false;
try{await failedWriteExit.RunWriteAsync(()=>Task.FromException(new IOException("disk error")));}catch(IOException){writeFailed=true;}
Check(writeFailed&&await failedWriteExit.FinishAsync([],TimeSpan.FromSeconds(1)),"failed persistence is reported without leaking shutdown wait");
var syncWriteExit=new DigitalTwinIndustrialAutomationPlatform.Services.ShutdownCoordinator();bool syncFailed=false;
try{await syncWriteExit.RunWriteAsync(()=>throw new IOException("synchronous disk error"));}catch(IOException){syncFailed=true;}
Check(syncFailed&&await syncWriteExit.FinishAsync([],TimeSpan.FromSeconds(1)),"synchronous write failure also releases shutdown tracking");

Directory.Delete(dir,true);

Console.WriteLine($"SUCCESS: {checks} checks");

sealed class FakeSource(Mat image):IProductFrameSource{
 public bool Disposed {get;private set;}
 public Mat Capture(CancellationToken token){token.ThrowIfCancellationRequested();return image.Clone();}
 public void Dispose()=>Disposed=true;
}
