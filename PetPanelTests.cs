using System;
using System.Drawing;

static class PetPanelTests {
    static int passed;
    static void Check(bool value,string name) {if(!value)throw new Exception(name);passed++;}
    static void Main() {
        var pet=new Rectangle(500,400,112,122);var panel=new Size(190,88);var area=new Rectangle(0,0,1920,1040);
        var p=new PetPanelPlacement("invalid");
        Check(p.Preferred=="top-end","invalid preference recovers to client default");
        Check(!PetPanelPlacement.Valid(null),"null preference rejected");
        Check(PetPanelPlacement.CornerAt(new Point(555,460),pet)=="top-start","upper left quadrant");
        Check(PetPanelPlacement.CornerAt(new Point(556,460),pet)=="top-end","center x belongs to end");
        Check(PetPanelPlacement.CornerAt(new Point(555,461),pet)=="bottom-start","center y belongs to bottom");
        Check(PetPanelPlacement.CornerAt(new Point(556,461),pet)=="bottom-end","exact center uses native equality behavior");
        var initial=p.Resolve(pet,panel,area,24);
        Check(initial==new Point(624,400),"upper panel sits beside sprite and aligns with its top");
        p.Begin(new Point(620,335),initial);p.Move(new Point(623,338));
        Check(!p.HasMoved && p.DragPosition==initial,"3px jitter is a click");
        p.Move(new Point(624,335));
        Check(p.HasMoved && p.DragPosition==new Point(628,400),"4px starts drag and retains grab offset");
        p.Move(new Point(650,360));
        Check(p.DragPosition==new Point(654,425),"full displacement from original press avoids drift");
        p.Move(new Point(620,335));
        Check(p.HasMoved,"returning to press after drag does not become a click");
        Check(p.End(new Point(450,600),pet) && p.Preferred=="bottom-start","release pointer selects quadrant, not panel center");
        var lower=p.Resolve(pet,panel,area,24);
        Check(lower==new Point(298,434) && lower.Y+panel.Height==pet.Bottom,"lower panel sits beside sprite and aligns with its bottom");
        p.Begin(new Point(340,580),lower);p.End(new Point(340,580),pet);
        Check(p.Preferred=="bottom-start","click preserves preference");
        p.Begin(new Point(340,580),lower);p.Move(new Point(900,100));p.Cancel();
        Check(!p.Active && p.Preferred=="bottom-start","capture loss or Escape cancels without storing another corner");
        var stored=new PetPanelPlacement(p.Preferred);
        Check(stored.Resolve(pet,panel,area,24)==lower,"restoring saved corner recreates position");
        var edgePet=new Rectangle(1750,900,112,122);
        var edge=stored.Resolve(edgePet,panel,area,24);
        Check(edge.X>=24 && edge.Y>=24 && edge.X+190+24<=area.Right && edge.Y+88+24<=area.Bottom,"edge fallback keeps panel and shadow visible");
        Check(stored.Preferred=="bottom-start" && stored.Effective=="top-start","temporary vertical flip does not replace saved preference");
        Check(stored.Resolve(pet,panel,area,24)==lower && stored.Effective=="bottom-start","returning from edge restores preferred side");
        var right=new PetPanelPlacement("top-end");
        right.Resolve(new Rectangle(1750,400,112,122),panel,area,24);
        Check(right.Effective=="top-start" && right.Preferred=="top-end","right boundary flips horizontally");
        var left=new Rectangle(-1920,0,1920,1040);
        var negative=right.Resolve(new Rectangle(-1400,400,112,122),panel,left,24);
        Check(negative==new Point(-1276,400),"negative monitor coordinates preserved");
        var above=new Rectangle(pet.Left-110,pet.Top-145,330,141);
        var below=new Rectangle(pet.Left-110,pet.Bottom+4,330,141);
        var actionBar=new Rectangle(pet.Left,pet.Bottom+8,pet.Width,32);
        foreach(string corner in new[]{"top-start","top-end","bottom-start","bottom-end"}) {
            var docking=new PetPanelPlacement(corner);
            var rect=new Rectangle(docking.Resolve(pet,panel,area,24),panel);
            Check(!rect.IntersectsWith(above) && !rect.IntersectsWith(below),corner+" avoids either native notification tray placement");
            Check(!rect.IntersectsWith(actionBar) && !rect.IntersectsWith(pet),corner+" clears pet and Action Bar");
        }
        var spring=new PetPanelSpring();spring.Begin(new Point(700,-400),0);
        var goal=new Point(88,-100);Point at=Point.Empty;
        for(int now=16;now<=2000;now+=16)at=spring.Step(goal,now);
        Check(!spring.Active && at==goal,"native spring parameters converge exactly");
        spring.Begin(new Point(20,30),0);var offset=spring.Step(goal,16);
        var oldAbsolute=new Point(500+offset.X,400+offset.Y);var movedAbsolute=new Point(650+offset.X,430+offset.Y);
        Check(movedAbsolute.X-oldAbsolute.X==150 && movedAbsolute.Y-oldAbsolute.Y==30,"offset animation follows mascot immediately");
        spring.Cancel();Check(spring.Step(goal,32)==goal,"new drag cancels previous snap");
        Console.WriteLine("Passed "+passed+" relative placement checks.");
    }
}
