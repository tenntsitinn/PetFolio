using System;
using System.Drawing;

static class PetDragTests {
    static int passed;
    static void Check(bool value,string name) {if(!value)throw new Exception(name);passed++;}
    static void Main() {
        var d=new PetDragFollower();var area=new Rectangle(0,0,1920,1080);
        var saved=new Point(500,400);
        Check(d.Resolve(saved,0)==saved,"idle uses saved client position");
        d.Begin(new Point(530,445),saved,saved);
        d.Move(new Point(630,475),area,112,40);
        Check(d.Resolve(saved,16)==new Point(600,430),"horizontal and vertical movement before save");
        Check(d.Resolve(new Point(501,401),32)==new Point(600,430),"file change does not rewind active drag");
        d.Move(new Point(730,545),area,112,40);
        Check(d.Resolve(saved,48)==new Point(700,500),"fixed grab offset without cumulative drift");
        d.End(64);
        Check(!d.Active && d.Resolve(saved,80)==new Point(700,500),"release retains live position until save");
        Check(d.Resolve(new Point(698,499),100)==new Point(698,499),"client position reconciles release");
        Check(d.Resolve(new Point(710,505),116)==new Point(710,505),"later persisted motion follows normally");
        d.Begin(new Point(720,530),new Point(710,505),new Point(710,505));
        d.Move(new Point(-100,-100),area,112,40);
        Check(d.Resolve(saved,120)==Point.Empty,"top and left display boundaries");
        d.Move(new Point(5000,5000),area,112,40);
        Check(d.Resolve(saved,130)==new Point(1808,918),"bottom and right match native mascot bounds");
        var left=new Rectangle(-1920,40,1920,1040);
        d.Move(new Point(-1000,500),left,112,40);
        Check(d.Resolve(saved,140)==new Point(-1010,475),"negative monitor origin and work area top");
        d.End(150);
        Check(d.Resolve(new Point(710,505),2149)==new Point(-1010,475),"old save held within reconciliation grace");
        Check(d.Resolve(new Point(710,505),2150)==new Point(710,505),"missing commit recovers after timeout");
        d.Begin(new Point(520,440),saved,saved);d.End(2200);
        Check(d.Resolve(saved,2210)==saved,"click without movement stays still");
        d.Begin(new Point(520,440),saved,saved);d.Move(new Point(620,540),area,112,40);d.Reset();
        Check(!d.Active && d.Resolve(saved,2220)==saved,"hidden or replaced target cancels drag");
        d.Begin(new Point(520,440),saved,saved);d.Move(new Point(620,540),area,112,40);d.End(2300);
        d.Begin(new Point(630,550),new Point(600,500),saved);d.Move(new Point(650,570),area,112,40);
        Check(d.Resolve(saved,2316)==new Point(620,520),"second drag during release reconciliation keeps its rendered offset");
        Console.WriteLine("Passed "+passed+" drag state checks.");
    }
}
