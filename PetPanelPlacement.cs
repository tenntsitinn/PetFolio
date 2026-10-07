using System;
using System.Drawing;

sealed class PetPanelPlacement {
    internal string Preferred { get; private set; }
    internal string Effective { get; private set; }
    internal bool Active { get; private set; }
    internal bool HasMoved { get; private set; }
    internal Point DragPosition { get; private set; }
    Point press, origin;
    static readonly string[] Corners={"top-start","top-end","bottom-start","bottom-end"};
    internal PetPanelPlacement(string preference="top-end") {Preferred=Valid(preference)?preference:"top-end";}
    internal static bool Valid(string value) {return Array.IndexOf(Corners,value)>=0;}
    // Same center comparisons and equality behavior as the client's badge corner handler.
    internal static string CornerAt(Point pointer,Rectangle pet) {
        return (pointer.Y<pet.Top+pet.Height/2.0?"top":"bottom")+"-"+
            (pointer.X<pet.Left+pet.Width/2.0?"start":"end");
    }
    internal void Begin(Point pointer,Point renderedPanel) {
        press=pointer;origin=renderedPanel;DragPosition=origin;Active=true;HasMoved=false;
    }
    internal void Move(Point pointer) {
        if(!Active)return;
        int dx=pointer.X-press.X,dy=pointer.Y-press.Y;
        if(!HasMoved && Math.Abs(dx)<4 && Math.Abs(dy)<4)return;
        HasMoved=true;DragPosition=new Point(origin.X+dx,origin.Y+dy);
    }
    internal bool End(Point pointer,Rectangle pet) {
        if(!Active)return false;
        bool commit=HasMoved;
        if(commit)Preferred=CornerAt(pointer,pet);
        Active=false;HasMoved=false;return commit;
    }
    internal void Cancel() {Active=false;HasMoved=false;}
    internal Point Resolve(Rectangle pet,Size panel,Rectangle workArea,int padding) {
        string chosen=Preferred;
        if(Overflow(Dock(Preferred,pet,panel),panel,workArea,padding)>0) {
            double best=double.MaxValue;
            foreach(string candidate in Corners) {
                var point=Dock(candidate,pet,panel);
                // Compare overflow first; retain the effective corner in tight space.
                double score=Overflow(point,panel,workArea,padding);
                if(candidate!=Preferred)score+=32;
                if(candidate==Effective)score-=8;
                if(score<best){best=score;chosen=candidate;}
            }
        }
        Effective=chosen;
        return Constrain(Dock(chosen,pet,panel),panel,workArea,padding);
    }
    static Point Dock(string corner,Rectangle pet,Size panel) {
        // Native notification trays occupy the area above or below the mascot.
        // Keep the quota body alongside the mascot, inside its vertical band,
        // instead of adding another panel to the client's notification/control rows.
        int top=pet.Top,bottom=pet.Bottom-panel.Height;
        if(panel.Height>pet.Height)top=bottom=pet.Top+(pet.Height-panel.Height)/2;
        return new Point(corner.EndsWith("start")?pet.Left-panel.Width-12:pet.Right+12,
            corner.StartsWith("top")?top:bottom);
    }
    static double Overflow(Point p,Size panel,Rectangle area,int padding) {
        int left=Math.Max(0,area.Left+padding-p.X),right=Math.Max(0,p.X+panel.Width+padding-area.Right);
        int top=Math.Max(0,area.Top+padding-p.Y),bottom=Math.Max(0,p.Y+panel.Height+padding-area.Bottom);
        return left+right+top+bottom+(left+right)*(double)panel.Height+(top+bottom)*(double)panel.Width;
    }
    internal static Point Constrain(Point p,Size panel,Rectangle area,int padding) {
        int x=Clamp(p.X,area.Left+padding,area.Right-panel.Width-padding);
        int y=Clamp(p.Y,area.Top+padding,area.Bottom-panel.Height-padding);
        return new Point(x,y);
    }
    static int Clamp(int n,int min,int max) {return Math.Max(min,Math.Min(Math.Max(min,max),n));}
}

// Client badge spring: stiffness 420, damping 20, mass .7. Animate the offset
// relative to the mascot so moving the mascot never waits for the snap animation.
sealed class PetPanelSpring {
    double x,y,vx,vy;
    long last;
    internal bool Active { get; private set; }
    internal void Begin(Point offset,long now) {x=offset.X;y=offset.Y;vx=vy=0;last=now;Active=true;}
    internal void Cancel() {Active=false;}
    internal Point Step(Point target,long now) {
        if(!Active)return target;
        double remaining=Math.Min(.064,Math.Max(0,now-last)/1000.0);last=now;
        while(remaining>0) {
            double dt=Math.Min(remaining,1.0/240);remaining-=dt;
            vx+=((target.X-x)*420-vx*20)/.7*dt;vy+=((target.Y-y)*420-vy*20)/.7*dt;
            x+=vx*dt;y+=vy*dt;
        }
        if(Math.Abs(target.X-x)<.5 && Math.Abs(target.Y-y)<.5 && Math.Abs(vx)<5 && Math.Abs(vy)<5) {
            Active=false;x=target.X;y=target.Y;
        }
        return new Point((int)Math.Round(x),(int)Math.Round(y));
    }
}
