using System.Drawing;

namespace KinectV2Viewer.Custom
{
    public class YoloItem_custom
    {
        public string Type { get; set; }   //tên của vật, ví dụ : apple, orange,.. 
        public double Confidence { get; set; }
        public int X { get; set; } //x,y là tọa độ góc trên bên trái của bounding box
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public Point Center()
        {
            return new Point(this.X + this.Width / 2, this.Y + this.Height / 2);
        }
    }
}
