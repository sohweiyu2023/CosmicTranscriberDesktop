using CosmicTranscriber.Utilities;
var cases = new (string Name,double Current,double View,double Top,double Bottom,double Extent,double Expected)[] {
 ("clipped button needs whole visible hit area",1375,430,416,454,2000,1401),
 ("visible button does not move",900,430,100,138,2000,900),
 ("above viewport moves upward",900,430,-20,18,2000,878),
 ("fractional bottom clipping",900,430,400,430.5,2000,902.5),
 ("top edge clamps at content start",0,430,0,38,2000,0),
 ("bottom edge clamps at content end",1570,430,400,445,2000,1570),
 ("oversized editor aligns leading edge",900,430,-10,600,2000,890),
 ("content smaller than viewport",0,430,20,58,200,0),
 ("invalid viewport leaves offset unchanged",900,0,10,50,2000,900),
 ("invalid target leaves offset unchanged",900,430,double.NaN,50,2000,900)
};
foreach(var c in cases){var actual=FocusViewportVisibility.RequiredOffset(c.Current,c.View,c.Top,c.Bottom,c.Extent);if(Math.Abs(actual-c.Expected)>0.00001)throw new Exception(c.Name+" expected "+c.Expected+" got "+actual);Console.WriteLine("PASS "+c.Name);}
Console.WriteLine("FOCUS_VIEWPORT_CASES="+cases.Length);
