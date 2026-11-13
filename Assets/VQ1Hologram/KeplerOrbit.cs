using System;
namespace VQ1Hologram
{
    [Serializable]
    public sealed class KeplerOrbit
    {
        public double epoch,a,e,i,om,w,ma,n;
        public double Period => 360.0/n;
        public void Position(double jd, out double x, out double y, out double z)
        {
            double rad=Math.PI/180;
            double m=Math.IEEERemainder((ma+n*(jd-epoch))*rad,2*Math.PI), E=m;
            for(int k=0;k<20;k++)
            {
                double step=(E-e*Math.Sin(E)-m)/(1-e*Math.Cos(E));
                E-=step; if(Math.Abs(step)<1e-13)break;
            }
            double px=a*(Math.Cos(E)-e), py=a*Math.Sqrt(1-e*e)*Math.Sin(E);
            double u=px*Math.Cos(w*rad)-py*Math.Sin(w*rad);
            double v=px*Math.Sin(w*rad)+py*Math.Cos(w*rad);
            x=u*Math.Cos(om*rad)-v*Math.Sin(om*rad)*Math.Cos(i*rad);
            y=u*Math.Sin(om*rad)+v*Math.Cos(om*rad)*Math.Cos(i*rad);
            z=v*Math.Sin(i*rad);
        }
    }
}
