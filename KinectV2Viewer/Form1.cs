
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using System.Numerics;
using System.Speech.Recognition;
using System.Speech.Synthesis;


//using Alturos.Yolo;
//using Alturos.Yolo.Model;
using KinectV2Viewer.Model;
using KinectV2Viewer.Custom;


using Microsoft.Kinect;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Kitware.VTK;
using VTKLibrary;

using PclSharp.Filters;
using PclSharp.Struct;
using PclSharp.IO;
using PclSharp.SampleConsensus;
using PclSharp.Segmentation;
using PclSharp.Std;
using PclSharp;

namespace KinectV2Viewer
{
    public partial class Form1 : Form
    {
        
        KinectSensor mySensor = null;
        MultiSourceFrameReader myReader = null;
        CoordinateMapper coordinateMapper = null;
        ColorSpacePoint[] colorSpacePoints;
        CameraSpacePoint[] cameraSpacePoints;
        public static int depthHeight = 0;
        public static int depthWidth = 0;
        public static int colorHeight = 0;
        public static int colorWidth = 0;
        public const double depthLimit = 3.0;
        public const double unitScale = 100.0;         // scale from m to cm etc.
        ushort[] depthFrameData = null;
        byte[] colorFrameData = null;

        bool kinectViewer = false;

        vtkPolyData scenePolyData = vtkPolyData.New();      
        vtkPoints points = vtkPoints.New();  //
        vtkRenderer Renderer = vtkRenderer.New();  //
        vtkRenderWindow RenderWindow = vtkRenderWindow.New();  //
        vtkRenderWindowInteractor Iren = vtkRenderWindowInteractor.New();
        vtkInteractorStyleTrackballCamera style = vtkInteractorStyleTrackballCamera.New();
        vtkPLYReader reader = vtkPLYReader.New();  //tạo bộ đọc file PLY
        vtkPolyDataMapper mapper = vtkPolyDataMapper.New();  //
        vtkActor actor = vtkActor.New();   //

        List<vtkPolyData>  Poly=new List<vtkPolyData>();
        List<vtkVertexGlyphFilter> Gly = new List<vtkVertexGlyphFilter>();
        List<vtkMapper> Mapper = new List<vtkMapper>();
        List<vtkActor> Actor = new List<vtkActor>();
        List<vtkPoints> point1 = new List<vtkPoints>();  //số lượng vật thu được sau khi phân đoạn
        List<PclSharp.Struct.PointXYZ> pointxyz = new List<PointXYZ>();   //tập hợp chuỗi các điểm có tọa độ X,Y,Z
        List<PointCloudOfXYZ> pclOfXYZ = new List<PointCloudOfXYZ>();  //

       
        

        float[] td = new float[5];
        int tinhieuduong, tinhieuam;
        float[] SetupStep = new float[5];

        
        
        public Form1()
        {
            InitializeComponent();
            //Control.CheckForIllegalCrossThreadCalls = false;
            for(int i=0;i<5;i++)
            {
                td[i] = 0;  // khi khởi động thì tất cả vị trí biến khớp của robot ở thời điểm hiện tại đều là 0 (home)
            }
            GetPortNames();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Geometry
            vtkVectorText text = vtkVectorText.New();      //cái này có thể hiểu giống như một cái polydata này
            text.SetText("Display 3D Point Clouds!");
            vtkElevationFilter elevation = vtkElevationFilter.New();       
            elevation.SetInputConnection(text.GetOutputPort());  //đầu ra của text là đầu vào của elevation
            //điều chỉnh cỡ chữ
            elevation.SetLowPoint(0, 0, 0);
            elevation.SetHighPoint(20, 0, 0);

            // mapper
            vtkPolyDataMapper textMapper = vtkPolyDataMapper.New();
            textMapper.SetInputConnection(elevation.GetOutputPort());   //sau khi lấy được elevation rồi thì cho vào đây

            // actor
            vtkActor textActor = vtkActor.New();
            textActor.SetMapper(textMapper);  //cho text mapper vào đây
            //tóm lại,quá trình sẽ là text =>elevation =>text mapper =>text actor


            // get a reference to the renderwindow of our renderWindowControl1
            /*Tạo một bộ renderwindow để hiển thị xử lý ảnh lên pictureBoxPointCloud*/
            RenderWindow.SetParentId(pictureBoxPointCloud.Handle);                         
            RenderWindow.SetSize(pictureBoxPointCloud.Width, pictureBoxPointCloud.Height);

            // Setup the background gradient
            //chỉnh tý màu cho picturebox
            Renderer.GradientBackgroundOn();
            Renderer.SetBackground(0.5, 0.5, 1.0);
            Renderer.SetBackground2(0, 0, 0);

            // add actor to the renderer
            //tạo một bộ render tên là renderer mới để chứa mấy cái xử lý ảnh, mục đích là để thêm vào renderwindow
            //sau đó thêm cái actor vừa tạo được 
            Renderer.AddActor(textActor);

            // ensure all actors are visible (in this example not necessarely needed,
            // but in case more than one actor needs to be shown it might be a good idea :))
            Renderer.ResetCamera();

            RenderWindow.AddRenderer(Renderer); //thêm cái renderer vừa tạo vào renderwindow
            RenderWindow.Render(); //chạy render

            Iren.SetRenderWindow(RenderWindow); //set render window
            Iren.SetInteractorStyle(style);  //chịu
            Iren.Start();  //run
            // tóm lại làm cái này là để bước đầu làm quen với xử lý ảnh trên thư viện vtk
            
            //Thread for handling serial communications, dùng để chạy song song đa tác vụ
            Thread updateKinect = new Thread(new ThreadStart(UpdateKinect)); //khởi tạo kinect trên một luồng khác khi load chương trình (cho chạy nhanh)
            updateKinect.IsBackground = true;
            updateKinect.Start();
            
            //nút nhấn của yolo
            btnDetect.Enabled = false;
            toolStripStatusLabelYoloInfo.Text = "";
            btnYoloSave.Enabled = false;

            //Nhận diện giọng nói
            Choices commands = new Choices(File.ReadAllLines(@"H:\DATN\nhap\code_all_ver3\KinectV2Viewer\DefaultCommands.txt"));
            //commands.Add(new String[] { "pick apple", "pick orange" });
            GrammarBuilder gBuilder = new GrammarBuilder();
            gBuilder.Append(commands);
            Grammar grammar = new Grammar(gBuilder);
            recEngine.LoadGrammarAsync(grammar);
            recEngine.SetInputToDefaultAudioDevice();
            recEngine.SpeechRecognized += recEngine_SpeechRecognized;
        }

        private void UpdateKinect()
        {
            //khởi tạo các chức năng cho camera kinect
            mySensor = KinectSensor.GetDefault();
            if (mySensor != null)
            {
                mySensor.Open();
            }
            coordinateMapper = mySensor.CoordinateMapper;   //khởi tạo vầ khai báo hệ tọa độ cho camera (lưu yý hệ tọa độ này có thể bị xoay so với hệ tọa độ robot)
            myReader = mySensor.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.Infrared | FrameSourceTypes.Body);           
            //ảnh độ sâu
            depthWidth = mySensor.DepthFrameSource.FrameDescription.Width;             //chiều rộng của độ sâu được lấy từ camera kinect
            depthHeight = mySensor.DepthFrameSource.FrameDescription.Height;           // chiều cao của độ sâu được lấy từ camera kinect
            depthFrameData = new ushort[depthWidth * depthHeight];                     // kích thước của ảnh độ sâu = rộng x cao
            cameraSpacePoints = new CameraSpacePoint[depthWidth * depthHeight];
            colorSpacePoints = new ColorSpacePoint[depthWidth * depthHeight];   //trường điểm ảnh color phải trùng kích thước với trường điểm ảnh camera thì sau này mới dùng được để lấy ra pointcloud

            //ảnh màu
            colorWidth = mySensor.ColorFrameSource.FrameDescription.Width;
            colorHeight = mySensor.ColorFrameSource.FrameDescription.Height;
            colorFrameData = new byte[colorWidth * colorHeight * 32 / 8];  //mảng 1 chiều nhưng có kích thước gấp 4 lần cái depthframedata, cái colorFrameData này sau này sẽ dùng trong getPointCloud

            myReader.MultiSourceFrameArrived += myReader_MultiSourceFrameArrived;
        }

        void myReader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            var reference = e.FrameReference.AcquireFrame();

            using (var colorFrame = reference.ColorFrameReference.AcquireFrame())
            using (var depthFrame = reference.DepthFrameReference.AcquireFrame())
            {
                if (colorFrame != null && kinectViewer == true)
                {
                    //var bitmap = Extensions.ToBitmap(frame);
                    pictureBoxColor.Image = colorFrame.ToBitmap();   //hiển thị camera màu
                }

                if (depthFrame != null && kinectViewer == true)
                {
                    //var bitmap = Extensions.ToBitmap(frame); 
                    pictureBoxDepth.Image = depthFrame.ToBitmap();  //hiển thị camera độ sâu
                }

                if (colorFrame != null && depthFrame != null && kinectViewer == true)     
                {
                    depthFrame.CopyFrameDataToArray(depthFrameData);
                    colorFrame.CopyConvertedFrameDataToArray(colorFrameData, ColorImageFormat.Bgra);
                    coordinateMapper.MapDepthFrameToColorSpace(depthFrameData, colorSpacePoints);   //ảnh 2D
                    coordinateMapper.MapDepthFrameToCameraSpace(depthFrameData, cameraSpacePoints); //ảnh 3D
                }
            }
        }
/*
tóm lại, sau hàm updatekinect thì chúng ta có gì?
Có: 1. depthFrameData,colorFrameData độ dài gấp 4 lần depthFrameData[depthWidth*depthHeight]
2. colorSpacePoint và cameraSpacePoint: được khai báo và khởi tạo từ các mảng cùng tên, trong đó độ rộng 2 mảng này là [depthWidth*depthHeight]

 */
        private void buttonStart_Click(object sender, EventArgs e)
        {
            kinectViewer = true;
            //UpdateKinect(); khởi động camera
            
        }

        private void buttonStop_Click(object sender, EventArgs e)
        {
            //mục đích: tắt camera
            kinectViewer = false;
        }
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (myReader != null)        //nếu myReader là biến được kế thừa từ biến KinectSensor.mySensor, thì chẳng phải đóng hàm mySensor là đủ rồi à ???
            {
                myReader.Dispose();
            }

            if (mySensor != null)
            {
                mySensor.Close();
            }
            
        }

        List<int> COlorX;
        List<int> COlorY;
        List<int> pc_Idx;
        //int[] ptIndices;
        //List<int[]> listptIndices;
        private void getPointsCloud()   
        {
            //tức là phải ấn nút capture point cloud thì mấy cái chuỗi này mới được khởi tạo
            COlorX = new List<int>();
            COlorY = new List<int>();
            pc_Idx = new List<int>();
            //ptIndices = new int[3];
            //listptIndices = new List<int[]>();
            vtkPoints pointss = vtkPoints.New();
            //scenePolyData = vtkPolyData.New(); bởi lúc này đã khai báo và khởi tạo rồi

            vtkUnsignedCharArray colors = vtkUnsignedCharArray.New();      //tạo ra mảng kiểu char không dấu
            colors.SetNumberOfComponents(3);     //colorx,colory,colorz
            colors.SetName("Colors");   //mảng này tên là Colors
            int ptIdx = 0;
            for (int i = 0; i < cameraSpacePoints.Length; i++)  //xét từng điểm ảnh trong trường điểm ảnh độ sâu cameraSpacePoint, lưu ý là trường điểm này có kích thước chỉ bằng 1/4 của trường colorSpacePoint
            {
                CameraSpacePoint p = cameraSpacePoints[i];     //lưu ý rằng hệ tọa độ của camera khác với hệ tọa độ của robot,sau muốn mà lấy thì phải xoay trục tđ
                Point3D pt = new Point3D();   //sau khi lấy được các điểm p và tọa độ x,y,z rồi thì xoay trục tọa độ của ngần ấy điểm đó rồi gán vào đây
                

                if (p.Z > depthLimit)
                {
                    continue;
                }

                else if (p.Z <= depthLimit)
                {
                    if (Single.IsNegativeInfinity(p.X) == true)
                    {
                        continue;
                    }

                    else if (Single.IsNegativeInfinity(p.X) == false)
                    {
                        ColorSpacePoint colPt = colorSpacePoints[i];


                        //Vì Math trong C# không có làm tròn lên nên ta phải dùng cách cộng giá trị với 0.5 rồi làm tròn, cốt để nếu thập phân >0.5 thì làm tròn lên
                        int colorX = (int)Math.Floor(colPt.X + 0.5);//30.2+0.5 = 30.7 --> 30; 30.6+0.5 = 31.1 --> 31       
                        int colorY = (int)Math.Floor(colPt.Y + 0.5);


                        if ((colorX >= 0) && (colorX < colorWidth) && (colorY >= 0) && (colorY < colorHeight))
                        {
                            COlorX.Add(colorX);
                            COlorY.Add(colorY);
                            pc_Idx.Add(ptIdx);
                            //ptIndices[0] = colorX;
                            //ptIndices[1] = colorY;
                            //ptIndices[2] = ptIdx;
                            //listptIndices.Add(ptIndices);
                            //để hiểu tại sao công thức lại ntn thì xem trong nháp
                            int colorIndex = ((colorY * colorWidth) + colorX) * 32 / 8;  // mỗi 4 điểm trong colorspacepoint sẽ mang đủ yếu tố của 1 điểm ngoài đời thực
                            //tóm lại là tưởng tượng là trải phẳng cái colorSpacePoints ra
                            Byte b = 0; Byte g = 0; Byte r = 0;

                            b = colorFrameData[colorIndex++];
                            //b=colorFrameData[colorIndex]; colorIndex = colorIndex + 1;
                            g = colorFrameData[colorIndex++];
                            r = colorFrameData[colorIndex++];
                           
                            Color color = Color.FromArgb(r, g, b);

                            pt.X = p.X * unitScale;
                            pt.Y = p.Z * unitScale;    // chuyển hệ tọa độ của camera, xoay sao cho đúng hướng với htđ của robot (tức là xoay quanh trục X)
                            pt.Z = p.Y * unitScale;     

                            //lấy các điểm p trong cameraSpacePoint,xoay trục y với z để có được tọa độ chuẩn của robot, gán vào pt, sau đó lưu vào vtkPoints là pointss
                            pointss.InsertNextPoint(pt.X, pt.Y, pt.Z);  //lấy được các điểm trong đám mây điểm nhưng mà thuộc hệ tọa độ của robot,lưu vào pointss
                            colors.InsertNextTuple3(r, g, b);    //lấy được data thông số màu sắc của từng điểm ảnh
                            ptIdx++;
                        }
                    }
                }
                
            }

            // tạo một cái scenePolyData chứa cái pointss vừa kiếm được
            scenePolyData.SetPoints(pointss);  //cái pointss này đã được khai báo và khởi tạo ở bên trên
            scenePolyData.GetPointData().SetScalars(colors); //đưa vào scenePolyData, cái này sẽ khiến đám mây điểm có màu

            //tạo một glyph filter để làm bộ lọc cho ảnh có đầu vào chính là scenePolyData
            vtkVertexGlyphFilter GlyphFilters = vtkVertexGlyphFilter.New();
            GlyphFilters.SetInput(scenePolyData);
            GlyphFilters.Update();

            scenePolyData.ShallowCopy(GlyphFilters.GetOutput());
        }

        
        private void buttonCapturePointCloud_Click(object sender, EventArgs e)
        {
         
            getPointsCloud();  //mục đích cuối cùng của cái hàm này là thay đổi cái scenePolyData
            
            if (scenePolyData.GetNumberOfPoints() > 0)
            {
                vtkVertexGlyphFilter GlyphFilter = vtkVertexGlyphFilter.New();   // tạo một cái glyphfilter tương tự như bước bên trên
                GlyphFilter.SetInput(scenePolyData);
                GlyphFilter.Update();

                vtkPolyDataMapper mapper = vtkPolyDataMapper.New();        //tương tự
                mapper.SetInputConnection(GlyphFilter.GetOutputPort());

                vtkActor actor = vtkActor.New();      //tương tự
                actor.SetMapper(mapper);

                // cái bộ renderer trước đó chứa cái textActor nay được thay thế bằng cái actor mới
                //actor mới này chứa cái scenepolydata của đám mây điểm đã được set ở hàm dựng getPointCloud
                Renderer.RemoveAllViewProps();    
                Renderer.AddActor(actor);   //add cái actor vào renderer
                //Renderer.ResetCamera();

                Renderer.SetBackground(0.1804, 0.5451, 0.3412);
                RenderWindow.AddRenderer(Renderer);
                RenderWindow.SetParentId(pictureBoxPointCloud.Handle);
                RenderWindow.SetSize(pictureBoxPointCloud.Width, pictureBoxPointCloud.Height);

                RenderWindow.Render();   //renderer này nằm trong renderwindow, nên chạy lại cái renderwindow này 

                Iren.SetRenderWindow(RenderWindow);
                Iren.SetInteractorStyle(style);
                Iren.Start();
            }
            tabControl1.SelectedIndex = 1;
        }
//đến lúc này, cái scenePolyData đang dữ liệu ảnh thực, được capture bởi capturePointCloud button

        private void buttonOpenPly_Click(object sender, EventArgs e)
        {
            //openFileDialog.Filter = "File Ply(*.ply)|*.ply|All File(*.*)|*.*";
            //openFileDialog.FilterIndex = 2;
            openFileDialog.InitialDirectory = @"H:\DATN\Trained file\Models\Models";
            tabControl1.SelectedIndex = 1;
            //openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                reader.SetFileName(openFileDialog.FileName);   //cái reader này để mở cái file ply
                reader.Update();
                scenePolyData.ShallowCopy(reader.GetOutput());  //bỏ ảnh ply này vào scenePolyData   
                vtkVertexGlyphFilter gly = vtkVertexGlyphFilter.New();
                gly.SetInput(scenePolyData);  //bỏ scenePolyData này vào glyph filter gly
                gly.Update();

                //mapper chính
                mapper.SetInputConnection(gly.GetOutputPort());    //bỏ cái gly vừa tạo được vào mapper, lưu ý là mapper này là global mapper
                //actor chính
                actor.SetMapper(mapper);    //cái actor này cũng thế
                Renderer.RemoveAllViewProps();    //bỏ hết mấy cái có trước trong renderer đi
                Renderer.AddActor(actor);   //add cái actor mới này vào
                Renderer.SetBackground(0.1804, 0.5451, 0.3412); //set background color
                RenderWindow.AddRenderer(Renderer);   //cho cái renderer này vào renderwindow
                RenderWindow.SetParentId(pictureBoxPointCloud.Handle);
                RenderWindow.SetSize(pictureBoxPointCloud.Width, pictureBoxPointCloud.Height);
                RenderWindow.Render();

                //chịu, hỏi thầy xem cái Iren này để làm gì
                Iren.SetRenderWindow(RenderWindow);
                Iren.SetInteractorStyle(style);
                Iren.Start();
            }
        }

//khi bấm openply, scenepolydata lại là data của ảnh ply lưu trong máy trước đó

        public double DistancetoPlane(double[] o, double[] n, double[] point)
        {
            //khoảng cách từ mắt camera đến vật
            //o là tọa độ của gốc
            //p là tọa độ mũi
            //n là tọa độ của pháp tuyến
            double distance = 0;
            distance = Math.Abs(n[0] * (o[0] - point[0]) + n[1] * (o[1] - point[1]) + n[2] * (o[2] - point[2]));    //tích vô hướng của 2 vector

            return distance;
        }


        private void buttonPlaneDetection_Click(object sender, EventArgs e)
        {
            double[] p = new double[3];              //là tọa độ mũi của vector pháp tuyến, trùng với cái normal thu được sau khi chạy hàm planeDetection
            double[] o = new double[3];              // tọa độ của gốc
            vtkPolyData poly = vtkPolyData.New();
            vtkPolyData poly1 = vtkPolyData.New();
            vtkPoints output = vtkPoints.New();
            poly.ShallowCopy(/*reader.GetOutput()*/scenePolyData);  //cái scenePolyData được tạo ra sau khi ấn nút openPLY, cái shallow copy này là 1 method copy an toàn 
            Functions.planeDetection(poly, 0.25, 100, ref p, ref o);   //hàm tự động phát hiện mặt phẳng
            //0.25 này gọi là inlierThreshold, tức là ngưỡng dung sai
            //100 này gọi là maxIter, hiểu nôm na là số vòng lặp tối đa mà thuật toán chạy cho việc train tìm ra mặt phẳng tốt nhất

            Console.WriteLine("p= " + p[0] + " ," + p[1] + ", " + p[2]);   //cái p này chính là tọa độ mũi của vector pháp tuyến
            Console.WriteLine("o= " + o[0] + " ," + o[1] + " ," + o[2]);

            double[] pt = new double[3];
            for (int i = 0; i < poly.GetNumberOfPoints(); i++)
            {
                //nhận tọa độ các điểm từ ảnh bằng lệnh getpoint
                pt = poly.GetPoint(i);
                //đưa vào hàm tính khoảng cách
                double distance = DistancetoPlane(o, p, pt);
                if (distance > 0.75)  //nếu khoảng cách từ mắt camera đến điểm lớn hơn 0.75 thì
                {
                    output.InsertNextPoint(pt[0], pt[1], pt[2]); //lưu tọa độ của điểm đó vào vtkPoints output
                }
            }
            Renderer.RemoveAllViewProps();
            poly1.SetPoints(output);  //khi nào dùng setpoint, khi nào getpoint
            //points=output; //lưu cái tập tọa độ điểm này vào biến global points (có vẻ khá vô dụng)
            scenePolyData.ShallowCopy(poly1);
            vtkVertexGlyphFilter gly = vtkVertexGlyphFilter.New();
            gly.SetInput(poly1);
            gly.Update();
            mapper.SetInputConnection(gly.GetOutputPort());
            actor.SetMapper(mapper);
            actor.GetProperty().SetColor(1, 1, 1);
            Renderer.AddActor(actor);

            RenderWindow.Render();

        }
        /*
         getpoint khác setpoint ở điểm nào ? và khi nào dùng getpoint,setpoint?
        1.đầu vào của setpoint là vtkpoints,đầu vào của getpoint là pointIndex
        2.setpoint là để set dữ liệu cho polyData, trong đó getpoint là để lấy ra được điểm mong muốn từ trong polydata ra
        tóm lại, thứ tự sẽ là :
        vtkpoints points;
        scenepolydata.setpoint(Points);
        double[] pt = new double[3];
        pt=scenepolydata.getpoint(i);
        output.insertnextpoint(pt[0],pt[1],pt[2]);
         */
        /*
         Đến bước này, khi ấn button test plane rồi thì scenePolyData gồm các điểm đã được xác định mặt phẳng đẹp nhất
         */

        double[] tdd = new double[4];
        private void ClusterExtraction_Click(object sender, EventArgs e)
        {
            //using (var cloud = new PointCloudOfXYZ())
            var cloud = new PointCloudOfXYZ();   //khai báo và khởi tạo một đám mây điểm tên cloud
            vtkPolyData polydata = vtkPolyData.New();   //cái polydata này NÊN được lấy từ cái scenepolydata sau khi đã tính toán phát hiện mặt phẳng
            polydata.ShallowCopy(scenePolyData/*.reader.GetOutput()*/);
            double[] toado = new double[3];
            var pointXYZ = new PclSharp.Struct.PointXYZ();
            for (int j = 0; j <= polydata.GetNumberOfPoints(); j++)
            {
                toado = polydata.GetPoint(j);   //cái này quan trọng này, khi GetPoint thì có thể lấy được 3 tọa độ x,y,z.
                pointXYZ.X = (float)toado[0];
                pointXYZ.Y = (float)toado[1];
                pointXYZ.Z = (float)toado[2];
                cloud.Add(pointXYZ);
                //tóm lại cái biến toado chỉ như là một cái buffer trung gian, mục đích là để đưa được các danh sách số lượng điểm từ polydata vào  
            }
        //đến bước này là lấy được PointClouOfXYZ cloud từ polydata    
            using (var clusterIndices = new VectorOfPointIndices())   //các vector của chỉ số điểm, hoặc có thể hiểu là các đám mây điểm để phân đoạn
            {
                using (var vg = new VoxelGridOfXYZ())  //chia lưới
                {
                   // vg.SetInputCloud(cloud);
                    //vg.LeafSize = new Vector3(0.01f);

                    var cloudFiltered = new PointCloudOfXYZ();
                    // vg.filter(cloudFiltered);

                    cloudFiltered = cloud;    //lúc này cái cloudFiltered đã chứa số lượng các điểm mà cloud vừa lấy được từ polydata
                    using (var seg = new SACSegmentationOfXYZ()
                    {
                        OptimizeCoefficients = true,
                        ModelType = SACModel.Plane,
                        MethodType = SACMethod.RANSAC,
                        MaxIterations = 1000,
                        DistanceThreshold = 0.75,//0.75;0.85
                    })
                    using (var cloudPlane = new PointCloudOfXYZ())
                    using (var coefficients = new PclSharp.Common.ModelCoefficients())// hệ số mẫu
                    using (var inliers = new PointIndices())// danh mục các điểm
                    {
                        
                        int i = 0;
                        int nrPoints = cloudFiltered.Points.Count;// nrPoints được gán số lượng các điểm pointcloud (chỉ đơn thuần là số lượng thôi nhé!!)
                        
                        while (cloudFiltered.Points.Count > 0.3 * nrPoints)
                        {
                            seg.SetInputCloud(cloudFiltered);   //đưa cái đám mây điểm vào thuật toán phân đoạn tên seg
                            seg.Segment(inliers, coefficients);   //phân đoạn với các hệ số mẫu và danh mục các điểm
                            
                            if (inliers.Indices.Count == 0)   //nếu không nhận được danh mục các điểm thì
                                Assert.Fail("could not estimate a planar model for the given dataset");
                            
                            using (var extract = new ExtractIndicesOfXYZ() { Negative = false })//khai báo danh mục các phần ảnh
                            {
                                extract.SetInputCloud(cloudFiltered);// thiết lập các điểm pointcloud đưa vào extract
                                extract.SetIndices(inliers.Indices);
                                
                                extract.filter(cloudPlane);// cloudPlane là đầu ra
                                
                                extract.Negative = true;
                                var cloudF = new PointCloudOfXYZ();
                                extract.filter(cloudF);// cloudF là các điểm đầu ra
                                
                                cloudFiltered.Dispose();  //hủy cloudFiltered
                                cloudFiltered = cloudF;   //thu nhỏ dần dần cái đám mây điểm một
                                
                            }

                            i++;
                        }
                        Console.WriteLine("==============");
                        Console.WriteLine("pt mat phang = " + coefficients.Values[0] + "x + " + coefficients.Values[1] + "y + " + coefficients.Values[2] + "z + " + coefficients.Values[3]);
                        tdd[0] = coefficients.Values[0];
                        tdd[1] = coefficients.Values[1];
                        tdd[2] = coefficients.Values[2];
                        tdd[3] = coefficients.Values[3];
                        vtkPoints point = vtkPoints.New();
                        for (int k = 0; k <= cloudFiltered.Points.Count; k++)
                        {
                            //lưu các điểm trong cloudFiltered vừa được trích xuất phân đoạn ra vào point
                            point.InsertNextPoint(cloudFiltered.Points[k].X, cloudFiltered.Points[k].Y, cloudFiltered.Points[k].Z); 

                        }
                        
                        Renderer.RemoveAllViewProps();
                        vtkPolyData poly = vtkPolyData.New();
                        poly.SetPoints(point);
                        vtkVertexGlyphFilter gly = vtkVertexGlyphFilter.New();
                        gly.SetInput(poly);
                        gly.Update();
                        mapper.SetInputConnection(gly.GetOutputPort());
                        actor.SetMapper(mapper);
                        actor.GetProperty().SetColor(1, 1, 1);
                        //Renderer.RemoveAllViewProps();
                        Renderer.AddActor(actor);
                        //RenderWindow.AddRenderer(Renderer);
                        RenderWindow.Render();

                        //Assert.IsTrue(i > 1, "Didn't find more than 1 plane");

                        //DÙNG PHƯƠNG PHÁP TREE
                        var tree = new PclSharp.Search.KdTreeOfXYZ();
                        tree.SetInputCloud(cloudFiltered);

                        using (var ec = new EuclideanClusterExtractionOfXYZ
                        {
                            ClusterTolerance = 3.5,//3.5;4
                            MinClusterSize = /*450*/200,
                            MaxClusterSize = 25000,//25000,
                        })
                        {
                            ec.SetSearchMethod(tree);// dùng phương pháp tree
                            ec.SetInputCloud(cloudFiltered);// ec nhận giá trị các điểm cloudFiltered
                            ec.Extract(clusterIndices);// đưa kết quả ra clusterIndices
                        }
                        //khi đã phân đoạn được các vật thể bắt đầu tách ra
                        var Cluster = new List<PointCloudOfXYZ>();  //tạo một chuỗi mới chứa các đám mây điểm được phân đoạn riêng biệt
                        foreach (var pis in clusterIndices)// pis là số lượng các vật thể, mỗi vật chứa 1 cụm điểm ảnh
                        {
                            //using (var cloudCluster = new PointCloudOfXYZ())/
                            var cloudCluster = new PointCloudOfXYZ(); // cloudCluster là một đám mây điểm của một vật
                            {
                                foreach (var pit in pis.Indices)// xét trong từng vật thể, pit là các điểm trong mỗi pis
                                    cloudCluster.Add(cloudFiltered.Points[pit]);   //chuyển từ pis sang cloud cluster

                                cloudCluster.Width = cloudCluster.Points.Count;
                                cloudCluster.Height = 1;
                                //Cluster.Add(cloudCluster);
                            }
                            Cluster.Add(cloudCluster); //lấy được cloudCluster rồi thì cho vào Cluster 
                        }

                        var Cluster1 = new List<PointCloudOfXYZ>();
                        foreach (var pis1 in Cluster)  //lại lấy từng đám mây điểm trong cluster một
                        {
                            var pointcloudXYZ = new PointCloudOfXYZ();
                            pointcloudXYZ = pis1;
                            var pointcloudXYZ1 = new PointCloudOfXYZ();
                            var sor = new StatisticalOutlierRemovalOfXYZ();  //loại bỏ các điểm ngoại lệ
                            sor.SetInputCloud(/*cloudFiltered*/pointcloudXYZ);  //đầu vào là từng cái pis trong Cluster,lúc này đã là pointcloudXYZ
                            sor.MeanK = 50;
                            sor.StdDevMulThresh = 2.7;//2.7;3.5
                            sor.filter(pointcloudXYZ1);    //đầu ra sau khi đã loại bỏ các điểm ngoại lệ thì cho vào cái pointcloudXYZ1
                            Cluster1.Add(pointcloudXYZ1);  //lưu vào chuỗi Cluster1
                            pclOfXYZ.Add(pointcloudXYZ1);
                        }

                        //Lưu đám mây điểm sau khi đã loại bỏ các điểm ngoại lệ của từng đám mây điểm vào một biến poin, sau đó lưu từng cái poin đó vào chuỗi global list<vtkPoints> point1
                        for (int k = 0; k < Cluster1.Count; k++)   //xét từng cụm đám mây điểm trong chuỗi các cụm đám mây điểm đã được phân đoạn với nhau
                        {
                            vtkPoints poin = vtkPoints.New();
                            PclSharp.Std.Vector<PclSharp.Struct.PointXYZ> PointXYZ;
                            PointXYZ = Cluster1[k].Points;   //tức là ở đây, thay vì dùng GetPoints của vtk thì mình dùng cái này
                            for (int h = 0; h < PointXYZ.Count; h++)
                            {
                                poin.InsertNextPoint(PointXYZ[h].X, PointXYZ[h].Y, PointXYZ[h].Z);
                            }
                            point1.Add(poin);   //sau khi lấy được các điểm rồi thì mình add vào một chuỗi tên là point1

                        }
                    
                        Renderer.RemoveAllViewProps();
                        Console.WriteLine("so vat phat hien dc =" + point1.Count);
                        for (int m = 0; m < point1.Count; m++)
                        {
                            vtkPolyData Poly1 = vtkPolyData.New();
                            vtkVertexGlyphFilter Gly1 = vtkVertexGlyphFilter.New();
                            vtkPolyDataMapper Mapper1 = vtkPolyDataMapper.New();
                            vtkActor Actor1 = vtkActor.New();
                            Poly1.SetPoints(point1[m]);
                            Gly1.SetInput(Poly1);
                            Gly1.Update();
                            Mapper1.SetInputConnection(Gly1.GetOutputPort());
                            Actor1.SetMapper(Mapper1);
                            if (m == 0)
                            {
                                Actor1.GetProperty().SetColor(1.0, 0.0, 0.0);
                            }
                            if (m == 1)
                            {
                                Actor1.GetProperty().SetColor(1.0, 0.5, 0.0);
                            }
                            if (m == 2)
                            {
                                Actor1.GetProperty().SetColor(1.0, 0.5, 0.5);
                            }
                            if (m == 3)
                            {
                                Actor1.GetProperty().SetColor(0.0, 1.0, 0.0);
                            }
                            if (m == 4)
                            {
                                Actor1.GetProperty().SetColor(0.0, 1.0, 0.5);
                            }
                            if (m == 6)
                            {
                                Actor1.GetProperty().SetColor(0.5, 1.0, 0.5);
                            }
                            if (m == 7)
                            {
                                Actor1.GetProperty().SetColor(0.0, 0.0, 1.0);
                            }
                            if (m == 8)
                            {
                                Actor1.GetProperty().SetColor(0.5, 0.0, 1.0);
                            }
                            if (m == 9)
                            {
                                Actor1.GetProperty().SetColor(0.5, 0.5, 0.5);
                            }
                            if (m == 10)
                            {
                                Actor1.GetProperty().SetColor(0.1, 0.1, 0.1);
                            }
                            if (m == 11)
                            {
                                Actor1.GetProperty().SetColor(0.2, 0.2, 0.2);
                            }
                            if (m == 12)
                            {
                                Actor1.GetProperty().SetColor(0.3, 0.3, 0.3);
                            }
                            if (m == 13)
                            {
                                Actor1.GetProperty().SetColor(0.4, 0.4, 0.4);
                            }
                            if (m == 14)
                            {
                                Actor1.GetProperty().SetColor(0.6, 0.6, 0.6);
                            }
                            if (m == 15)
                            {
                                Actor1.GetProperty().SetColor(0.7, 0.7, 0.7);
                            }
                            if (m == 16)
                            {
                                Actor1.GetProperty().SetColor(0.8, 0.8, 0.8);
                            }
                            if (m == 17)
                            {
                                Actor1.GetProperty().SetColor(0.9, 0.9, 0.9);
                            }
                            if (m == 18)
                            {
                                Actor1.GetProperty().SetColor(1.0, 1.0, 0.0);
                            }
                            if (m == 19)
                            {
                                Actor1.GetProperty().SetColor(1.0, 0.0, 1.0);
                            }
                            if (m == 20)
                            {
                                Actor1.GetProperty().SetColor(0.0, 1.0, 1.0);
                            }
                            if (m == 21)
                            {
                                Actor1.GetProperty().SetColor(1.0, 0.7, 0.4);
                            }
                            if (m > 20)
                            {
                                Actor1.GetProperty().SetColor(m * 1.0 / point1.Count, 1 - m * 1.0 / point1.Count, 0.0);
                            }

                            //Actor1.GetProperty().SetColor(m * 1.0 / point1.Count, 1 - m * 1.0 / point1.Count, 0.0);
                            Renderer.AddActor(Actor1);
                            
                            //toàn là các biến global
                            Poly.Add(Poly1);
                            Gly.Add(Gly1);
                            Mapper.Add(Mapper1);
                            Actor.Add(Actor1);

                        }
                        RenderWindow.Render();
                    }
                }
            }
        }

        private void OrientedBoundingBox_Click(object sender, EventArgs e)
        {
            vtkPoints pts = vtkPoints.New();
            
            for (int i = 0; i < Poly.Count; i++)
            {
                pts = Poly[i].GetPoints();
                //PLD = Poly[i];
                if (pts.GetNumberOfPoints() < 20) return;

                double[] Dcorner = new double[3];
                double[] Dmax = new double[3];
                double[] Dmin = new double[3];
                double[] Dmid = new double[3];
                double[] Dsize = new double[3];

                IntPtr corner = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(double)) * 3);
                IntPtr max = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(double)) * 3);
                IntPtr min = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(double)) * 3);
                IntPtr mid = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(double)) * 3);
                IntPtr size = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(double)) * 3);

                //IntPtr[] td = new IntPtr[3];
                vtkOBBTree OBB = vtkOBBTree.New();
                vtkOBBTree.ComputeOBB(pts, corner, max, min, mid, size);
                Marshal.Copy(corner, Dcorner, 0, 3);
                Marshal.Copy(max, Dmax, 0, 3);
                Marshal.Copy(min, Dmin, 0, 3);
                Marshal.Copy(mid, Dmid, 0, 3);
                Marshal.Copy(size, Dsize, 0, 3);

                //Console.WriteLine("Corner: " + Dcorner[0] + "," + Dcorner[1] + "," + Dcorner[2]);
                //Console.WriteLine("max: " + Dmax[0] + "," + Dmax[1] + "," + Dmax[2]);
                //Console.WriteLine("mid: " + Dmid[0] + "," + Dmid[1] + "," + Dmid[2]);
                //Console.WriteLine("min: " + Dmin[0] + "," + Dmin[1] + "," + Dmin[2]);
                //Console.WriteLine("size: " + Dsize[0] + "," + Dsize[1] + "," + Dsize[2]);

                vtkPoints pts1 = vtkPoints.New();
                vtkPolyLine PolyLine = vtkPolyLine.New();
                vtkCellArray cell = new vtkCellArray();
                vtkPolyDataMapper mapper1 = vtkPolyDataMapper.New();
                vtkActor actor2 = new vtkActor();
                vtkPolyData PLD = new vtkPolyData();
                
                
                double[,] p = new double[,]{
                    {Dcorner[0], Dcorner[1], Dcorner[2]},
                    {Dcorner[0] + Dmid[0], Dcorner[1] + Dmid[1], Dcorner[2] + Dmid[2]},
                    {Dcorner[0] + Dmid[0] + Dmin[0], Dcorner[1] + Dmid[1] + Dmin[1], Dcorner[2] + Dmid[2] + Dmin[2]},
                    {Dcorner[0] + Dmin[0], Dcorner[1] + Dmin[1], Dcorner[2] + Dmin[2]},
                    {Dcorner[0] + Dmax[0] + Dmin[0], Dcorner[1] + Dmax[1] + Dmin[1], Dcorner[2] + Dmax[2] + Dmin[2]},
                    {Dcorner[0] + Dmax[0], Dcorner[1] + Dmax[1], Dcorner[2] + Dmax[2]},
                    {Dcorner[0] + Dmid[0] + Dmax[0], Dcorner[1] + Dmax[1] + Dmid[1], Dcorner[2] + Dmax[2] + Dmid[2]},
                    {Dcorner[0] + Dmid[0] + Dmin[0] + Dmax[0], Dcorner[1] + Dmid[1] + Dmin[1] + Dmax[1], Dcorner[2] + Dmid[2] + Dmin[2] + Dmax[2]},
                    {Dcorner[0] + Dmax[0] + Dmin[0], Dcorner[1] + Dmax[1] + Dmin[1], Dcorner[2] + Dmax[2] + Dmin[2]},
                    {Dcorner[0] + Dmax[0], Dcorner[1] + Dmax[1], Dcorner[2] + Dmax[2]},
                    {Dcorner[0], Dcorner[1], Dcorner[2]},
                    {Dcorner[0] + Dmid[0], Dcorner[1] + Dmid[1], Dcorner[2] + Dmid[2]},
                    {Dcorner[0] + Dmid[0] + Dmax[0], Dcorner[1] + Dmax[1] + Dmid[1], Dcorner[2] + Dmax[2] + Dmid[2]},
                    {Dcorner[0] + Dmid[0] + Dmin[0] + Dmax[0], Dcorner[1] + Dmid[1] + Dmin[1] + Dmax[1], Dcorner[2] + Dmid[2] + Dmin[2] + Dmax[2]},
                    {Dcorner[0] + Dmid[0] + Dmin[0], Dcorner[1] + Dmid[1] + Dmin[1], Dcorner[2] + Dmid[2] + Dmin[2]},
                    {Dcorner[0] + Dmin[0], Dcorner[1] + Dmin[1], Dcorner[2] + Dmin[2]},
                    {Dcorner[0], Dcorner[1], Dcorner[2]},
                };
                for(int j = 0; j < 17; j++)
                    pts1.InsertNextPoint(p[j,0], p[j, 1], p[j, 2]);
                PolyLine.GetPointIds().SetNumberOfIds(17);
                for (int k = 0; k < 17; k++)
                {
                    PolyLine.GetPointIds().SetId(k, k);
                }
                cell.InsertNextCell(PolyLine);
                PLD.SetPoints(pts1);
                PLD.SetLines(cell);
                mapper1.SetInput(PLD);
                actor2.SetMapper(mapper1);
                actor2.GetProperty().SetColor(0.8, 0.8, 0.8);
                actor2.GetProperty().SetLineWidth(2);
                Renderer.AddActor(actor2);
                // tính độ dài các cạnh của đường bao
                double disMax = Math.Sqrt(Dmax[0] * Dmax[0] + Dmax[1] * Dmax[1] + Dmax[2] * Dmax[2]);
                double disMin = Math.Sqrt(Dmin[0] * Dmin[0] + Dmin[1] * Dmin[1] + Dmin[2] * Dmin[2]);
                double disMid = Math.Sqrt(Dmid[0] * Dmid[0] + Dmid[1] * Dmid[1] + Dmid[2] * Dmid[2]);
                Console.WriteLine("do dai canh max = " + disMax);
                Console.WriteLine("do dai canh min = " + disMin);
                Console.WriteLine("do dai canh midx = " + disMid);

                // tính góc
                double[,] u = new double[,] { { Dmax[0], Dmax[1] }, { 1, 0 }, { Dmin[0], Dmin[1] }, { 0, 1 } };
                double cornerx = (u[0, 0] * u[1, 0] + u[0, 1] * u[1, 1]) / (Math.Sqrt(u[0, 0] * u[0, 0] + u[0, 1] * u[0, 1]) * Math.Sqrt(u[1, 0] * u[1, 0] + u[1, 1] * u[1, 1]));
                double acx = Math.Acos(cornerx)*180/Math.PI;
                double cornery = (u[0, 0] * u[3, 0] + u[0, 1] * u[3, 1]) / (Math.Sqrt(u[0, 0] * u[0, 0] + u[0, 1] * u[0, 1]) * Math.Sqrt(u[3, 0] * u[3, 0] + u[3, 1] * u[3, 1]));
                double acy = Math.Acos(cornery) * 180 / Math.PI;
                double cornerx1 = (u[2, 0] * u[1, 0] + u[2, 1] * u[1, 1]) / (Math.Sqrt(u[2, 0] * u[2, 0] + u[2, 1] * u[2, 1]) * Math.Sqrt(u[1, 0] * u[1, 0] + u[1, 1] * u[1, 1]));
                double acx1 = Math.Acos(cornerx1) * 180 / Math.PI;
                double cornery1 = (u[2, 0] * u[3, 0] + u[3, 1] * u[2, 1]) / (Math.Sqrt(u[2, 0] * u[2, 0] + u[2, 1] * u[2, 1]) * Math.Sqrt(u[3, 0] * u[3, 0] + u[3, 1] * u[3, 1]));
                double acy1 = Math.Acos(cornery1) * 180 / Math.PI;
                Console.WriteLine("acx= " + acx + " acy= " + acy + " acx1= " + acx1 + " acy1= " + acy1);

                //tính tọa độ của tâm
                double[] t = new double[3];
                t[0] = Dcorner[0] + (Dmax[0] + Dmin[0] + Dmid[0])/2;
                t[1] = Dcorner[1] + (Dmax[1] + Dmin[1] + Dmid[1])/2;
                t[2] = Dcorner[2] + (Dmax[2] + Dmin[2] + Dmid[2])/2;
                Console.WriteLine("centre " + i + " =[" + t[0] + "," + t[1] + "," + t[2]);
                
                vtkPoints pointCenter = vtkPoints.New();
                vtkPolyLine PolyLine1 = vtkPolyLine.New();
                vtkCellArray cell3 = new vtkCellArray();
                vtkPolyDataMapper mapper3 = vtkPolyDataMapper.New();
                vtkActor actor3 = new vtkActor();
                vtkPolyData PLD3 = new vtkPolyData();
                double[,] htd = new double[,] { 
                    { t[0], t[1], t[2] }, 
                    { t[0] + 15, t[1], t[2] },
                    { t[0], t[1], t[2] },
                    { t[0], t[1]+15, t[2] },
                    { t[0], t[1], t[2] },
                    { t[0], t[1], t[2]+15 },
                    { t[0], t[1], t[2] }
                };
                for(int a=0;a<7;a++)
                {
                    pointCenter.InsertNextPoint(htd[a, 0], htd[a, 1], htd[a, 2]);
                }
                
                PolyLine1.GetPointIds().SetNumberOfIds(7);
                for(int b=0;b<7;b++)
                {
                    PolyLine1.GetPointIds().SetId(b, b);
                }
                cell3.InsertNextCell(PolyLine1);
                PLD3.SetPoints(pointCenter);
                PLD3.SetLines(cell3);
                mapper3.SetInput(PLD3);
                actor3.SetMapper(mapper3);
                actor3.GetProperty().SetColor(1, 1, 1);
                actor3.GetProperty().SetLineWidth(2);
                Renderer.AddActor(actor3);
            }
            RenderWindow.Render();
        }

        vtkUnsignedCharArray color = vtkUnsignedCharArray.New();
        
        private void Cut_Click(object sender, EventArgs e)
        {
            vtkPolyData receiveImageTo = vtkPolyData.New();
            vtkPolyData receiveImageTo1 = vtkPolyData.New();
            receiveImageTo.ShallowCopy(scenePolyData);
            vtkPoints convertPLDtoP = vtkPoints.New();
            vtkPoints limRange = vtkPoints.New();
            convertPLDtoP=receiveImageTo.GetPoints();
            double[] a,b,c,d = new double[3];
            b = convertPLDtoP.GetPoint(0);
            c = convertPLDtoP.GetPoint(0);
            color.SetNumberOfComponents(3);
            vtkDataArray SaveColor = receiveImageTo.GetPointData().GetScalars();
            
            for (int i=0;i< receiveImageTo.GetNumberOfPoints();i++)
            {
                a = receiveImageTo.GetPoint(i);
                d = SaveColor.GetTuple3(i);                         //3 color
                if (a[0] >= b[0]) { b[0] = a[0]; }
                if (a[1] >= b[1]) { b[1] = a[1]; }
                if (a[2] >= b[2]) { b[2] = a[2]; }
                if (c[0] >= a[0]) { c[0] = a[0]; }
                if (c[1] >= a[1]) { c[1] = a[1]; }
                if (c[2] >= a[2]) { c[2] = a[2]; }
                if ((a[0] > -20 && a[0] < 33) && (a[1] > 56.3 && a[1] < /*70*/68.2) && (a[2] > -5 && a[2] < 30)/*(a[0] > -20 && a[0] < 30) && (a[1] > 30 && a[1] < 71) && (a[2] > -5 && a[2] < 30)*//*(a[0] > 70 && a[0] < 510) && (a[1] > 70 && a[1] < 423) && (a[2] < 900 && a[2] > 400)*/)//x ngnag, y doc, z dung
                //x ngang, y doc, z dung
                {
                    limRange.InsertNextPoint(a[0], a[1], a[2]);
                    color.InsertNextTuple3(d[0], d[1], d[2]);
                }
            }
            
            vtkVertexGlyphFilter Gly1 = new vtkVertexGlyphFilter();
            vtkPolyDataMapper Mapper1 = vtkPolyDataMapper.New();
            vtkActor Actor1 = new vtkActor();
            receiveImageTo1.SetPoints(limRange);
            receiveImageTo1.GetPointData().SetScalars(color);
            scenePolyData.DeleteCells();
            scenePolyData.ShallowCopy(receiveImageTo1);
            Gly1.SetInput(receiveImageTo1); 
            Gly1.Update();
            Mapper1.SetInputConnection(Gly1.GetOutputPort());
            Actor1.SetMapper(Mapper1);
            Renderer.RemoveAllViewProps();
            Renderer.AddActor(Actor1);
            RenderWindow.Render();
        }

        private void buttonSavePLY_Click(object sender, EventArgs e)
        {
            vtkPLYWriter savePLY = vtkPLYWriter.New();
            savePLY.SetInput(scenePolyData);
            string path = DateTime.Now.Month.ToString() + "." + DateTime.Now.Day.ToString() + "." + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + ".ply";
            saveFileDialog1.InitialDirectory = @"H:\DATN\Trained file\Models\Models";
            saveFileDialog1.FileName = path;
            //saveFileDialog1.Filter= "File Ply(*.ply)|*.ply|All File(*.*)|*.*";
            //saveFileDialog1.FilterIndex = 2;
            //saveFileDialog1.DefaultExt = "ply";
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                savePLY.SetFileName(saveFileDialog1.FileName);
                savePLY.SetArrayName("Colors");
                savePLY.Write();
            }
        }

        private void buttonRemovingOutliers_Click(object sender, EventArgs e)
        {
            vtkPolyData PLData = vtkPolyData.New();
            PLData.ShallowCopy(scenePolyData);
            var pointXYZ = new PclSharp.Struct.PointXYZ();
            double[] cooPoint = new double[3];
            var pointcloudXYZ = new PointCloudOfXYZ();
            var pointcloudXYZ1 = new PointCloudOfXYZ();
            for (int i=0; i<=PLData.GetNumberOfPoints();i++)
            {
                cooPoint = PLData.GetPoint(i);
                pointXYZ.X = (float)cooPoint[0];
                pointXYZ.Y = (float)cooPoint[1];
                pointXYZ.Z = (float)cooPoint[2];
                pointcloudXYZ.Add(pointXYZ);
            }
            var sor=new StatisticalOutlierRemovalOfXYZ();
            sor.SetInputCloud(pointcloudXYZ);
            sor.MeanK=50;
            sor.StdDevMulThresh = 1.5;
            sor.filter(pointcloudXYZ1);

            vtkPoints point = vtkPoints.New();
            for (int k = 0; k <= pointcloudXYZ1.Points.Count; k++)
            {
                point.InsertNextPoint(pointcloudXYZ1.Points[k].X, pointcloudXYZ1.Points[k].Y, pointcloudXYZ1.Points[k].Z);

            }
            Renderer.RemoveAllViewProps();
            vtkPolyData poly = vtkPolyData.New();
            poly.SetPoints(point);
            scenePolyData.ShallowCopy(poly);
            vtkVertexGlyphFilter gly = vtkVertexGlyphFilter.New();
            gly.SetInput(poly);
            gly.Update();
            mapper.SetInputConnection(gly.GetOutputPort());
            actor.SetMapper(mapper);
            actor.GetProperty().SetColor(1, 1, 1);
            //Renderer.RemoveAllViewProps();
            Renderer.AddActor(actor);
            //RenderWindow.AddRenderer(Renderer);
            RenderWindow.Render();
        }

        List<double> phi = new List<double>();
        List<double> endpoint = new List<double>();
        private void buttonAABB_Click(object sender, EventArgs e)
        {
            axisAlignedBoundingBox();
        }
        //List<double> addmatAngleRo = new List<double>();
        void axisAlignedBoundingBox()
        {
            Console.OutputEncoding = Encoding.Unicode;
            Renderer.RemoveAllViewProps();   //hủy dữ liệu trong bộ renderer
            double[] addst = new double[899];

            double[] a, b, c, hmax = new double[3];
            //double[] color2 = new double[3];
            //vtkUnsignedCharArray color1 = vtkUnsignedCharArray.New();
            int color1 = 0;
            foreach (var poly in Poly)
            {
                color1++;    //mỗi 1 cụm đám mây điểm là 1 màu

                //YtoZ là sao ? Nó có ý nghĩa là gì 
                vtkPolyData PolyYToZ = vtkPolyData.New();
                vtkPoints PointYToZ = vtkPoints.New();
                vtkVertexGlyphFilter glyYToZ = vtkVertexGlyphFilter.New();
                vtkPolyDataMapper mapperYToZ = vtkPolyDataMapper.New();
                vtkActor actYToZ = vtkActor.New();
                //color1.SetNumberOfComponents(3);
                //vtkDataArray SaveColor = poly.GetPointData().GetScalars();
                double[] pointIn = new double[3];
                double[] pointYToZ = new double[3];
                for (int i = 0; i < poly.GetNumberOfPoints(); i++)   //xét trong từng cụm đám mây điểm của chuỗi chứa các đám mây điểm đó
                {
                    pointIn = poly.GetPoint(i);
                    //color2 = SaveColor.GetTuple3(i);
                    pointYToZ[0] = pointIn[0];   //mệt thực sự @@ đhs khó thế này cũng nghĩ ra bằng được :v
                    pointYToZ[1] = pointIn[2];
                    pointYToZ[2] = pointIn[1];
                    PointYToZ.InsertNextPoint(pointYToZ[0], pointYToZ[1], pointYToZ[2]);
                    //tóm lại, đây là thủ thuật lấy các điểm trong từng cụm đám mây điểm poly của chuỗi PolyData Poly, sau đó đưa các điểm đó vào PointYtoZ
                    //color1.InsertNextTuple3(color2[0], color2[1], color2[2]);
                }
                PolyYToZ.SetPoints(PointYToZ);
                //PolyYToZ.GetPointData().SetScalars(color1);
                glyYToZ.SetInput(PolyYToZ);

                mapperYToZ.SetInputConnection(glyYToZ.GetOutputPort());
                actYToZ.SetMapper(mapperYToZ);
                actYToZ.GetProperty().SetColor(1 - 0.2 * (color1 - 1), 0.1 * (color1 - 1), 0.2 * (color1 - 1));
                Renderer.AddActor(actYToZ);
                RenderWindow.Render();

                /*
                 Đến bước này là mình đã vẽ và hiển thị lại được các cụm đám mây điểm đã được phân đoạn và lưu trong chuỗi List<PolyData> Poly
                 */

                vtkPoints point = vtkPoints.New();
                vtkPolyData polydata = vtkPolyData.New();
                vtkPolyData polydata1 = vtkPolyData.New();
                vtkActor act1 = vtkActor.New(); //dùng cho hiển thị
                vtkActor act2 = vtkActor.New();
                vtkPolyDataMapper vmapper1 = vtkPolyDataMapper.New();
                vtkPolyDataMapper vmapper2 = vtkPolyDataMapper.New();
                b = PolyYToZ.GetPoint(0)/*poly.GetPoint(0)*/;   //lấy điểm đầu tiên của các cụm đám mây điểm, thực ra là ví dụ thế vì tý nữa nó còn thay đổi giá trị nhiều
                c = PolyYToZ.GetPoint(0)/*poly.GetPoint(0)*/;   //lấy điểm đầu tiên của các cụm đám mây điểm, thực ra là ví dụ thế vì tý nữa nó còn thay đổi giá trị nhiều
                for (int i = 0; i < /*poly*/PolyYToZ.GetNumberOfPoints(); i++)
                {
                    a = /*poly*/PolyYToZ.GetPoint(i);   // lấy ra từng điểm một trong cụm PolyYtoZ
                    if (a[0] >= b[0]) { b[0] = a[0]; }  // xmax
                    if (a[1] >= b[1]) { b[1] = a[1]; }  // ymax
                    if (a[2] >= b[2]) { b[2] = a[2]; }  // zmax  => lấy được tọa độ điểm b là tọa độ xa nhất theo cả 3 chiều x,y,z của cụm đám mây điểm
                    if (c[0] >= a[0]) { c[0] = a[0]; }  // xmin
                    if (c[1] >= a[1]) { c[1] = a[1]; }  // ymin
                    if (c[2] >= a[2]) { c[2] = a[2]; hmax[0] = a[0]; hmax[1] = a[2]; hmax[2] = a[1]; }  // zmin  => lấy được tọa độ điểm c là tọa độ gần nhất theo cả 3 chiều x,y,z của cụm đám mây điểm
                    //lưu ý: Cái tọa độ điểm hmax này lại được tính theo hệ tọa độ gốc của camera trước khi bị xoay quanh trục x ngược chiều kim đồng hồ 90 độ
                }
                // khoảng cách từ 1 điểm đến 1 mặt phẳng, xem lại toán lớp 12
                double kc = Math.Abs(hmax[0] * tdd[0] + hmax[1] * tdd[1] + hmax[2] * tdd[2] + tdd[3]) / Math.Sqrt(tdd[0] * tdd[0] + tdd[1] * tdd[1] + tdd[2] * tdd[2]);
                //cái tdd này lấy từ pt mặt phẳng khi cluster extraction
                Console.WriteLine("=============");
                Console.WriteLine("Chiều cao của vật là: " + kc);  //!
                //Renderer.RemoveAllViewProps();
                vtkPlane plane = vtkPlane.New();
                plane.SetOrigin(0.0, /*c[1]*/0.0, /*0.0*/c[2]);   //tọa độ một điểm mà cái mặt phẳng đó đi qua, thì khi này c[2] là điểm thấp nhất trong các điểm lấy được ỏw cái PolyYtoZ
                plane.SetNormal(0.0, /*1.0*/0.0, 1.0);
                //tạo trước hai thằng p và project này để tý nữa lấy điểm
                double[] p = new double[3];
                double[] projected = new double[3];
                for (int i = 0; i < /*poly*/PolyYToZ.GetNumberOfPoints(); i++)
                {
                    p = /*poly*/PolyYToZ.GetPoint(i);

                    IntPtr pP = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(double)) * 3);
                    IntPtr pProjected = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(double)) * 3);
                    Marshal.Copy(p, 0, pP, 3);
                    Marshal.Copy(projected, 0, pProjected, 3);

                    // NOTE: normal assumed to have magnitude 1
                    plane.ProjectPoint(pP, pProjected);
                    Marshal.Copy(pProjected, projected, 0, 3);
                    Marshal.FreeHGlobal(pP);
                    Marshal.FreeHGlobal(pProjected);
                    point.InsertNextPoint(projected[0], projected[1], projected[2]);
                }
                /*
                 mục đích của cái vòng for trên để làm gì? Là để chiếu tất cả các điểm trong cái PolyYtoZ lên mặt phẳng
                plane, cái mặt phẳng mà // với Oxy và đi qua điểm c[2] lúc này có vị trí thấp nhất đo được từ camera.Cuối cùng thì sau khi đã chiếu rồi, 
                tức là "hạ độ cao" trục Z của các điểm rồi, thì ta cho vào vtkPoints point.
                 */

                polydata.SetPoints(point);
                vtkPoints poi = vtkPoints.New();
                vtkPoints poi1 = vtkPoints.New();
                vtkPolyData polydat = vtkPolyData.New();
                vtkPolyData polydat2 = vtkPolyData.New();
                vtkCellArray cellarray1 = vtkCellArray.New();
                vtkCellArray cellarray2 = vtkCellArray.New();
                vtkPolyLine PolyLine = vtkPolyLine.New();
                vtkPolyLine PolyLine2 = vtkPolyLine.New();
                vtkRenderer Ren = vtkRenderer.New();
                double[,] saveArea = new double[899, 6];    //mảng 2 chiều, 899 hàng và 6 cột
                double[,] box = new double[,]
                { //cái box này là gì ?
                    {0,0,0 },
                    {10,0,0 },
                    {0,0,0 },
                    {0,10,0 },
                    {0,0,0 },
                    {0,0,10 },
                };
                //Console.WriteLine("b ");
                for (int k = 0; k < 6; k++)
                {
                    poi.InsertNextPoint(box[k, 0], box[k, 1], box[k, 2]);   //trong cái poi này có 6 điểm
                }

                PolyLine.GetPointIds().SetNumberOfIds(6);
                for (int u = 0; u < 6; u++)
                {
                    PolyLine.GetPointIds().SetId(u, u);
                }
                cellarray1.InsertNextCell(PolyLine);
                polydat.SetPoints(poi);
                polydat.SetLines(cellarray1);
                vmapper1.SetInput(polydat);
                act1.SetMapper(vmapper1);
                act1.GetProperty().SetColor(1, 1, 1);
                act1.GetProperty().SetLineWidth(2);
                Renderer.AddActor(act1);
                //RenderWindow.Render();
                //thuc hien phep quay quanh truc z de tim ra hinh bao nho nhat
                for (int i = 0; i < 899; i++)  //mỗi lần quay là sẽ quay 1/900 vòng
                {
                    vtkPoints poin = vtkPoints.New();
                    double[] t = new double[3];
                    double[] s = new double[3];
                    double dai, rong, dt;
                    for (int j = 0; j < polydata.GetNumberOfPoints(); j++)
                    {

                        t = polydata.GetPoint(j);   //đây là các điểm đã trải phẳng ra vtkplane
                        Matrix4x4 mat = new Matrix4x4();
                        mat.M11 = (float)Math.Cos(2 * i * Math.PI / 3600);
                        mat.M12 = /*0*/-(float)Math.Sin(2 * i * Math.PI / 3600);
                        mat.M13 = 0/*(float)(Math.Sin(2 * i * Math.PI / 3600))*/;
                        mat.M14 = 0;
                        mat.M21 = /*0*/(float)Math.Sin(2 * i * Math.PI / 3600);
                        mat.M22 = /*1*/(float)Math.Cos(2 * i * Math.PI / 3600);
                        mat.M23 = 0;
                        mat.M24 = 0;
                        mat.M31 = 0/*-(float)Math.Sin(2 * i * Math.PI / 3600)*/;
                        mat.M32 = 0;
                        mat.M33 = 1/*(float)Math.Cos(2 * i * Math.PI / 3600)*/;
                        mat.M34 = 0;
                        mat.M41 = 0;
                        mat.M42 = 0;
                        mat.M43 = 0;
                        mat.M44 = 1;
                        Matrix4x4 matbd = new Matrix4x4((float)t[0], (float)t[1], (float)t[2], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                        Matrix4x4 mats = new Matrix4x4();
                        mats = matbd * mat;
                        s[0] = mats.M11;
                        s[1] = mats.M12;
                        s[2] = mats.M13;
                        poin.InsertNextPoint(s[0], s[1], s[2]);    //trong này chứa các điểm nằm ngoài đường rìa của đám mây điểm của vật
                    }
                    polydata1.SetPoints(poin);   //đưa các điểm rìa đấy vào một cái polydata mới

                    double[] d, f, g = new double[3];
                    d = polydata1.GetPoint(0);
                    f = polydata1.GetPoint(0);
                    for (int h = 0; h < polydata1.GetNumberOfPoints(); h++)
                    {
                        g = polydata1.GetPoint(h);
                        if (g[0] >= d[0]) { d[0] = g[0]; }  // xmax
                        if (g[1] >= d[1]) { d[1] = g[1]; }  // ymax
                        if (g[2] >= d[2]) { d[2] = g[2]; }  // zmax
                        if (f[0] >= g[0]) { f[0] = g[0]; }  // xmin
                        if (f[1] >= g[1]) { f[1] = g[1]; }  // ymin
                        if (f[2] >= g[2]) { f[2] = g[2]; }  // zmin
                        saveArea[i, 0] = f[0];   //trong từng trường hợp
                        saveArea[i, 1] = f[1];
                        saveArea[i, 2] = f[2];
                        saveArea[i, 3] = d[0];
                        saveArea[i, 4] = d[1];
                        saveArea[i, 5] = d[2];
                    }
                    //tóm lại, vòng  lặp for h trên để làm gì? để lấy được các điểm lớn nhất, bé nhất nằm ngoài rìa từ đó vẽ bounding box theo hướng đó

                    dai = Math.Sqrt((d[0] - f[0]) * (d[0] - f[0]));      //dài tính theo phương x
                    rong = Math.Sqrt((d[1] - f[1]) * (d[1] - f[1]));     //rộng tính theo phương y
                    dt = dai * rong;
                    addst[i] = dt;   //addst lúc này chứa 900 cái diện tích của CHỈ một vật

                }
                double min = addst[0];
                int index = 0;
                for (int i = 0; i < 899; i++)
                {
                    if (addst[i] <= min)
                    {
                        index = i;
                        min = addst[i];
                    }
                }
                //sau cái vòng lặp này thì mới là lúc lấy ra được cái diện tích bé nhất, và lấy được nó nằm ở vị trí thứ mấy trong cái addst[] kia
                double[,] box1 = new double[5, 3]    //tọa độ các cạnh của cái hộp
                //cái này là lấy đúng trường hợp bé nhất thôi
                {
                    {saveArea[index, 0],saveArea[index, 1],saveArea[index, 2] },   //xmin,ymin,zmin
                    {saveArea[index, 3],saveArea[index, 1],saveArea[index, 2] },   //xmax,ymin,zmin
                    {saveArea[index, 3],saveArea[index, 4],saveArea[index, 2] },   //xmax,ymax,zmin
                    {saveArea[index, 0],saveArea[index, 4],saveArea[index, 2] },   //xmin,ymax,zmin
                    {saveArea[index, 0],saveArea[index, 1],saveArea[index, 2] },   //xmin,ymin,zmin
                };
                for (int k = 0; k < 5; k++)
                {
                    poi1.InsertNextPoint(box1[k, 0], box1[k, 1], box1[k, 2]);   //lấy ra các điểm trong cái mảng 2 chiều box1 đó, đưa vào poi1
                }
                //trong poi1 lúc này có 5 điểm
                double[] retur = new double[3];
                double[,] s1 = new double[5, 3];

                vtkPoints poi2 = vtkPoints.New();
                for (int j = 0; j < poi1.GetNumberOfPoints()/*5*/; j++)
                {
                    retur = poi1.GetPoint(j);  //lấy từng điểm trong poi1, sau đó lại đưa vào mảng 3 phần tử retur
                    Matrix4x4 mat = new Matrix4x4();   //cái ma trận này sau 5 lần duyệt thì nó vẫn thế, không thay đổi, không phải lo !
                    mat.M11 = (float)Math.Cos(-2 * index * Math.PI / 3600);
                    mat.M12 = -(float)Math.Sin(-2 * index * Math.PI / 3600);
                    mat.M13 = 0;
                    mat.M14 = 0;
                    mat.M21 = (float)Math.Sin(-2 * index * Math.PI / 3600);
                    mat.M22 = (float)Math.Cos(-2 * index * Math.PI / 3600);
                    mat.M23 = 0;
                    mat.M24 = 0;
                    mat.M31 = 0;
                    mat.M32 = 0;
                    mat.M33 = 1;
                    mat.M34 = 0;
                    mat.M41 = 0;
                    mat.M42 = 0;
                    mat.M43 = 0;
                    mat.M44 = 1;
                    Matrix4x4 matsource = new Matrix4x4((float)retur[0], (float)retur[1], (float)retur[2], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    Matrix4x4 matdestination = new Matrix4x4();
                    matdestination = matsource * mat;
                    s1[j, 0] = matdestination.M11;
                    s1[j, 1] = matdestination.M12;
                    s1[j, 2] = matdestination.M13;
                }
                double[,] box2 = new double[,]{
                    {s1[0,0],s1[0,1],s1[0,2] },
                    {s1[1,0],s1[1,1],s1[1,2] },
                    {s1[2,0],s1[2,1],s1[2,2] },
                    {s1[3,0],s1[3,1],s1[3,2] },
                    {s1[4,0],s1[4,1],s1[4,2] },
                    {s1[4,0],s1[4,1],b[2] },   //zmax
                    {s1[1,0],s1[1,1],b[2] },
                    {s1[1,0],s1[1,1],s1[1,2] },
                    {s1[1,0],s1[1,1],b[2] },
                    {s1[2,0],s1[2,1],b[2] },
                    {s1[2,0],s1[2,1],s1[2,2] },
                    {s1[2,0],s1[2,1],b[2] },
                    {s1[3,0],s1[3,1],b[2] },
                    {s1[3,0],s1[3,1],s1[3,2] },
                    {s1[3,0],s1[3,1],b[2] },
                    {s1[4,0],s1[4,1],b[2] },
                };
                for (int j = 0; j < 16; j++)
                {
                    poi2.InsertNextPoint(box2[j, 0], box2[j, 1], box2[j, 2]);   //16 điểm
                }
                PolyLine2.GetPointIds().SetNumberOfIds(16);
                for (int u = 0; u < 16; u++)
                {
                    PolyLine2.GetPointIds().SetId(u, u);
                }
                cellarray2.InsertNextCell(PolyLine2);
                polydat2.SetPoints(poi2);
                polydat2.SetLines(cellarray2);
                vmapper2.SetInput(polydat2);
                act2.SetMapper(vmapper2);
                act2.GetProperty().SetColor(1.0, 1.0, 1.0);
                act2.GetProperty().SetLineWidth(2);
                Renderer.AddActor(act2);
                RenderWindow.Render();
                Vector3[] vector3 = new Vector3[3];
                vector3[0].X = (float)(s1[1, 0] - s1[0, 0]);
                vector3[0].Y = (float)(s1[1, 1] - s1[0, 1]);
                vector3[0].Z = (float)(s1[1, 2] - s1[0, 2]);
                vector3[1].X = (float)(s1[3, 0] - s1[0, 0]);
                vector3[1].Y = (float)(s1[3, 1] - s1[0, 1]);
                vector3[1].Z = (float)(s1[3, 2] - s1[0, 2]);
                vector3[2].X = (float)(s1[4, 0] - s1[0, 0]);
                vector3[2].Y = (float)(s1[4, 1] - s1[0, 1]);
                vector3[2].Z = (float)(b[2] - s1[0, 2]);
                double[] corner = new double[] { s1[0, 0], s1[0, 1]/*c[1]*/, s1[0, 2] };
                Vector3 vector3X = vector3[0];
                Vector3 vector3Y = vector3[1];
                Vector3 vector3Z = vector3[2];
                Vector3 vector3s = new Vector3();   //vector trung gian để chuyển
                float max, mid, min1;
                for (int dem1 = 0; dem1 < 3; dem1++)
                {
                    for (int de = dem1 + 1; de < 3; de++)
                    {
                        if (vector3[de].Length() < vector3[dem1].Length())
                        {
                            vector3s = vector3[dem1];
                            vector3[dem1] = vector3[de];
                            vector3[de] = vector3s;
                        }
                    }
                }
                max = vector3[2].Length();
                mid = vector3[1].Length();
                min1 = vector3[0].Length();
                //Console.WriteLine("max= " + max + "; mid= " + mid + "; min= " + min1);
                double[] pc = new double[4];   //tọa độ của tâm của vật đo trong hệ camera (nhưng đã bị xoay trục)
                pc[0] = corner[0] + (vector3[0].X + vector3[1].X + vector3[2].X) / 2;
                pc[1] = corner[1] + (vector3[0].Y + vector3[1].Y + vector3[2].Y) / 2;
                pc[2] = corner[2] + (vector3[0].Z + vector3[1].Z + vector3[2].Z) / 2;
                pc[3] = 1;
                Console.WriteLine("Tọa độ tâm của vật đo trong hệ tọa độ camera: ");
                Console.WriteLine("center[" + pc[0] + "; " + pc[1] + "; " + pc[2] + "; " + pc[3] + "]");
                for (int add = 0; add < 4; add++)
                {
                    endpoint.Add(pc[add]); //chuỗi chứa tọa độ điểm cuối của các vật trong Poly, viết liền theo một hàng
                }
            }
            // ma tran hieu chuan toa do diem
            disposeCenter();
            tabControl2.SelectedIndex = 1;
        }


        double[] er = new double[4];
        double[,] matAngleRo = new double[3, 3];
        double[,] matAngleRo_T = new double[3, 3];
        
        private void disposeCenter()
        {
            Console.OutputEncoding = Encoding.Unicode;
            //epoint là ma trận n hàng 4 cột chứ tọa độ điểm tâm của từng vật, trong đó n hàng mỗi hàng là 1 tọa độ, cột cuối của ma trận toàn 1
            double[,] epoint = new double[endpoint.Count / 4, 4];  
            double[,] epointafter = new double[endpoint.Count / 4, 4];
            double[] ss=new double[4];
            for (int i=0;i<endpoint.Count/4;i++)
            {
                for(int j=0;j<4;j++)
                {
                    epoint[i, j] = endpoint[4 * i + j]; //khởi tạo giá trị cho epoint
                }
            }
            //sau cái này thì epoint chính xác là ma trận chứa tọa độ các điểm cuối của tâm của vật
            for(int i=0;i<endpoint.Count/4;i++)
            {
                for(int j=i+1;j<endpoint.Count/4;j++)
                {
                    if(epoint[i,0]>epoint[j,0])
                    {
                        for(int h=0;h<4;h++)
                        {
                            ss[h] = epoint[j, h];
                            epoint[j, h] = epoint[i, h];
                            epoint[i, h] = ss[h];
                        }
                    }
                    if(epoint[i, 0] == epoint[j, 0]) //trong trường hợp tọa độ X của các vật bằng nhau
                    {
                        if(epoint[i, 1] < epoint[j, 1])
                        {
                            for (int h = 0; h < 4; h++)
                            {
                                ss[h] = epoint[j, h];
                                epoint[j, h] = epoint[i, h];
                                epoint[i, h] = ss[h];
                            }
                        }
                    }
                }
            }
            /*
             tóm lại, sau vòng lặp for i kia chúng ta đã sắp xếp lại ma trân epoint với trình tự:
            1. Vật nào có tọa độ X bé nhất sẽ lên hàng đầu, càng xuống hàng dưới càng tăng dần
            2. trong trường hợp tọa độ X bằng nhau thì vật nào có tọa độ Y lớn hơn sẽ lên trước
             */
            //Toạ độ cuối cùng của vật đo được qua Camera là ma trận epoint[n,4], trong đó n là số hàng của ma trận, cũng là số vật phát hiện được

            //Ma trận chuyển toạ độ camera sang toạ độ robot
            Matrix4x4 calib = new Matrix4x4((float)0.9970, (float)0.0127, (float)0.0759, (float)-77.0964, (float)0.0088, (float)-0.9986, (float)0.0519, (float)447.8857, (float)0.0764, (float)-0.0511, (float)-0.9958, (float)675.0744, 0, 0, 0, 1);
           // Matrix4x4 calib = new Matrix4x4((float)0.6422, (float)-0.0214, (float)0.7662, (float)-917.9060, (float)0.7602, (float)0.1460, (float)-0.6331, (float)368.4494, (float)-0.0983, (float)0.9891, (float)0.1100, (float)-126.6683, 0, 0, 0, 1);
            /*Hàm chuyển đổi toạ độ của vật đo được qua toạ độ của camera về toạ độ của vật so với robot*/

            for (int k=0;k<endpoint.Count/4;k++)   //chạy vòng lặp với số lần lặp là số vật phát hiện được với trình tự như cái ma trận epoint được sắp xếp ở trên
            {
                er[0] = calib.M11 * epoint[k, 0] * 10 + calib.M12 * epoint[k, 1] * 10 + calib.M13 * epoint[k, 2] * 10 + calib.M14 * epoint[k, 3];
                er[1] = calib.M21 * epoint[k, 0] * 10 + calib.M22 * epoint[k, 1] * 10 + calib.M23 * epoint[k, 2] * 10 + calib.M24 * epoint[k, 3];
                er[2] = calib.M31 * epoint[k, 0] * 10 + calib.M32 * epoint[k, 1] * 10 + calib.M33 * epoint[k, 2] * 10 + calib.M34 * epoint[k, 3];
                er[3] = calib.M41 * epoint[k, 0] * 10 + calib.M42 * epoint[k, 1] * 10 + calib.M43 * epoint[k, 2] * 10 + calib.M44 * epoint[k, 3];

                /* toạ độ của điểm so với gốc toạ độ của robot
                   X= er[0]
                   Y= er[1]
                   Z= er[2]
                   1
                 */
                int l = k + 1;
                Console.WriteLine("==========");
                Console.WriteLine("Tọa độ tâm vật " + l + " đo trong hệ tọa độ robot" + " là "+" ["+ er[0] + ";" + er[1] + ";" + er[2] + ";" + er[3] + "]");
                double angle = Math.Atan(er[1] / er[0]);
                Console.WriteLine("Góc quay của vật " + l + " là: " + angle * 360 / (2 * Math.PI));


                /*GIải bài toán động học ngược robot bằng phương pháp các nhóm 3*/

                //Ma trận quay Cosin chuyển hướng của khâu gần cuối (nếu k tính servo là khâu cuối thì khâu này là khâu cuối) để tới được vật cần gắp
                matAngleRo[0, 0] = Math.Cos(angle); matAngleRo[0, 1] = 0; matAngleRo[0, 2] = Math.Sin(angle);
                matAngleRo[1, 0] = Math.Sin(angle); matAngleRo[1, 1] = 0; matAngleRo[1, 2] = -Math.Cos(angle);
                matAngleRo[2, 0] = 0; matAngleRo[2, 1] = 1; matAngleRo[2, 2] = 0;

                double[] R0E_d = new double[3];   
                double[] dE = new double[] { 130, 0.0, 0.0 };  //tọa độ tâm của khâu thao tác so với khâu trước nó 

                //Hàm nhân ma trận
                for (int i = 0; i < 3; i++)
                {
                    double tg = 0, tg1 = 0;                    
                    for (int j = 0; j < 3; j++)
                    {
                        tg = matAngleRo[i, j] * dE[j];
                        tg1 += tg;
                    }
                    R0E_d[i] = tg1; //cái này chỉ là hàm nhân ma trận
                }
                double[] qC = new double[3];  //KHởi tạo khớp trung gian C, cái này mới chính là tọa độ điểm trung gian C
                qC[0] = er[0] - R0E_d[0];   //Xc
                qC[1] = er[1] - R0E_d[1];   //Yc
                qC[2] = er[2] - R0E_d[2];   //Zc

                double[] q = new double[5];   //Khởi tạo biến q là góc quay của các khớp
                double a1, b1, c1, d1, h1 = 234, h2 = 221, h3 = 128, h4 = 96, h5 = 130;   //a1,b1,c1,d1 là các hệ số trong bài toán động học ngược
                q[0] = Math.Atan2(qC[1], qC[0]);
                a1 = -2 * h2 * (qC[0] * Math.Cos(q[0]) + qC[1] * Math.Sin(q[0]));
                b1 = 2 * h2 * (h1 - qC[2]);
                c1 = (h3 + h4) * (h3 + h4) - ((qC[0] * Math.Cos(q[0]) + qC[1] * Math.Sin(q[0])) * (qC[0] * Math.Cos(q[0]) + qC[1] * Math.Sin(q[0])) + h1 * h1 - 2 * h1 * qC[2] + h2 * h2 + qC[2] * qC[2]);
                d1 = Math.Atan2(b1 / Math.Sqrt(a1 * a1 + b1 * b1), a1 / Math.Sqrt(a1 * a1 + b1 * b1));
                //Góc quay của khớp 2: q[1]
                q[1] = d1 - Math.Atan2(Math.Sqrt(1 - (c1 / (Math.Sqrt(a1 * a1 + b1 * b1)) * (c1 / Math.Sqrt(a1 * a1 + b1 * b1)))), c1 / Math.Sqrt(a1 * a1 + b1 * b1));
                //Góc quay của khớp 3: q[2]
                q[2] = Math.Atan2((qC[2] - h1 - h2 * Math.Sin(q[1])), (qC[0] * Math.Cos(q[0]) + qC[1] * Math.Sin(q[0]) - h2 * Math.Cos(q[1]))) - q[1];
                //Góc quay của khớp 4: q[3]
                q[3] = Math.Asin(-Math.Cos(q[0]) * Math.Sin(q[1] + q[2]) * matAngleRo[0, 2] - Math.Sin(q[0]) * Math.Sin(q[1] + q[2]) * matAngleRo[1, 2] + Math.Cos(q[1] + q[2]) * matAngleRo[2, 2]);
                //Góc quay khớp 5: q[4]
                q[4] = Math.PI - Math.Asin(Math.Cos(q[0]) * Math.Cos(q[1] + q[2]) * matAngleRo[0, 0] + Math.Sin(q[0]) * Math.Cos(q[1] + q[2]) * matAngleRo[1, 0] + Math.Sin(q[1] + q[2]) * matAngleRo[2, 0]);

                q[0] = -(Math.PI / 2 - q[0]);   //Làm tròn góc khớp
                q[1] = -(Math.PI / 2 - q[1]);
                if (q[3] < 0.001)
                {
                    q[3] = 0;
                }
                q[4] = q[4] - Math.PI / 2;
                Console.WriteLine("q[0] = " + q[0] * 180 / (Math.PI) + ", q[1] = " + q[1] * 180 / (Math.PI) + ", q[2] = " + q[2] * 180 / (Math.PI) + ", q[3] = " + q[3] * 180 / (Math.PI) + ", q[4] = " + q[4] * 180 / (Math.PI));
                
                /*Chuyển đổi sang số đơn vị tương ứng với góc cần quay ở mỗi khớp*/
                phi.Add(((q[0] * 180 / (Math.PI))+0.5) / 0.4136);  //khớp 1 quay 1 đơn vị tương ứng 0.4136 độ Rad
                phi.Add(((q[1] * 180 / (Math.PI))+1) / 0.421);  //khớp 2 quay 1 đơn vị tương ứng 0.421 độ Rad
                phi.Add((((q[2] * 180 / (Math.PI))-2) / 0.8004)*10);  //khớp 3 quay 1 đơn vị tương ứng 0.8004 độ Rad
                phi.Add(q[3] * 180 / (Math.PI) / 4.05);    //khớp 4 quay 1 đơn vị tương ứng 4.05 độ Rad
                phi.Add(((q[4] * 180 / (Math.PI)+1.5)) / 0.9944);  //khớp 5 quay 1 đơn vị tương ứng 0.9944 độ Rad
                Console.WriteLine("Y" + q[0] * 180 / (Math.PI) / 0.4136 + " Z" + q[1] * 180 / (Math.PI) / 0.421 + " X" + q[2] * 180 / (Math.PI) / 0.8004 + " E" + q[3] * 180 / (Math.PI) / 4.05 + " H" + q[4] * 180 / (Math.PI) / 0.9944);
                
            }
            //lúc này, trong chuối phi chứa 5n góc q[i], trong đó i từ 0 đến 4 lần lượt là các góc quay của các khớp từ 1 đến 5, còn n là số vật mà phát hiện được
        }

        private void ClearPointCloud_Click(object sender, EventArgs e)
        {

            scenePolyData.RemoveDeletedCells();
        }

        private void GetPortNames()
        {
            string[] portNames = SerialPort.GetPortNames(); //load all names of  com ports to string 
            comboBoxPort.Items.Clear();                     //delete previous names in combobox items 
            foreach (string s in portNames)                 //add this names to comboboxPort items
            {
                comboBoxPort.Items.Add(s);
            }
            if (comboBoxPort.Items.Count > 0)   //if there are some com ports ,select first 
            {
                comboBoxPort.SelectedIndex = 0;
            }
            else
            {
                comboBoxPort.Text = "No COM Port "; //if there are no com ports ,write No Com Port
            }
        }

        private void buttonInitialPort_Click(object sender, EventArgs e)
        {
            try
            {
                //make sure port is not open
                if (!serialPort1.IsOpen)
                {

                    serialPort1.PortName = comboBoxPort.Text;
                    serialPort1.BaudRate = int.Parse(comboBoxBaudRate.Text);
                    serialPort1.DataReceived += serialPort1_DataReceived;
                    serialPort1.Open();
                    labelPortStatus.Text = "Connected";
                    labelPortStatus.BackColor = Color.Green;
                    listBoxReceived.Items.Clear();
                }

                else
                {
                    serialPort1.Close();
                    labelPortStatus.Text = "Port is not opened!";
                    labelPortStatus.BackColor = Color.Red;
                    listBoxReceived.Items.Clear();
                }
            }
            catch
            {
                labelPortStatus.Text = "Unauthorized Access";
                labelPortStatus.BackColor = Color.Yellow;
            }
        }

        delegate void deleDataReceived();
        void DataReceived()
        {
            if (listBoxReceived.InvokeRequired)
            {
                this.Invoke(new deleDataReceived(DataReceived));
            }
            else
            {

                if (serialPort1.IsOpen)
                {
                    string received = String.Empty;
                    try
                    {
                        received = serialPort1.ReadExisting();//ReadLine() ReadExisting

                    }
                    catch (TimeoutException)
                    {
                        received = "Timeout Exception";
                    }
                    listBoxReceived.Items.Add(received);
                }
            }
        }
        private void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            Thread DataReceivedthread = new Thread(DataReceived);
            DataReceivedthread.Start();
            //save the input from the Arduino
            /*
            if (serialPort1.IsOpen)
            {
                string received = String.Empty;
                try
                {
                    received = serialPort1.ReadExisting();//ReadLine() ReadExisting

                }
                catch (TimeoutException)
                {
                    received = "Timeout Exception";
                }
                listBoxReceived.Items.Add(received);
            }
            */
            
        }


        private void buttonGui_Click(object sender, EventArgs e)
        {
            listBoxReceived.Items.Clear();
            string lenh = textBoxNhapDuLieu.Text;
            lenh = lenh.Replace("\r\n", ";");
            string[] arrString = lenh.Split(';');//string[] arrString = lenh.Split(new char[] {'\n'})
            int i = arrString.Length;
            for (int j = 0; j < i; j++)
            {
                if (arrString[j] == "D" || arrString[j] == "M")
                {
                    if (arrString[j] == "D")
                    {
                        serialPort1.Write("#turnservo1open*");
                        Console.Write("#turnservo1open*");
                    }
                    if (arrString[j] == "M")
                    {
                        serialPort1.Write("#turnservo1close*");
                        Console.Write("#turnservo1close*");
                    }
                }
                if (arrString[j] != "D" && arrString[j] != "M")
                {
                    //Console.Write("\n" + arrString[j]);
                    serialPort1.WriteLine("\n" + arrString[j]);
                }
                //read data from serial port
                //save the input from the Arduino
                string received = String.Empty;
                try
                {
                    received = serialPort1.ReadExisting();//ReadLine
                }
                catch (TimeoutException)
                {
                    received = "Timeout Exception";
                }
                listBoxReceived.Items.Add(received);
                //Console.WriteLine(received);
                //Set poll frequency to 100Hz
                while (true)
                //For(int k = 0; k < 1000; k++)
                {
                    Thread.Sleep(10);
                    if (received.Contains("ABS") || received.Contains("#turnservo1open") || received.Contains("#turnservo1close")) break;
                    else
                    {
                        received = serialPort1.ReadExisting();
                        //Console.WriteLine(received);
                        //Set poll frequency to 100Hz
                    }
                }
            }
        }
        
        private void X_Up_Click(object sender, EventArgs e)
        {
            tinhieuduong = 0;
            string giatri = ImportXStep.Text;
            SetupStep[0] = Convert.ToInt16(giatri);
            CongToaDo();
        }
        
        private void X_Down_Click(object sender, EventArgs e)
        {
            tinhieuam = 0;
            string giatri = ImportXStep.Text;
            SetupStep[0] = Convert.ToInt16(giatri);
            TruToaDo();
        }

        private void Y_Up_Click(object sender, EventArgs e)
        {
            tinhieuduong = 1;
            string giatri = ImportYStep.Text;
            SetupStep[1] = Convert.ToInt16(giatri);
            CongToaDo();
        }

        private void Y_Down_Click(object sender, EventArgs e)
        {
            tinhieuam = 1;
            string giatri = ImportYStep.Text;
            SetupStep[1] = Convert.ToInt16(giatri);
            TruToaDo();
        }
        
        private void Z_Up_Click(object sender, EventArgs e)
        {
            tinhieuduong = 2;
            string giatri = ImportZStep.Text;
            SetupStep[2] = Convert.ToInt16(giatri);
            CongToaDo();
        }

        private void Z_Down_Click(object sender, EventArgs e)
        {
            tinhieuam = 2;
            string giatri = ImportZStep.Text;
            SetupStep[2] = Convert.ToInt16(giatri);
            TruToaDo();
        }
        
        private void E_Up_Click(object sender, EventArgs e)
        {
            tinhieuduong = 3;
            string giatri = ImportEStep.Text;
            SetupStep[3] = Convert.ToInt16(giatri);
            CongToaDo();
        }
        
        private void E_Down_Click(object sender, EventArgs e)
        {
            tinhieuam = 3;
            string giatri = ImportEStep.Text;
            SetupStep[3] = Convert.ToInt16(giatri);
            TruToaDo();
        }
        
        private void H_Up_Click(object sender, EventArgs e)
        {
            tinhieuduong = 4;
            string giatri = ImportHStep.Text;
            SetupStep[4] = Convert.ToInt16(giatri);
            CongToaDo();
        }

        private void H_Down_Click(object sender, EventArgs e)
        {
            tinhieuam = 4;
            string giatri = ImportHStep.Text;
            SetupStep[4] = Convert.ToInt16(giatri);
            TruToaDo();
        }

        private void CongToaDo()
        {
            td[tinhieuduong] += SetupStep[tinhieuduong];
            if (td[tinhieuduong] > 220)
            {
                td[tinhieuduong] = 220;
                MessageBox.Show("Giới hạn biên dương");
            }
            string convertstring;
            convertstring = td[tinhieuduong].ToString();
            if (tinhieuduong == 0)
            {
                serialPort1.WriteLine("\n" + "G01 X" + convertstring);
            }
            if (tinhieuduong == 1)
            {
                serialPort1.WriteLine("\n" + "G01 Y" + convertstring);
            }
            if (tinhieuduong == 2)
            {
                serialPort1.WriteLine("\n" + "G01 Z" + convertstring);
            }
            if (tinhieuduong == 3)
            {
                serialPort1.WriteLine("\n" + "G01 E" + convertstring);
            }
            if (tinhieuduong == 4)
            {
                serialPort1.WriteLine("\n" + "G01 H" + convertstring);
            }
        }

        private void TruToaDo()
        {
            td[tinhieuam] -= SetupStep[tinhieuam];
            if (td[tinhieuam] < -220)
            {
                td[tinhieuam] = -220;
                MessageBox.Show("Giới hạn biên âm ");
            }
            string convertstring;
            convertstring = td[tinhieuam].ToString();
            if (tinhieuam == 0)
            {
                serialPort1.WriteLine("\n" + "G01 X" + convertstring);
            }
            if (tinhieuam == 1)
            {
                serialPort1.WriteLine("\n" + "G01 Y" + convertstring);
            }
            if (tinhieuam == 2)
            {
                serialPort1.WriteLine("\n" + "G01 Z" + convertstring);
            }
            if (tinhieuam == 3)
            {
                serialPort1.WriteLine("\n" + "G01 E" + convertstring);
            }
            if (tinhieuam == 4)
            {
                serialPort1.WriteLine("\n" + "G01 H" + convertstring);
            }
        }



        private void ServoOff_Click(object sender, EventArgs e)
        {
            string close = "#turnservo1close*";
            //listBoxReceived.Items.Add("\n"+ "Servo Off");
            serialPort1.WriteLine(close);
        }

        private void ServoOn_Click(object sender, EventArgs e)
        {
            string open = "#turnservo1open*";
            serialPort1.WriteLine(open); 
            Console.Write("#turnservo1open*");
        }

        private void Record_Click(object sender, EventArgs e)
        {
            string CodeMustSave = textBoxNhapDuLieu.Text;
            MessageBox.Show(listBoxReceived.Text);
            using (StreamWriter Writer = new StreamWriter(@"G:\MachineVisionTool - Copy\FileWrite\" + DateTime.Now.Month.ToString() + "." + DateTime.Now.Day.ToString() + "." + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + ".txt", false))
            {
                Writer.WriteLine(CodeMustSave);
            }
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog op = new OpenFileDialog();
            if (op.ShowDialog() == DialogResult.OK)
            {
                string fileName;
                fileName = op.FileName;
                using (StreamReader readFile = new StreamReader(fileName))
                {
                    textBoxNhapDuLieu.Text = readFile.ReadToEnd();
                }
            }
        }

        private void Home_Click(object sender, EventArgs e)
        {
            serialPort1.WriteLine("\n" + "G01 X0 Y0 Z0 E0 H0");
            for (int i = 0; i < 5; i++)
            {
                td[i] = 0;
            }
        }

        private void buttonAutomatic_Click(object sender, EventArgs e)
        {
            //Y:0.4030851/Y1, Z:0.4125/Z1, X:0.8114/X1, E:3.86/E1, H:0.9589/H1
            int i = phi.Count / 5;
            double[] arr = new double[5];
            string[] str = new string[5];
            for(int j=0;j<i;j++)  //xét lần lượt từng vật một trong các tọa độ vật mà vừa lấy được ở disposeCenter
            {
                for(int h=0;h<5;h++)
                {
                    arr[h] = phi[h+j*5];
                    //nên nhớ rằng arr chỉ có 5 dung lượng, vậy nên sau mỗi khi load xong 1 vật trong cái for j ta phải tạo mới lại một cái arr khác
                    //Lấy lần lượt Y: phi[0], Z: phi[1], X: phi[2], E: phi[3], H: phi[4] của vật 1 khi j=0
                    str[h] = Convert.ToString(arr[h]);  //cái này dùng để gửi qua serialport
                    
                }
                double ar = arr[1] + 40;    //Z bù vào để sửa sai số gắp vật
                double arx = arr[2] +100;    //X bù vào để sửa sai số gắp vật
                double arh = arr[4]-10;     //H bù vào để sửa sai số gắp vật
                string st = Convert.ToString(ar);
                string stx = Convert.ToString(arx);
                string sth = Convert.ToString(arh);
                string[] strg = new string[11];
                //strg[0] = "G01 X" + stx+ " E" + str[3] + " H" + str[4];
                //strg[1] = "G01 Y" + str[0] + " Z" + st;
                //strg[2] = "M";
                //strg[3] = "G01 X" + str[2]+" Z"+str[1];
                //strg[4] = "D";
                //strg[5] = "G01 Z-150";
                //strg[6] = "G01 Y70";
                //strg[7] = "G01 Z" + str[1];
                //strg[8] = "M";
                //strg[9] = "G01 Z-150"+" X"+str[2];
                //strg[10] = "D";

                strg[0] = "G01 Y" + str[0]+" F20000";                                //Lệnh khởi động xoay trục Y (khâu 1) và set tốc độ là 20000
                strg[1] = "G01 X" + stx + " Z" + st + " H" + str[4];                 //Lệnh xoay các trục X bù (khâu 3), trục Z bù (khâu 2), trục H (khâu 5) (lúc này dùng góc bù để lúc mở servo thì chưa chạm vào vật)
                strg[2] = "M";                                                       //Lệnh mở servo để gắp vật
                strg[3] = "G01 X" + str[2] + " Z" + str[1];                          //Lệnh này để đưa tay gắp đến đúng vị trí của vật 
                strg[4] = "D";                                                       //Lệnh kẹp servo để giữ vật
                strg[5] = "G01 Z-100"+" H"+arh;                                      //Lệnh nhấc cánh tay lên theo trục Z (khâu 2) 150 phi và H (khâu 5) 
                strg[6] = "G01 Y70";                                                 //Lệnh xoay trục Y (khâu 1) đưa vật ra ngoài
                strg[7] = "G01 Z" + str[1]+ " H" + str[4]; ;                         //Lệnh hạ cánh tay xuống trục Z(khâu 2), trục H (khâu 5) đặt vật trên mặt bàn
                strg[8] = "M";                                                       //Lệnh mở servo để nhả vật
                strg[9] = "G01 Z-100" + " X" + str[2]+ " H" + arh;                   //Lệnh nhấc cánh tay để không chạm phải vật
                strg[10] = "D";                                                      //Lệnh đóng kẹp servo
                for (int k = 0; k < 11; k++)
                {
                    if (strg[k] == "D" || strg[k] == "M")
                    {
                        if (strg[k] == "D")
                        {
                            serialPort1.Write("#turnservo1open*");
                        }
                        if (strg[k] == "M")
                        {
                            //Console.Write("#turnservo1close*");
                            serialPort1.Write("#turnservo1close*");
                        }
                    }
                    if (strg[k] != "D" && strg[k] != "M")
                    {
                        serialPort1.WriteLine(/*"\n" + */strg[k]);
                    }
                    string received = String.Empty;
                    try
                    {
                        received = serialPort1.ReadExisting();
                    }
                    catch (TimeoutException)
                    {
                        received = "Timeout Exception";
                    }
                    listBoxReceived.Items.Add(received);
                    while (true)
                    {
                        Thread.Sleep(10);
                        if (received.Contains("ABS") || received.Contains("#turnservo1open") || received.Contains("#turnservo1close")) break;
                        else
                        {
                            received = serialPort1.ReadExisting();
                            Console.WriteLine(received);
                        }
                    }
                }
            }
        }
        
        private void buttonClear_Click(object sender, EventArgs e)
        {
            textBoxNhapDuLieu.Clear();
        }
        

     

        private void AutoImage_Click(object sender, EventArgs e)
        {
            //openfiledialog.filter = "file ply(*.ply)|*.ply|all file(*.*)|*.*";
            //openfiledialog.filterindex = 2;
            //openfiledialog.initialdirectory = "c:\\users\\chien pc\\desktop\\models\\models";
            //tabcontrol1.selectedindex = 1;
            ////openfiledialog.restoredirectory = true;
            //if (openfiledialog.showdialog() == dialogresult.ok)
            //{
            //    reader.setfilename(openfiledialog.filename);
            //    reader.update();
            //    scenepolydata.shallowcopy(reader.getoutput());
            //    vtkvertexglyphfilter gly = vtkvertexglyphfilter.new();
            //    gly.setinput(scenepolydata);
            //    gly.update();

            //    mapper.setinputconnection(gly.getoutputport());
            //    actor.setmapper(mapper);
            //    renderer.removeallviewprops();
            //    renderer.addactor(actor);
            //    renderer.setbackground(0.1804, 0.5451, 0.3412);
            //    renderwindow.addrenderer(renderer);
            //    renderwindow.setparentid(pictureboxpointcloud.handle);
            //    renderwindow.setsize(pictureboxpointcloud.width, pictureboxpointcloud.height);
            //    renderwindow.render();

            //    //iren.setrenderwindow(renderwindow);
            //    //iren.setinteractorstyle(style);
            //    //iren.start();
            //}
            kinectViewer = true;
            Thread.Sleep(2000);
            
            #region CapturePointCloud
            getPointsCloud();
            tabControl1.SelectedIndex = 1;
            if (scenePolyData.GetNumberOfPoints() > 0)
            {
                vtkVertexGlyphFilter GlyphF = vtkVertexGlyphFilter.New();
                GlyphF.SetInput(scenePolyData);
                GlyphF.Update();
                vtkPolyDataMapper mpper = vtkPolyDataMapper.New();
                mpper.SetInputConnection(GlyphF.GetOutputPort());
                vtkActor atr = vtkActor.New();
                atr.SetMapper(mpper);
                //Renderer.RemoveAllViewProps();
                Renderer.AddActor(atr);
                Renderer.ResetCamera();
                RenderWindow.Render();
            }
            #endregion
            
            #region button_Cut
            // cat vung nhin thay
            vtkPolyData receiveImageTo = vtkPolyData.New();
            vtkPolyData receiveImageTo1 = vtkPolyData.New();
            receiveImageTo.ShallowCopy(scenePolyData);
            vtkPoints convertPLDtoP = vtkPoints.New();
            vtkPoints limRange = vtkPoints.New();
            convertPLDtoP = receiveImageTo.GetPoints();
            double[] a, b, c, d = new double[3];
            b = convertPLDtoP.GetPoint(0);
            c = convertPLDtoP.GetPoint(0);
            color.SetNumberOfComponents(3);
            vtkDataArray SaveColor = receiveImageTo.GetPointData().GetScalars();

            for (int i = 0; i < receiveImageTo.GetNumberOfPoints(); i++)
            {
                a = receiveImageTo.GetPoint(i);
                d = SaveColor.GetTuple3(i);                         //3 color
                if (a[0] >= b[0]) { b[0] = a[0]; }
                if (a[1] >= b[1]) { b[1] = a[1]; }
                if (a[2] >= b[2]) { b[2] = a[2]; }
                if (c[0] >= a[0]) { c[0] = a[0]; }
                if (c[1] >= a[1]) { c[1] = a[1]; }
                if (c[2] >= a[2]) { c[2] = a[2]; }
                if ((a[0] > -20 && a[0] < 33) && (a[1] > 56.3 && a[1] < 70) && (a[2] > -5 && a[2] < 30)/*(a[0] > -20 && a[0] < 30) && (a[1] > 30 && a[1] < 71) && (a[2] > -5 && a[2] < 30)*//*(a[0] > 70 && a[0] < 510) && (a[1] > 70 && a[1] < 423) && (a[2] < 900 && a[2] > 400)*/)//x ngnag, y doc, z dung
                {
                    limRange.InsertNextPoint(a[0], a[1], a[2]);
                    color.InsertNextTuple3(d[0], d[1], d[2]);
                }
            }
            vtkVertexGlyphFilter Gly1 = new vtkVertexGlyphFilter();
            vtkPolyDataMapper Mapper1 = vtkPolyDataMapper.New();
            vtkActor Actor1 = new vtkActor();
            receiveImageTo1.SetPoints(limRange);
            receiveImageTo1.GetPointData().SetScalars(color);
            scenePolyData.DeleteCells();
            scenePolyData.ShallowCopy(receiveImageTo1);
            Gly1.SetInput(receiveImageTo1);
            Gly1.Update();
            Mapper1.SetInputConnection(Gly1.GetOutputPort());
            Actor1.SetMapper(Mapper1);
            //Renderer.RemoveAllViewProps();
            Renderer.AddActor(Actor1);
            RenderWindow.Render();
            #endregion

            #region button_ClusterExtraction
            //phan doan
            var cloud = new PointCloudOfXYZ();
            vtkPolyData polydata = vtkPolyData.New();
            polydata.ShallowCopy(scenePolyData);
            double[] toado = new double[3];
            var pointXYZ = new PclSharp.Struct.PointXYZ();
            for (int j = 0; j <= polydata.GetNumberOfPoints(); j++)
            {
                toado = polydata.GetPoint(j);
                pointXYZ.X = (float)toado[0];
                pointXYZ.Y = (float)toado[1];
                pointXYZ.Z = (float)toado[2];
                cloud.Add(pointXYZ);
            }

            using (var clusterIndices = new VectorOfPointIndices())
            {


                using (var vg = new VoxelGridOfXYZ())
                {
                    // vg.SetInputCloud(cloud);
                    //vg.LeafSize = new Vector3(0.01f);

                    var cloudFiltered = new PointCloudOfXYZ();
                    // vg.filter(cloudFiltered);

                    cloudFiltered = cloud;
                    using (var seg = new SACSegmentationOfXYZ()
                    {
                        OptimizeCoefficients = true,
                        ModelType = SACModel.Plane,
                        MethodType = SACMethod.RANSAC,
                        MaxIterations = 1000,
                        DistanceThreshold = /*0.35*/0.75,//0.5
                    })
                    using (var cloudPlane = new PointCloudOfXYZ())
                    using (var coefficients = new PclSharp.Common.ModelCoefficients())// hệ số mẫu
                    using (var inliers = new PointIndices())// danh mục các điểm
                    {

                        int i = 0;
                        int nrPoints = cloudFiltered.Points.Count;// nrPoints được gán số lượng các điểm pointcloud

                        while (cloudFiltered.Points.Count > 0.3/*0.06*/ * nrPoints)
                        {
                            seg.SetInputCloud(cloudFiltered);
                            seg.Segment(inliers, coefficients);
                            if (inliers.Indices.Count == 0)
                                Assert.Fail("could not estimate a planar model for the given dataset");

                            using (var extract = new ExtractIndicesOfXYZ() { Negative = false })//khai báo danh mục các phần ảnh
                            {
                                extract.SetInputCloud(cloudFiltered);// thiết lập các điểm pointcloud đưa vào extract
                                extract.SetIndices(inliers.Indices);

                                extract.filter(cloudPlane);// cloudPlane là đầu ra

                                extract.Negative = true;
                                var cloudF = new PointCloudOfXYZ();
                                extract.filter(cloudF);// cloudF là các điểm đầu ra

                                cloudFiltered.Dispose();
                                cloudFiltered = cloudF;

                            }

                            i++;
                        }
                        
                        vtkPoints Point = vtkPoints.New();
                        for (int k = 0; k <= cloudFiltered.Points.Count; k++)
                        {
                            Point.InsertNextPoint(cloudFiltered.Points[k].X, cloudFiltered.Points[k].Y, cloudFiltered.Points[k].Z);

                        }

                        Renderer.RemoveAllViewProps();
                        vtkPolyData poly = vtkPolyData.New();
                        poly.SetPoints(Point);
                        vtkVertexGlyphFilter gly2 = vtkVertexGlyphFilter.New();
                        gly2.SetInput(poly);
                        gly2.Update();
                        mapper.SetInputConnection(gly2.GetOutputPort());
                        actor.SetMapper(mapper);
                        actor.GetProperty().SetColor(1, 1, 1);
                        //Renderer.RemoveAllViewProps();
                        Renderer.AddActor(actor);
                        //RenderWindow.AddRenderer(Renderer);
                        RenderWindow.Render();

                        //Assert.IsTrue(i > 1, "Didn't find more than 1 plane");
                        var tree = new PclSharp.Search.KdTreeOfXYZ();
                        tree.SetInputCloud(cloudFiltered);

                        using (var ec = new EuclideanClusterExtractionOfXYZ
                        {
                            ClusterTolerance = /*3.25*/3.5,
                            MinClusterSize = 200,
                            MaxClusterSize = 3000,//25000,
                        })
                        {
                            ec.SetSearchMethod(tree);// dùng phương pháp tree
                            ec.SetInputCloud(cloudFiltered);// ec nhận giá trị các điểm cloudFiltered
                            ec.Extract(clusterIndices);// đưa kết quả ra clusterIndices
                        }
                        //khi đã phân đoạn được các vật thể bắt đầu tách ra
                        var Cluster = new List<PointCloudOfXYZ>();
                        foreach (var pis in clusterIndices)// pis là số lượng các vật thể, mỗi vật chứa 1 cụm điểm ảnh
                        {
                            //using (var cloudCluster = new PointCloudOfXYZ())// cloudCluster là các điểm ảnh trong từng vật thể
                            var cloudCluster = new PointCloudOfXYZ();
                            {
                                foreach (var pit in pis.Indices)// xét trong từng vật thể
                                    cloudCluster.Add(cloudFiltered.Points[pit]);

                                cloudCluster.Width = cloudCluster.Points.Count;
                                cloudCluster.Height = 1;
                                //Cluster.Add(cloudCluster);
                            }
                            Cluster.Add(cloudCluster);
                        }

                        var Cluster1 = new List<PointCloudOfXYZ>();
                        foreach (var pis1 in Cluster)
                        {
                            var pointcloudXYZ = new PointCloudOfXYZ();
                            pointcloudXYZ = pis1;
                            var pointcloudXYZ1 = new PointCloudOfXYZ();
                            var sor = new StatisticalOutlierRemovalOfXYZ();
                            sor.SetInputCloud(/*cloudFiltered*/pointcloudXYZ);
                            sor.MeanK = 50;
                            sor.StdDevMulThresh = 2.7;//phai 7, giua tren 4, chinh giua 7, 1.25
                            sor.filter(pointcloudXYZ1);
                            Cluster1.Add(pointcloudXYZ1);
                            pclOfXYZ.Add(pointcloudXYZ1);
                        }


                        for (int k = 0; k < Cluster1.Count; k++)
                        {
                            vtkPoints poin = vtkPoints.New();
                            PclSharp.Std.Vector<PclSharp.Struct.PointXYZ> PointXYZ;
                            PointXYZ = Cluster1[k].Points;
                            for (int h = 0; h < PointXYZ.Count; h++)
                            {
                                poin.InsertNextPoint(PointXYZ[h].X, PointXYZ[h].Y, PointXYZ[h].Z);
                            }
                            point1.Add(poin);

                        }

                        
                        //Renderer.RemoveAllViewProps();
                        Console.WriteLine("so vat phat hien dc =" + point1.Count);
                        for (int m = 0; m < point1.Count; m++)
                        {
                            vtkPolyData Poly1 = vtkPolyData.New();
                            vtkVertexGlyphFilter Gly2 = vtkVertexGlyphFilter.New();
                            vtkPolyDataMapper Mapper2 = vtkPolyDataMapper.New();
                            vtkActor Actor2 = vtkActor.New();
                            Poly1.SetPoints(point1[m]);
                            Gly2.SetInput(Poly1);
                            Gly2.Update();
                            Mapper2.SetInputConnection(Gly2.GetOutputPort());
                            Actor2.SetMapper(Mapper2);
                            if (m == 0)
                            {
                                Actor2.GetProperty().SetColor(1.0, 0.0, 0.0);
                            }
                            if (m == 1)
                            {
                                Actor2.GetProperty().SetColor(1.0, 0.5, 0.0);
                            }
                            if (m == 2)
                            {
                                Actor2.GetProperty().SetColor(1.0, 0.5, 0.5);
                            }
                            if (m == 3)
                            {
                                Actor2.GetProperty().SetColor(0.0, 1.0, 0.0);
                            }
                            if (m == 4)
                            {
                                Actor2.GetProperty().SetColor(0.0, 1.0, 0.5);
                            }
                            if (m == 6)
                            {
                                Actor2.GetProperty().SetColor(0.5, 1.0, 0.5);
                            }
                            if (m == 7)
                            {
                                Actor2.GetProperty().SetColor(0.0, 0.0, 1.0);
                            }
                            if (m == 8)
                            {
                                Actor2.GetProperty().SetColor(0.5, 0.0, 1.0);
                            }
                            if (m == 9)
                            {
                                Actor2.GetProperty().SetColor(0.5, 0.5, 0.5);
                            }
                            if (m == 10)
                            {
                                Actor2.GetProperty().SetColor(0.1, 0.1, 0.1);
                            }
                            if (m == 11)
                            {
                                Actor2.GetProperty().SetColor(0.2, 0.2, 0.2);
                            }
                            if (m == 12)
                            {
                                Actor2.GetProperty().SetColor(0.3, 0.3, 0.3);
                            }
                            if (m == 13)
                            {
                                Actor2.GetProperty().SetColor(0.4, 0.4, 0.4);
                            }
                            if (m == 14)
                            {
                                Actor2.GetProperty().SetColor(0.6, 0.6, 0.6);
                            }
                            if (m == 15)
                            {
                                Actor2.GetProperty().SetColor(0.7, 0.7, 0.7);
                            }
                            if (m == 16)
                            {
                                Actor2.GetProperty().SetColor(0.8, 0.8, 0.8);
                            }
                            if (m == 17)
                            {
                                Actor2.GetProperty().SetColor(0.9, 0.9, 0.9);
                            }
                            if (m == 18)
                            {
                                Actor2.GetProperty().SetColor(1.0, 1.0, 0.0);
                            }
                            if (m == 19)
                            {
                                Actor2.GetProperty().SetColor(1.0, 0.0, 1.0);
                            }
                            if (m == 20)
                            {
                                Actor2.GetProperty().SetColor(0.0, 1.0, 1.0);
                            }
                            if (m == 21)
                            {
                                Actor2.GetProperty().SetColor(1.0, 0.7, 0.4);
                            }
                            if (m > 20)
                            {
                                Actor2.GetProperty().SetColor(m * 1.0 / point1.Count, 1 - m * 1.0 / point1.Count, 0.0);
                            }

                            //Actor1.GetProperty().SetColor(m * 1.0 / point1.Count, 1 - m * 1.0 / point1.Count, 0.0);
                            Renderer.AddActor(Actor2);

                            Poly.Add(Poly1);
                            Gly.Add(Gly2);
                            Mapper.Add(Mapper2);
                            Actor.Add(Actor2);

                        }
                        RenderWindow.Render();
                    }
                }
            }
            #endregion

            #region button_BoundingBox
            //tim hinh bao
            double[] addst = new double[899];
            double[] a1, b1, c1 = new double[3];
            Renderer.RemoveAllViewProps();
            foreach (var poly in Poly)
            {
                vtkPolyData PolyYToZ = vtkPolyData.New();
                vtkPoints PointYToZ = vtkPoints.New();
                vtkVertexGlyphFilter glyYToZ = vtkVertexGlyphFilter.New();
                vtkPolyDataMapper mapperYToZ = vtkPolyDataMapper.New();
                vtkActor actYToZ = vtkActor.New();
                double[] pointIn = new double[3];
                double[] pointYToZ = new double[3];
                for (int i = 0; i < poly.GetNumberOfPoints(); i++)
                {
                    pointIn = poly.GetPoint(i);
                    pointYToZ[0] = pointIn[0];
                    pointYToZ[1] = pointIn[2];
                    pointYToZ[2] = pointIn[1];
                    PointYToZ.InsertNextPoint(pointYToZ[0], pointYToZ[1], pointYToZ[2]);
                }
                PolyYToZ.SetPoints(PointYToZ);
                glyYToZ.SetInput(PolyYToZ);
                
                mapperYToZ.SetInputConnection(glyYToZ.GetOutputPort());
                actYToZ.SetMapper(mapperYToZ);
                Renderer.AddActor(actYToZ);
                RenderWindow.Render();

                vtkPoints point = vtkPoints.New();
                vtkPolyData polydt = vtkPolyData.New();
                vtkPolyData polydata1 = vtkPolyData.New();
                vtkActor act1 = vtkActor.New();
                vtkActor act2 = vtkActor.New();
                vtkPolyDataMapper vmapper1 = vtkPolyDataMapper.New();
                vtkPolyDataMapper vmapper2 = vtkPolyDataMapper.New();
                b1 = PolyYToZ.GetPoint(0)/*poly.GetPoint(0)*/;
                c1 = PolyYToZ.GetPoint(0)/*poly.GetPoint(0)*/;
                for (int i = 0; i < /*poly*/PolyYToZ.GetNumberOfPoints(); i++)
                {
                    a1 = /*poly*/PolyYToZ.GetPoint(i);
                    if (a1[0] >= b1[0]) { b1[0] = a1[0]; }  // xmax
                    if (a1[1] >= b1[1]) { b1[1] = a1[1]; }  // ymax
                    if (a1[2] >= b1[2]) { b1[2] = a1[2]; }  // zmax
                    if (c1[0] >= a1[0]) { c1[0] = a1[0]; }  // xmin
                    if (c1[1] >= a1[1]) { c1[1] = a1[1]; }  // ymin
                    if (c1[2] >= a1[2]) { c1[2] = a1[2]; }  // zmin
                }
                //Renderer.RemoveAllViewProps();
                vtkPlane plane = vtkPlane.New();
                plane.SetOrigin(0.0, /*c[1]*/0.0, /*0.0*/c1[2]);
                plane.SetNormal(0.0, /*1.0*/0.0, 1.0);
                double[] p = new double[3];
                double[] projected = new double[3];
                for (int i = 0; i < /*poly*/PolyYToZ.GetNumberOfPoints(); i++)
                {
                    p = /*poly*/PolyYToZ.GetPoint(i);

                    IntPtr pP = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(double)) * 3);
                    IntPtr pProjected = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(double)) * 3);
                    Marshal.Copy(p, 0, pP, 3);
                    Marshal.Copy(projected, 0, pProjected, 3);

                    // NOTE: normal assumed to have magnitude 1
                    plane.ProjectPoint(pP, pProjected);
                    Marshal.Copy(pProjected, projected, 0, 3);
                    Marshal.FreeHGlobal(pP);
                    Marshal.FreeHGlobal(pProjected);
                    point.InsertNextPoint(projected[0], projected[1], projected[2]);
                }
                polydt.SetPoints(point);
                vtkPoints poi = vtkPoints.New();
                vtkPoints poi1 = vtkPoints.New();
                vtkPolyData polydat = vtkPolyData.New();
                vtkPolyData polydat2 = vtkPolyData.New();
                vtkCellArray cellarray1 = vtkCellArray.New();
                vtkCellArray cellarray2 = vtkCellArray.New();
                vtkPolyLine PolyLine = vtkPolyLine.New();
                vtkPolyLine PolyLine2 = vtkPolyLine.New();
                vtkRenderer Ren = vtkRenderer.New();
                double[,] saveArea = new double[899, 6];
                double[,] box = new double[,] {
                    {0,0,0 },
                    {10,0,0 },
                    {0,0,0 },
                    {0,10,0 },
                    {0,0,0 },
                    {0,0,10 },
                };
                //Console.WriteLine("b ");
                for (int k = 0; k < 6; k++)
                {
                    poi.InsertNextPoint(box[k, 0], box[k, 1], box[k, 2]);
                }

                PolyLine.GetPointIds().SetNumberOfIds(6);
                for (int u = 0; u < 6; u++)
                {
                    PolyLine.GetPointIds().SetId(u, u);
                }
                cellarray1.InsertNextCell(PolyLine);
                polydat.SetPoints(poi);
                polydat.SetLines(cellarray1);
                vmapper1.SetInput(polydat);
                act1.SetMapper(vmapper1);
                act1.GetProperty().SetColor(1, 1, 1);
                act1.GetProperty().SetLineWidth(2);
                Renderer.AddActor(act1);
                //RenderWindow.Render();
                //thuc hien phep quay quanh truc z
                for (int i = 0; i < 899; i++)
                {
                    vtkPoints poin1 = vtkPoints.New();
                    double[] t = new double[3];
                    double[] s = new double[3];
                    double dai, rong, dt;
                    for (int j = 0; j < polydt.GetNumberOfPoints(); j++)
                    {

                        t = polydt.GetPoint(j);
                        Matrix4x4 mat = new Matrix4x4();
                        mat.M11 = (float)Math.Cos(2 * i * Math.PI / 3600);
                        mat.M12 = /*0*/-(float)Math.Sin(2 * i * Math.PI / 3600);
                        mat.M13 = 0/*(float)(Math.Sin(2 * i * Math.PI / 3600))*/;
                        mat.M14 = 0;
                        mat.M21 = /*0*/(float)Math.Sin(2 * i * Math.PI / 3600);
                        mat.M22 = /*1*/(float)Math.Cos(2 * i * Math.PI / 3600);
                        mat.M23 = 0;
                        mat.M24 = 0;
                        mat.M31 = 0/*-(float)Math.Sin(2 * i * Math.PI / 3600)*/;
                        mat.M32 = 0;
                        mat.M33 = 1/*(float)Math.Cos(2 * i * Math.PI / 3600)*/;
                        mat.M34 = 0;
                        mat.M41 = 0;
                        mat.M42 = 0;
                        mat.M43 = 0;
                        mat.M44 = 1;
                        Matrix4x4 matbd = new Matrix4x4((float)t[0], (float)t[1], (float)t[2], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                        Matrix4x4 mats = new Matrix4x4();
                        mats = matbd * mat;
                        s[0] = mats.M11;
                        s[1] = mats.M12;
                        s[2] = mats.M13;
                        poin1.InsertNextPoint(s[0], s[1], s[2]);
                    }
                    polydata1.SetPoints(poin1);

                    double[] d1, f, g = new double[3];
                    d1 = polydata1.GetPoint(0);
                    f = polydata1.GetPoint(0);
                    for (int h = 0; h < polydata1.GetNumberOfPoints(); h++)
                    {
                        g = polydata1.GetPoint(h);
                        if (g[0] >= d1[0]) { d1[0] = g[0]; }  // xmax
                        if (g[1] >= d1[1]) { d1[1] = g[1]; }  // ymax
                        if (g[2] >= d1[2]) { d1[2] = g[2]; }  // zmax
                        if (f[0] >= g[0]) { f[0] = g[0]; }  // xmin
                        if (f[1] >= g[1]) { f[1] = g[1]; }  // ymin
                        if (f[2] >= g[2]) { f[2] = g[2]; }  // zmin
                        saveArea[i, 0] = f[0];
                        saveArea[i, 1] = f[1];
                        saveArea[i, 2] = f[2];
                        saveArea[i, 3] = d1[0];
                        saveArea[i, 4] = d1[1];
                        saveArea[i, 5] = d1[2];
                    }

                    dai = Math.Sqrt((d1[0] - f[0]) * (d1[0] - f[0]));
                    rong = Math.Sqrt((d1[1] - f[1]) * (d1[1] - f[1]));
                    dt = dai * rong;
                    addst[i] = dt;

                }
                double min = addst[0];
                int index = 0;
                for (int i = 0; i < 899; i++)
                {
                    if (addst[i] <= min)
                    {
                        index = i;
                        min = addst[i];
                    }
                }
                double[,] box1 = new double[,] {
                        {saveArea[index, 0],saveArea[index, 1],saveArea[index, 2] },
                        {saveArea[index, 3],saveArea[index, 1],saveArea[index, 2] },
                        {saveArea[index, 3],saveArea[index, 4],saveArea[index, 2] },
                        {saveArea[index, 0],saveArea[index, 4],saveArea[index, 2] },
                        {saveArea[index, 0],saveArea[index, 1],saveArea[index, 2] },
                    };
                for (int k = 0; k < 5; k++)
                {
                    poi1.InsertNextPoint(box1[k, 0], box1[k, 1], box1[k, 2]);
                }
                double[] retur = new double[3];
                double[,] s1 = new double[5, 3];

                vtkPoints poi2 = vtkPoints.New();
                for (int j = 0; j < poi1.GetNumberOfPoints(); j++)
                {
                    retur = poi1.GetPoint(j);
                    Matrix4x4 mat = new Matrix4x4();
                    mat.M11 = (float)Math.Cos(-2 * index * Math.PI / 3600);
                    mat.M12 = -(float)Math.Sin(-2 * index * Math.PI / 3600);
                    mat.M13 = 0;
                    mat.M14 = 0;
                    mat.M21 = (float)Math.Sin(-2 * index * Math.PI / 3600);
                    mat.M22 = (float)Math.Cos(-2 * index * Math.PI / 3600);
                    mat.M23 = 0;
                    mat.M24 = 0;
                    mat.M31 = 0;
                    mat.M32 = 0;
                    mat.M33 = 1;
                    mat.M34 = 0;
                    mat.M41 = 0;
                    mat.M42 = 0;
                    mat.M43 = 0;
                    mat.M44 = 1;
                    Matrix4x4 matsource = new Matrix4x4((float)retur[0], (float)retur[1], (float)retur[2], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    Matrix4x4 matdestination = new Matrix4x4();
                    matdestination = matsource * mat;
                    s1[j, 0] = matdestination.M11;
                    s1[j, 1] = matdestination.M12;
                    s1[j, 2] = matdestination.M13;
                }
                double[,] box2 = new double[,]{
                    {s1[0,0],s1[0,1],s1[0,2] },
                    {s1[1,0],s1[1,1],s1[1,2] },
                    {s1[2,0],s1[2,1],s1[2,2] },
                    {s1[3,0],s1[3,1],s1[3,2] },
                    {s1[4,0],s1[4,1],s1[4,2] },
                    {s1[4,0],s1[4,1],b1[2] },
                    {s1[1,0],s1[1,1],b1[2] },
                    {s1[1,0],s1[1,1],s1[1,2] },
                    {s1[1,0],s1[1,1],b1[2] },
                    {s1[2,0],s1[2,1],b1[2] },
                    {s1[2,0],s1[2,1],s1[2,2] },
                    {s1[2,0],s1[2,1],b1[2] },
                    {s1[3,0],s1[3,1],b1[2] },
                    {s1[3,0],s1[3,1],s1[3,2] },
                    {s1[3,0],s1[3,1],b1[2] },
                    {s1[4,0],s1[4,1],b1[2] },
                };
                for (int j = 0; j < 16; j++)
                {
                    poi2.InsertNextPoint(box2[j, 0], box2[j, 1], box2[j, 2]);
                }
                PolyLine2.GetPointIds().SetNumberOfIds(16);
                for (int u = 0; u < 16; u++)
                {
                    PolyLine2.GetPointIds().SetId(u, u);
                }
                cellarray2.InsertNextCell(PolyLine2);
                polydat2.SetPoints(poi2);
                polydat2.SetLines(cellarray2);
                vmapper2.SetInput(polydat2);
                act2.SetMapper(vmapper2);
                act2.GetProperty().SetColor(1.0, 0.5, 0.0);
                act2.GetProperty().SetLineWidth(2);
                Renderer.AddActor(act2);
                RenderWindow.Render();
                Vector3[] vector3 = new Vector3[3];
                vector3[0].X = (float)(s1[1, 0] - s1[0, 0]);
                vector3[0].Y = (float)(s1[1, 1] - s1[0, 1]);
                vector3[0].Z = (float)(s1[1, 2] - s1[0, 2]);
                vector3[1].X = (float)(s1[3, 0] - s1[0, 0]);
                vector3[1].Y = (float)(s1[3, 1] - s1[0, 1]);
                vector3[1].Z = (float)(s1[3, 2] - s1[0, 2]);
                vector3[2].X = (float)(s1[4, 0] - s1[0, 0]);
                vector3[2].Y = (float)(s1[4, 1] - s1[0, 1]);
                vector3[2].Z = (float)(b1[2] - s1[0, 2]);
                double[] corner = new double[] { s1[0, 0], s1[0, 1]/*c[1]*/, s1[0, 2] };
                Vector3 vector3X = vector3[0];
                Vector3 vector3Y = vector3[1];
                Vector3 vector3Z = vector3[2];
                Vector3 vector3s = new Vector3();
                float max, mid, min1;
                for (int dem1 = 0; dem1 < 3; dem1++)
                {
                    for (int de = dem1 + 1; de < 3; de++)
                    {
                        if (vector3[de].Length() < vector3[dem1].Length())
                        {
                            vector3s = vector3[dem1];
                            vector3[dem1] = vector3[de];
                            vector3[de] = vector3s;
                        }
                    }
                }
                max = vector3[2].Length();
                mid = vector3[1].Length();
                min1 = vector3[0].Length();
                Console.WriteLine("max= " + max + "; mid= " + mid + "; min= " + min1);
                double[] pc = new double[4];
                pc[0] = corner[0] + (vector3[0].X + vector3[1].X + vector3[2].X) / 2;
                pc[1] = corner[1] + (vector3[0].Y + vector3[1].Y + vector3[2].Y) / 2;
                pc[2] = corner[2] + (vector3[0].Z + vector3[1].Z + vector3[2].Z) / 2;
                pc[3] = 1;
                Console.WriteLine("center[" + pc[0] + "; " + pc[1] + "; " + pc[2] + "]");
                for (int add = 0; add < 4; add++)
                {
                    endpoint.Add(pc[add]);
                }
                // ma tran hieu chuan toa do diem


            }
            disposeCenter();
            tabControl2.SelectedIndex = 1;
            #endregion
        }


        // PHÁT HIỆN VẬT THỂ BẰNG ALTUROS YOLO

        YoloWrapper_custom YOLO;    //khai báo một wrapper cho phần mềm yolo

        void YoloV3()
        {

            var GpuConfig = new GpuConfig_custom();
            GpuConfig.GpuIndex = 0;
            
            // khởi tạo wrapper đó dùng cho detect (trong đó sử dụng phần cứng là GPU)
            YOLO = new YoloWrapper_custom(configPath.configFile, configPath.weightsFile, configPath.namesFile,GpuConfig);

        }
        

        
        private ImageInfo GetCurrentImage()
        {
            //sau khi đưa được ảnh vào trong datagridviewFile rồi thì ta cần lấy thông tin mà ảnh có để làm các tác vụ khác
            ImageInfo item;
            item = (ImageInfo)(this.dataGridViewFile.CurrentRow?.DataBoundItem);
            return item;
        }

        private void DetectSelectedImage()    
        {
            var items = this.Detect();
            yoloitems = this.Detect();
            this.dataGridViewResult.DataSource = items;
            this.DrawBoundingBoxes(items);
        }

        //tạo list các detected items trong ảnh đang được hiển thị trên picturebox (mục đích để đưa data vào datagridviewresult)
        private List<YoloItem_custom> Detect(bool memoryTransfer = true)    
        {
            if (this.YOLO == null)
            {
                MessageBox.Show("YoloV3 is not init...");
                return null;
            }

            var imageInfo = this.GetCurrentImage();
            var imageData = File.ReadAllBytes(imageInfo.Path);
            //List<YoloItem> items;
            var sw = new Stopwatch();
            sw.Start();
            List<YoloItem_custom> items;
            if (memoryTransfer)
            {
                items = this.YOLO.Detect(imageData).ToList();
            }
            else
            {
                items = this.YOLO.Detect(imageInfo.Path).ToList();
            }
            sw.Stop();
            this.groupBoxResult.Text = $"Result [ Processed in {sw.Elapsed.TotalMilliseconds:0} ms ]";
            return items;
        }
        private Brush GetBrush(double confidence)
        {
            if (confidence > 0.5)
            {
                return Brushes.Green;
            }
            else if (confidence >= 0.2 && confidence <= 0.5)
            {
                return Brushes.Orange;
            }
            return Brushes.DarkRed;
        }
        private void DrawBoundingBoxes(List<YoloItem_custom> items, YoloItem_custom selectedItem = null)
        {
            //Lấy đường dẫn của ảnh trong DataGridViewFile 
            var imageInfo = GetCurrentImage();
            var image = Image.FromFile(imageInfo.Path);

            //Khai báo font chữ dùng định danh vật hiển thị trên ảnh
            var font = new Font(FontFamily.GenericSansSerif, 20);

            //vẽ đường bao cho vật
            using (var canvas = Graphics.FromImage(image))
            {
                foreach (var item in items)
                {
                    var x = item.X;
                    var y = item.Y;
                    var width = item.Width;
                    var height = item.Height;

                    var brush = this.GetBrush(item.Confidence);
                    var penSize = image.Width / 100.0f;

                    using (var pen = new Pen(brush, penSize))
                    {
                        canvas.DrawRectangle(pen, x, y, width, height);
                        canvas.FillRectangle(brush, x - (penSize / 2), y - 15, width + penSize, 25);
                        canvas.DrawString(item.Type.ToString(), font, Brushes.White, item.X, item.Y - 20);
                    }
                }
                canvas.Flush();
            }
            var oldImage = this.pictureBox1.Image;
            this.pictureBox1.Image = image;
            oldImage?.Dispose();
            yoloSavedImage = image;
        }
        private void DrawSelectingBox(List<YoloItem_custom> items, YoloItem_custom selectedItem = null)
            //Khi click chọn item nào trên bảng datagridresult thì trên imagebox sẽ highlight item đó
        {
            var imageInfo = this.GetCurrentImage();
            var image = Image.FromFile(imageInfo.Path);
            using (var canvas = Graphics.FromImage(image))
            {
                foreach (var item in items)
                {
                    var x = item.X;
                    var y = item.Y;
                    var width = item.Width;
                    var height = item.Height;

                    var font = new Font(FontFamily.GenericSansSerif, 20);
                    var brush = this.GetBrush(item.Confidence);
                    var penSize = image.Width / 100.0f;
                    var pen = new Pen(brush, penSize);
                    using (var overlayBrush = new SolidBrush(Color.FromArgb(150, 255, 255, 102)))
                    {
                        if (item.Equals(selectedItem))
                        {
                            canvas.FillRectangle(overlayBrush, x, y, width, height);
                        }
                        canvas.DrawRectangle(pen, x, y, width, height);
                        canvas.FillRectangle(brush, x - (penSize / 2), y - 15, width + penSize, 25);
                        canvas.DrawString(item.Type.ToString(), font, Brushes.White, item.X, item.Y - 20);
                    }

                }
                canvas.Flush();
            }
            var oldImage = this.pictureBox1.Image;
            this.pictureBox1.Image = image;
            oldImage?.Dispose();
        }

        int idxSaveImage = 0;
        private void buttonCapture_Click(object sender, EventArgs e)
        {
            //luu ảnh
            //string rgb_path = @"C:\Users\datbt\Desktop\code_all_ver3\Data anh\RGB\capture_" + idxSaveImage.ToString() + ".jpeg";
            //pictureBoxColor.Image.Save(rgb_path, ImageFormat.Jpeg);
            //idxSaveImage++;
            //xuất ra datagridviewFile
            //var imageInfos = new DirectoryImageReader().Analyze(@"C:\Users\datbt\Desktop\code_all_ver3\Data anh\RGB");
            dataGridViewFile.DataSource = 0;
            var imageInfos = new DirectoryImageReader().Analyze(@"H:\DATN\Trained file\data anh");
            this.dataGridViewFile.DataSource = imageInfos.ToList();
            this.btnYoloSave.Enabled = true;
            tabControl1.SelectedIndex = 1;

        }

        private void btnLoadYolo_Click(object sender, EventArgs e)
        {
            var GpuConfig = new GpuConfig_custom();
            toolStripStatusLabelYoloInfo.Text = "Initializing...";
            btnLoadYolo.Enabled = false;
            btnLoadYolo.Text = "Loading";
            Task.Run(() =>
            {
                var sw = new Stopwatch();
                sw.Start();
                notifyIconYoloInformation.ShowBalloonTip(5000, "YOLO Information", "Loading your YOLO", ToolTipIcon.None);
                YoloV3();
                Invoke(new Action(() =>
                {
                    btnDetect.Enabled = true;
                    btnLoadYolo.Enabled = false;
                    btnLoadYolo.Text = "Loaded";
                }));
                sw.Stop();
                var action = new MethodInvoker(delegate ()
                {
                    var deviceName = this.YOLO.GetGraphicDeviceName(GpuConfig);
                    this.toolStripStatusLabelYoloInfo.Text = $"Initialized YOLO in {sw.Elapsed.TotalMilliseconds:0} ms - Detection System: {this.YOLO.DetectionSystem} {deviceName} - Weights: YoloV3_custom.weights";
                });

                
                this.notifyIconYoloInformation.ShowBalloonTip(8000, "YOLO Information","Loading completed \nGPU: Geforce GTX 1050 \nWeights File: YoloV3_custom.weights",ToolTipIcon.None);
                
                statusStrip1.Invoke(action);
                
               
            });
        }
        private void btnDetect_Click(object sender, EventArgs e)
        {
            this.DetectSelectedImage();
        }

        private void dataGridViewFile_SelectionChanged(object sender, EventArgs e)
        {
            var oldImage = this.pictureBox1.Image;
            var imageInfo = this.GetCurrentImage();
            this.pictureBox1.Image = Image.FromFile(imageInfo.Path);
            oldImage?.Dispose();

            this.dataGridViewResult.DataSource = null;
            this.groupBoxResult.Text = $"Result";
        }

        private void dataGridViewResult_SelectionChanged(object sender, EventArgs e)
        {
            if (!dataGridViewResult.Focused)
            {
                return;
            }
            var items = dataGridViewResult.DataSource as List<YoloItem_custom>;
            var selectedItem = dataGridViewResult.CurrentRow?.DataBoundItem as YoloItem_custom;
            DrawSelectingBox(items, selectedItem);
        }

        List<YoloItem_custom> yoloitems = new List<YoloItem_custom>();
        List<vtkPoints> pointyolo; 
        double[] pt;
        private void btnYoloExtract_Click(object sender, EventArgs e)
        {
            Console.OutputEncoding = Encoding.Unicode;
            pt = new double[3];  //tạo trước để tối ưu tài nguyên, thay đổi giá trị liên tục
            pointyolo = new List<vtkPoints>();
            //vtkPolyData poly = vtkPolyData.New();
            Console.WriteLine("====================");
            foreach (var item in yoloitems)   //for mẹ dùng để load từng phần tử trong yoloitems
            {
                vtkPoints output = vtkPoints.New();
                for(int i=0;i<pc_Idx.Count;i++)   //for con dùng để load từng điểm trong cái chuỗi pc_Idx mà mình lấy được ở bên trên
                {
                    if (item.X <= COlorX[i] && COlorX[i] <= item.X + item.Width && item.Y<=COlorY[i] && COlorY[i]<=item.Y+item.Height)
                    {
                        pt = scenePolyData.GetPoint(pc_Idx[i]);
                        output.InsertNextPoint(pt[0], pt[1], pt[2]);  // khi lưu bằng InsertNextPoint thì giá trị đầu không bị mất
                    }
                }
                pointyolo.Add(output);   //cái pointyolo chứa các cụm đám mây điểm đã được phân đoạn
            }
            
            Renderer.RemoveAllViewProps();
            Console.WriteLine("Số vật phát hiện được = " + pointyolo.Count);
            Console.WriteLine("Số điểm của mỗi vật là: ");
            for(int i = 0; i < pointyolo.Count; i++)
            {
                int j = i + 1;
                Console.WriteLine("Vật " + j + " = " + pointyolo[i].GetNumberOfPoints());
            }

            Poly = new List<vtkPolyData>();
            Gly = new List<vtkVertexGlyphFilter>();
            Mapper = new List<vtkMapper>();
            Actor = new List<vtkActor>();

            //HIỂN THỊ CÁC CỤM ĐÁM MÂY ĐIỂM SAU KHI PHÂN ĐOẠN
            //vtkPolyData Poly1 = vtkPolyData.New();
            //vtkVertexGlyphFilter Gly1 = vtkVertexGlyphFilter.New();
            //vtkPolyDataMapper Mapper1 = vtkPolyDataMapper.New();
            //vtkActor Actor1 = vtkActor.New();

            for (int m = 0; m < pointyolo.Count; m++)
            {
                vtkPolyData Poly1 = vtkPolyData.New();
                vtkVertexGlyphFilter Gly1 = vtkVertexGlyphFilter.New();
                vtkPolyDataMapper Mapper1 = vtkPolyDataMapper.New();
                vtkActor Actor1 = vtkActor.New();
                Poly1.SetPoints(pointyolo[m]);
                Gly1.SetInput(Poly1);
                Gly1.Update();
                Mapper1.SetInputConnection(Gly1.GetOutputPort());
                Actor1.SetMapper(Mapper1);
                if (m == 0)
                {
                    Actor1.GetProperty().SetColor(1.0, 0.0, 0.0);
                }
                if (m == 1)
                {
                    Actor1.GetProperty().SetColor(1.0, 0.5, 0.0);
                }
                if (m == 2)
                {
                    Actor1.GetProperty().SetColor(1.0, 0.5, 0.5);
                }
                if (m == 3)
                {
                    Actor1.GetProperty().SetColor(0.0, 1.0, 0.0);
                }
                if (m == 4)
                {
                    Actor1.GetProperty().SetColor(0.0, 1.0, 0.5);
                }
                if (m == 6)
                {
                    Actor1.GetProperty().SetColor(0.5, 1.0, 0.5);
                }
                if (m == 7)
                {
                    Actor1.GetProperty().SetColor(0.0, 0.0, 1.0);
                }
                if (m == 8)
                {
                    Actor1.GetProperty().SetColor(0.5, 0.0, 1.0);
                }
                if (m == 9)
                {
                    Actor1.GetProperty().SetColor(0.5, 0.5, 0.5);
                }
                if (m == 10)
                {
                    Actor1.GetProperty().SetColor(0.1, 0.1, 0.1);
                }
                if (m == 11)
                {
                    Actor1.GetProperty().SetColor(0.2, 0.2, 0.2);
                }
                if (m == 12)
                {
                    Actor1.GetProperty().SetColor(0.3, 0.3, 0.3);
                }
                if (m == 13)
                {
                    Actor1.GetProperty().SetColor(0.4, 0.4, 0.4);
                }
                if (m == 14)
                {
                    Actor1.GetProperty().SetColor(0.6, 0.6, 0.6);
                }
                if (m == 15)
                {
                    Actor1.GetProperty().SetColor(0.7, 0.7, 0.7);
                }
                if (m == 16)
                {
                    Actor1.GetProperty().SetColor(0.8, 0.8, 0.8);
                }
                if (m == 17)
                {
                    Actor1.GetProperty().SetColor(0.9, 0.9, 0.9);
                }
                if (m == 18)
                {
                    Actor1.GetProperty().SetColor(1.0, 1.0, 0.0);
                }
                if (m == 19)
                {
                    Actor1.GetProperty().SetColor(1.0, 0.0, 1.0);
                }
                if (m == 20)
                {
                    Actor1.GetProperty().SetColor(0.0, 1.0, 1.0);
                }
                else
                {
                    Actor1.GetProperty().SetColor(m * 1.0 / point1.Count, 1 - m * 1.0 / point1.Count, 0.0);
                }
                Renderer.AddActor(Actor1);
                //RenderWindow.AddRenderer(Renderer);

                Poly.Add(Poly1);
                Gly.Add(Gly1);
                Mapper.Add(Mapper1);
                Actor.Add(Actor1);
            }           
            RenderWindow.Render();
        }

        Image yoloSavedImage;
        int idxSaveImageYolo = 1;
        private void buttonSave_Click(object sender, EventArgs e)
        {
            //lưu ảnh
            
            string rgb_path = @"H:\DATN\Trained file\Detected image\Detected_image_" + idxSaveImageYolo.ToString() + ".jpeg";
            yoloSavedImage.Save(rgb_path, ImageFormat.Jpeg);
            idxSaveImageYolo++;
        }

        

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (YOLO != null) 
            {
                YOLO.Dispose();
            }
        }



        //NHẬN DIỆN GIỌNG NÓI, SỬ DỤNG ĐỂ CHỌN VẬT SAU KHI ĐƯỢC PHÁT HIỆN 

        SpeechRecognitionEngine recEngine = new SpeechRecognitionEngine();
        /*
         foreach(var item in yoloitems)
            {
                if(speech==item.Type)
                    {
                        if(item.Type ==apple)
                            {
                                 
                            }
                    }      
            }
         */

        void recEngine_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)   //ĐỪNG NGHĨ TRIỆT ĐỂ, VIẾT ĐƯỢC CÁI XƯƠNG SỐNG ĐÃ RỒI TÍNH TIẾP!!
        //khởi tạo cho sự kiện recEngine.SpeechRecognized 
        {
            Console.OutputEncoding = Encoding.UTF8;
            string speech = e.Result.Text;
            pt = new double[3];
            vtkPoints output = vtkPoints.New();
            notifyIconVoiceRecognition.ShowBalloonTip(5000, "Voice Recognition", "Searching object...", ToolTipIcon.None);
            foreach (var item in yoloitems)
            {
                if (item.Type != speech)
                {
                    MessageBox.Show("Sorry, Cannot find this kind of object!\nPlease try again...");
                    return;
                }
                else if (item.Type == speech)
                {
                    
                    for (int i = 0; i < pc_Idx.Count; i++)
                    {
                        if(item.X <= COlorX[i] && COlorX[i] <= item.X + item.Width && item.Y <= COlorY[i] && COlorY[i] <= item.Y + item.Height)
                        {
                            pt = scenePolyData.GetPoint(pc_Idx[i]);
                            output.InsertNextPoint(pt[0], pt[1], pt[2]);
                        }
                    }
                    Console.WriteLine("Số điểm trong vùng của vật [" + item.Type + "] là: " + output.GetNumberOfPoints());
                }
            }
            notifyIconVoiceRecognition.ShowBalloonTip(5000, "Voice Recognition", "Searching completed!", ToolTipIcon.None);
            Poly = new List<vtkPolyData>();
            Gly = new List<vtkVertexGlyphFilter>();
            Mapper = new List<vtkMapper>();
            Actor = new List<vtkActor>();

            vtkPolyData poly = vtkPolyData.New();
            vtkVertexGlyphFilter gly = vtkVertexGlyphFilter.New();
            vtkPolyDataMapper mapper = vtkPolyDataMapper.New();
            vtkActor actor = vtkActor.New();

            poly.SetPoints(output);
            gly.SetInput(poly);
            gly.Update();
            mapper.SetInputConnection(gly.GetOutputPort());
            actor.SetMapper(mapper);
            actor.GetProperty().SetColor(1.0, 0.5, 0.0);

            Poly.Add(poly);
            Gly.Add(gly);
            Mapper.Add(mapper);
            Actor.Add(actor);

            axisAlignedBoundingBox();
        }

        private void btnVoiceRecognitionStart_Click(object sender, EventArgs e)
        {
            recEngine.RecognizeAsync(RecognizeMode.Multiple);
            btnVoiceRecognitionStop.Enabled = true;
        }

        private void btnVoiceRecognitionStop_Click(object sender, EventArgs e)
        {
            recEngine.RecognizeAsyncStop();
            btnVoiceRecognitionStop.Enabled = false;
        }
    }
}
