using System.IO;


namespace KinectV2Viewer
{
    public class configPath
    {
        public static string BasePath = @"H:\DATN\Trained file\datasheet";
        public static string configFile = Path.Combine(BasePath, "yolov3.cfg");
        public static string weightsFile = Path.Combine(BasePath, "yolov3_4000.weights");
        public static string namesFile = Path.Combine(BasePath, "yolo.names");
    }
}
