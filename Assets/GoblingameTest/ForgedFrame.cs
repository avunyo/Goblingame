using UnityEngine;
namespace Goblingame.TestScene
{
    // Procedural, resolution-independent chamfered metal frame, with bevels and rivets.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ForgedFrame : UnityEngine.UI.MaskableGraphic
    {
        public float bevel=5, corner=16;
        public Color rim=new Color(.39f,.32f,.24f), interior=new Color(.055f,.06f,.06f,.95f);
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();Rect r=rectTransform.rect;
            Polygon(vh,r,corner,rim);
            Polygon(vh,new Rect(r.x+bevel,r.y+bevel,r.width-2*bevel,r.height-2*bevel),Mathf.Max(2,corner-bevel),interior);
            foreach(float x in new[]{r.x+corner*.55f,r.xMax-corner*.55f})foreach(float y in new[]{r.y+corner*.55f,r.yMax-corner*.55f})
                Polygon(vh,new Rect(x-2,y-2,4,4),1,new Color(.65f,.58f,.46f));
        }
        private static void Polygon(UnityEngine.UI.VertexHelper vh,Rect r,float cut,Color c)
        {
            cut=Mathf.Min(cut,Mathf.Min(r.width,r.height)*.45f);int start=vh.currentVertCount;
            Vector2[] p={new Vector2(r.x+cut,r.y),new Vector2(r.xMax-cut,r.y),new Vector2(r.xMax,r.y+cut),new Vector2(r.xMax,r.yMax-cut),new Vector2(r.xMax-cut,r.yMax),new Vector2(r.x+cut,r.yMax),new Vector2(r.x,r.yMax-cut),new Vector2(r.x,r.y+cut)};
            vh.AddVert(r.center,c,Vector2.zero);foreach(var v in p)vh.AddVert(v,c,Vector2.zero);
            for(int i=0;i<8;i++)vh.AddTriangle(start,start+1+i,start+1+(i+1)%8);
        }
    }
}
